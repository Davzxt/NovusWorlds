const express = require('express');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

router.get('/transactions', requireAuth, (req, res) => {
  const rows = db.prepare('SELECT * FROM transactions WHERE from_user_id = ? OR to_user_id = ? ORDER BY created_at DESC LIMIT 50').all(req.session.user.id, req.session.user.id);
  res.json({ transactions: rows });
});

router.post('/redeem', requireAuth, (req, res) => {
  const code = String(req.body.code || '').trim().toUpperCase();
  const promo = db.prepare('SELECT * FROM promo_codes WHERE code = ? AND (expires_at IS NULL OR expires_at > CURRENT_TIMESTAMP) AND (uses_remaining IS NULL OR uses_remaining > 0)').get(code);
  if (!promo) return res.status(400).json({ error: 'Codigo invalido ou expirado.' });
  db.prepare('UPDATE users SET novux = novux + ? WHERE id = ?').run(promo.novux_amount, req.session.user.id);
  db.prepare('UPDATE promo_codes SET uses_remaining = CASE WHEN uses_remaining IS NULL THEN NULL ELSE uses_remaining - 1 END WHERE id = ?').run(promo.id);
  db.prepare('INSERT INTO transactions (to_user_id, amount, type, description) VALUES (?, ?, ?, ?)').run(req.session.user.id, promo.novux_amount, 'promo', `Codigo ${code}`);
  res.json({ ok: true, amount: promo.novux_amount });
});

module.exports = router;
