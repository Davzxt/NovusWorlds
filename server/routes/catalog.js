const express = require('express');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

router.get('/', (req, res) => {
  const type = ['hat', 'face', 'shirt', 'pants'].includes(req.query.type) ? req.query.type : null;
  const q = `%${String(req.query.q || '').trim()}%`;
  const min = Number(req.query.min || 0);
  const max = Number(req.query.max || 100000000);
  const order = req.query.sort === 'cheap' ? 'price ASC' : req.query.sort === 'popular' ? 'sales_count DESC' : 'created_at DESC';
  const rows = db.prepare(`SELECT catalog_items.*, users.username AS creator FROM catalog_items JOIN users ON users.id = catalog_items.creator_id WHERE is_active = 1 AND name LIKE ? AND price BETWEEN ? AND ? ${type ? 'AND type = ?' : ''} ORDER BY ${order} LIMIT 80`).all(...(type ? [q, min, max, type] : [q, min, max]));
  res.json({ items: rows });
});

router.get('/:id', (req, res) => {
  const item = db.prepare('SELECT catalog_items.*, users.username AS creator FROM catalog_items JOIN users ON users.id = catalog_items.creator_id WHERE catalog_items.id = ?').get(req.params.id);
  if (!item) return res.status(404).json({ error: 'Item nao encontrado.' });
  item.hat_transform = JSON.parse(item.hat_transform || '{}');
  const owners = db.prepare('SELECT users.username, users.avatar_data FROM user_inventory JOIN users ON users.id = user_inventory.user_id WHERE item_id = ? LIMIT 12').all(req.params.id);
  res.json({ item, owners });
});

router.post('/:id/buy', requireAuth, (req, res) => {
  const item = db.prepare('SELECT * FROM catalog_items WHERE id = ? AND is_active = 1').get(req.params.id);
  const buyer = db.prepare('SELECT * FROM users WHERE id = ?').get(req.session.user.id);
  if (!item) return res.status(404).json({ error: 'Item nao encontrado.' });
  if (buyer.novux < item.price) return res.status(400).json({ error: 'Novux insuficiente.' });
  if (db.prepare('SELECT 1 FROM user_inventory WHERE user_id = ? AND item_id = ?').get(buyer.id, item.id)) return res.status(400).json({ error: 'Voce ja possui este item.' });
  const tx = db.transaction(() => {
    db.prepare('UPDATE users SET novux = novux - ? WHERE id = ?').run(item.price, buyer.id);
    if (item.creator_id) db.prepare('UPDATE users SET novux = novux + ? WHERE id = ?').run(Math.floor(item.price * 0.8), item.creator_id);
    db.prepare('INSERT INTO user_inventory (user_id, item_id) VALUES (?, ?)').run(buyer.id, item.id);
    db.prepare('UPDATE catalog_items SET sales_count = sales_count + 1 WHERE id = ?').run(item.id);
    db.prepare('INSERT INTO transactions (from_user_id, to_user_id, amount, type, description) VALUES (?, ?, ?, ?, ?)').run(buyer.id, item.creator_id, item.price, 'purchase', `Compra: ${item.name}`);
  });
  tx();
  res.json({ ok: true });
});

module.exports = router;
