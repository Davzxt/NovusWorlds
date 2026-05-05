const express = require('express');
const { prepare } = require('../db');
const { requireAuth } = require('../middleware/auth');

const router = express.Router();

const STRIPE_SECRET_KEY = process.env.STRIPE_SECRET_KEY;
let stripe = null;
if (STRIPE_SECRET_KEY) {
  stripe = require('stripe')(STRIPE_SECRET_KEY);
}

router.get('/balance', requireAuth, (req, res) => {
  const user = prepare('SELECT novux FROM users WHERE id = ?').get(req.session.userId);
  res.json({ novux: user.novux });
});

router.post('/donate', requireAuth, async (req, res) => {
  if (!stripe) {
    return res.status(503).json({ error: 'Payment processing unavailable' });
  }

  const { amount = 1 } = req.body;

  try {
    const session = await stripe.checkout.sessions.create({
      payment_method_types: ['card'],
      line_items: [{
        price_data: {
          currency: 'usd',
          product_data: {
            name: 'Donation to Novus Worlds',
            description: 'Support the platform'
          },
          unit_amount: amount * 100
        },
        quantity: 1
      }],
      mode: 'payment',
      success_url: `${req.headers.origin}/profile?donation=success`,
      cancel_url: `${req.headers.origin}/profile?donation=cancelled`,
      metadata: {
        userId: req.session.userId.toString()
      }
    });

    res.json({ sessionId: session.id, url: session.url });
  } catch (error) {
    console.error('Stripe error:', error);
    res.status(500).json({ error: 'Payment failed' });
  }
});

router.post('/promocode', requireAuth, (req, res) => {
  const { code } = req.body;

  if (!code) {
    return res.status(400).json({ error: 'Code required' });
  }

  const promo = prepare(`
    SELECT * FROM promo_codes WHERE code = ? AND uses_remaining > 0
    AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP)
  `).get(code.toUpperCase());

  if (!promo) {
    return res.status(400).json({ error: 'Invalid or expired code' });
  }

  prepare('UPDATE users SET novux = novux + ? WHERE id = ?')
    .run(promo.novux_amount, req.session.userId);

  prepare('UPDATE promo_codes SET uses_remaining = uses_remaining - 1 WHERE id = ?')
    .run(promo.id);

  prepare(`
    INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
    VALUES (NULL, ?, ?, 'promocode', 'Promo code: ?')
  `).run(req.session.userId, promo.novux_amount, code);

  res.json({ success: true, novux: promo.novux_amount });
});

router.get('/transactions', requireAuth, (req, res) => {
  const transactions = prepare(`
    SELECT t.*, 
           CASE 
             WHEN t.to_user_id = ? THEN 'received'
             ELSE 'sent'
           END as direction
    FROM transactions t
    WHERE t.from_user_id = ? OR t.to_user_id = ?
    ORDER BY t.created_at DESC
    LIMIT 50
  `).all(req.session.userId, req.session.userId, req.session.userId);

  res.json({ transactions });
});

router.post('/transfer', requireAuth, (req, res) => {
  const { toUsername, amount } = req.body;
  const amountInt = parseInt(amount);

  if (!toUsername || !amount || amountInt <= 0) {
    return res.status(400).json({ error: 'Invalid username or amount' });
  }

  const recipient = prepare('SELECT id, novux FROM users WHERE username = ?').get(toUsername);
  if (!recipient) {
    return res.status(404).json({ error: 'User not found' });
  }

  const sender = prepare('SELECT novux FROM users WHERE id = ?').get(req.session.userId);

  if (sender.novux < amountInt) {
    return res.status(400).json({ error: 'Not enough Novux' });
  }

  prepare('UPDATE users SET novux = novux - ? WHERE id = ?')
    .run(amountInt, req.session.userId);
  prepare('UPDATE users SET novux = novux + ? WHERE id = ?')
    .run(amountInt, recipient.id);

  prepare(`
    INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
    VALUES (?, ?, ?, 'transfer', 'Transfer to ?')
  `).run(req.session.userId, recipient.id, amountInt, toUsername);

  res.json({ success: true });
});

module.exports = router;