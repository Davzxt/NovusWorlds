const express = require('express');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

router.get('/:username', (req, res) => {
  const user = db.prepare('SELECT id, username, novux, is_admin, avatar_data, created_at FROM users WHERE username = ? AND deleted_at IS NULL').get(req.params.username);
  if (!user) return res.status(404).json({ error: 'Usuario nao encontrado.' });
  user.avatar_data = JSON.parse(user.avatar_data || '{}');
  const games = db.prepare('SELECT id, title, thumbnail_url, visit_count FROM games WHERE creator_id = ? AND is_active = 1 ORDER BY created_at DESC').all(user.id);
  const collection = db.prepare('SELECT catalog_items.* FROM user_inventory JOIN catalog_items ON catalog_items.id = user_inventory.item_id WHERE user_id = ?').all(user.id);
  const friends = db.prepare("SELECT u.username, u.last_login FROM friendships f JOIN users u ON u.id = CASE WHEN f.requester_id = ? THEN f.receiver_id ELSE f.requester_id END WHERE (f.requester_id = ? OR f.receiver_id = ?) AND f.status = 'accepted'").all(user.id, user.id, user.id);
  res.json({ user, games, collection, friends });
});

router.post('/:username/friend', requireAuth, (req, res) => {
  const target = db.prepare('SELECT id FROM users WHERE username = ?').get(req.params.username);
  if (!target || target.id === req.session.user.id) return res.status(400).json({ error: 'Pedido invalido.' });
  db.prepare('INSERT OR IGNORE INTO friendships (requester_id, receiver_id) VALUES (?, ?)').run(req.session.user.id, target.id);
  res.json({ ok: true });
});

module.exports = router;
