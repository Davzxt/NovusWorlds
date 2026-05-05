const express = require('express');
const { prepare } = require('../db');
const { requireAuth } = require('../middleware/auth');

const router = express.Router();

router.get('/:username', (req, res) => {
  const user = prepare(`
    SELECT id, username, novux, is_admin, avatar_data, created_at
    FROM users WHERE username = ?
  `).get(req.params.username);

  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }

  const games = prepare(`
    SELECT id, title, description, thumbnail_url, visit_count, max_players, created_at
    FROM games WHERE creator_id = ? AND is_active = 1
    ORDER BY created_at DESC
    LIMIT 10
  `).all(user.id);

  const inventory = prepare(`
    SELECT ci.*
    FROM user_inventory ui
    JOIN catalog_items ci ON ui.item_id = ci.id
    WHERE ui.user_id = ?
    ORDER BY ui.purchased_at DESC
  `).all(user.id);

  res.json({
    user: {
      id: user.id,
      username: user.username,
      novux: user.novux,
      isAdmin: user.is_admin === 1,
      createdAt: user.created_at,
      avatarData: JSON.parse(user.avatar_data || '{}')
    },
    games,
    inventory
  });
});

router.post('/friend/add', requireAuth, (req, res) => {
  const { username } = req.body;
  const targetUser = prepare('SELECT id FROM users WHERE username = ?').get(username);

  if (!targetUser) {
    return res.status(404).json({ error: 'User not found' });
  }

  if (targetUser.id === req.session.userId) {
    return res.status(400).json({ error: 'Cannot add yourself' });
  }

  const existing = prepare(`
    SELECT * FROM friendships 
    WHERE (requester_id = ? AND receiver_id = ?) OR (requester_id = ? AND receiver_id = ?)
  `).get(req.session.userId, targetUser.id, targetUser.id, req.session.userId);

  if (existing) {
    if (existing.status === 'accepted') {
      return res.status(400).json({ error: 'Already friends' });
    }
    if (existing.status === 'pending' && existing.requester_id === req.session.userId) {
      return res.status(400).json({ error: 'Friend request already sent' });
    }
  }

  prepare(`
    INSERT INTO friendships (requester_id, receiver_id, status)
    VALUES (?, ?, 'pending')
  `).run(req.session.userId, targetUser.id);

  res.json({ success: true });
});

router.post('/friend/accept', requireAuth, (req, res) => {
  const { requestId } = req.body;

  const friendship = prepare(`
    SELECT * FROM friendships WHERE id = ? AND receiver_id = ? AND status = 'pending'
  `).get(requestId, req.session.userId);

  if (!friendship) {
    return res.status(404).json({ error: 'Friend request not found' });
  }

  prepare(`UPDATE friendships SET status = 'accepted' WHERE id = ?`).run(requestId);

  res.json({ success: true });
});

router.post('/friend/decline', requireAuth, (req, res) => {
  const { requestId } = req.body;

  const friendship = prepare(`
    SELECT * FROM friendships WHERE id = ? AND receiver_id = ? AND status = 'pending'
  `).get(requestId, req.session.userId);

  if (!friendship) {
    return res.status(404).json({ error: 'Friend request not found' });
  }

  prepare(`DELETE FROM friendships WHERE id = ?`).run(requestId);

  res.json({ success: true });
});

router.post('/friend/remove', requireAuth, (req, res) => {
  const { friendId } = req.body;

  prepare(`DELETE FROM friendships WHERE id = ?`).run(friendId);

  res.json({ success: true });
});

router.get('/friends', requireAuth, (req, res) => {
  const friendships = prepare(`
    SELECT f.id, f.status, f.created_at,
           CASE 
             WHEN f.requester_id = ? THEN u2.id 
             ELSE u1.id 
           END as friend_id,
           CASE 
             WHEN f.requester_id = ? THEN u2.username 
             ELSE u1.username 
           END as friend_username,
           u.last_login
    FROM friendships f
    JOIN users u1 ON f.requester_id = u1.id
    JOIN users u2 ON f.receiver_id = u2.id
    WHERE (f.requester_id = ? OR f.receiver_id = ?) AND f.status = 'accepted'
  `).all(req.session.userId, req.session.userId, req.session.userId, req.session.userId);

  const pendingRequests = prepare(`
    SELECT f.id, f.created_at, u.id as user_id, u.username
    FROM friendships f
    JOIN users u ON f.requester_id = u.id
    WHERE f.receiver_id = ? AND f.status = 'pending'
  `).all(req.session.userId);

  res.json({ friendships, pendingRequests });
});

module.exports = router;