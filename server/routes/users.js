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

router.get('/', (req, res) => {
  const q = String(req.query.q || '').trim();
  if (q.length < 2) return res.json({ users: [] });
  const users = db.prepare(`
    SELECT username, is_admin, created_at, last_login
    FROM users
    WHERE deleted_at IS NULL AND username LIKE ?
    ORDER BY username
    LIMIT 25
  `).all(`%${q}%`);
  res.json({ users });
});

router.get('/me/friends', requireAuth, (req, res) => {
  const pending = db.prepare(`
    SELECT f.id, u.username requester, f.created_at
    FROM friendships f
    JOIN users u ON u.id = f.requester_id
    WHERE f.receiver_id = ? AND f.status = 'pending'
    ORDER BY f.created_at DESC
  `).all(req.session.user.id);
  const friends = db.prepare(`
    SELECT f.id, u.username, u.last_login
    FROM friendships f
    JOIN users u ON u.id = CASE WHEN f.requester_id = ? THEN f.receiver_id ELSE f.requester_id END
    WHERE (f.requester_id = ? OR f.receiver_id = ?) AND f.status = 'accepted'
    ORDER BY u.username
  `).all(req.session.user.id, req.session.user.id, req.session.user.id);
  res.json({ pending, friends });
});

router.post('/friends/:id/action', requireAuth, (req, res) => {
  const id = Number(req.params.id);
  const friendship = db.prepare('SELECT * FROM friendships WHERE id = ?').get(id);
  if (!friendship || friendship.receiver_id !== req.session.user.id) return res.status(404).json({ error: 'Pedido nao encontrado.' });
  const action = req.body.action === 'accept' ? 'accepted' : 'declined';
  db.prepare('UPDATE friendships SET status = ? WHERE id = ?').run(action, id);
  res.json({ ok: true });
});

router.post('/:username/friend', requireAuth, (req, res) => {
  const target = db.prepare('SELECT id FROM users WHERE username = ?').get(req.params.username);
  if (!target || target.id === req.session.user.id) return res.status(400).json({ error: 'Pedido invalido.' });
  db.prepare('INSERT OR IGNORE INTO friendships (requester_id, receiver_id) VALUES (?, ?)').run(req.session.user.id, target.id);
  res.json({ ok: true });
});

module.exports = router;
