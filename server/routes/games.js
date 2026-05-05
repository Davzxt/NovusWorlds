const express = require('express');
const { prepare } = require('../db');

const router = express.Router();

router.get('/', (req, res) => {
  const { sort = 'recent', search = '' } = req.query;
  
  let orderBy = 'created_at DESC';
  if (sort === 'popular') orderBy = 'visit_count DESC';
  if (sort === 'players') orderBy = 'max_players DESC';

  let where = 'WHERE is_active = 1';
  const params = [];
  
  if (search) {
    where += ' AND title LIKE ?';
    params.push(`%${search}%`);
  }

  const games = prepare(`
    SELECT g.id, g.title, g.description, g.thumbnail_url, g.visit_count, g.max_players, g.created_at,
           u.username as creator_username
    FROM games g
    JOIN users u ON g.creator_id = u.id
    ${where}
    ORDER BY ${orderBy}
    LIMIT 50
  `).all(...params);

  res.json({ games });
});

router.get('/featured', (req, res) => {
  const games = prepare(`
    SELECT g.id, g.title, g.description, g.thumbnail_url, g.visit_count, g.max_players,
           u.username as creator_username
    FROM games g
    JOIN users u ON g.creator_id = u.id
    WHERE g.is_active = 1 AND g.is_featured = 1
    ORDER BY g.visit_count DESC
    LIMIT 6
  `).all();

  res.json({ games });
});

router.get('/:id', (req, res) => {
  const game = prepare(`
    SELECT g.*, u.username as creator_username
    FROM games g
    JOIN users u ON g.creator_id = u.id
    WHERE g.id = ?
  `).get(req.params.id);

  if (!game) {
    return res.status(404).json({ error: 'Game not found' });
  }

  res.json({ game });
});

router.post('/', (req, res) => {
  const { requireAuth } = require('../middleware/auth');
  const { title, description, map_data, max_players = 20 } = req.body;

  if (!title || !map_data) {
    return res.status(400).json({ error: 'Title and map data required' });
  }

  const result = prepare(`
    INSERT INTO games (title, description, creator_id, map_data, max_players)
    VALUES (?, ?, ?, ?, ?)
  `).run(title, description || '', req.session.userId, JSON.stringify(map_data), max_players);

  res.json({ success: true, gameId: result.lastInsertRowid });
});

router.put('/:id', (req, res) => {
  const game = prepare('SELECT * FROM games WHERE id = ? AND creator_id = ?')
    .get(req.params.id, req.session.userId);

  if (!game) {
    return res.status(404).json({ error: 'Game not found or access denied' });
  }

  const { title, description, map_data, max_players, is_active } = req.body;

  prepare(`
    UPDATE games SET
      title = COALESCE(?, title),
      description = COALESCE(?, description),
      map_data = COALESCE(?, map_data),
      max_players = COALESCE(?, max_players),
      is_active = COALESCE(?, is_active),
      updated_at = CURRENT_TIMESTAMP
    WHERE id = ?
  `).run(title, description, map_data, max_players, is_active, req.params.id);

  res.json({ success: true });
});

router.delete('/:id', (req, res) => {
  const game = prepare('SELECT * FROM games WHERE id = ? AND creator_id = ?')
    .get(req.params.id, req.session.userId);

  if (!game) {
    return res.status(404).json({ error: 'Game not found or access denied' });
  }

  prepare('DELETE FROM games WHERE id = ?').run(req.params.id);

  res.json({ success: true });
});

router.get('/user/:userId', (req, res) => {
  const games = prepare(`
    SELECT g.id, g.title, g.description, g.thumbnail_url, g.visit_count, g.max_players, g.created_at
    FROM games g
    WHERE g.creator_id = ? AND g.is_active = 1
    ORDER BY g.created_at DESC
  `).all(req.params.userId);

  res.json({ games });
});

router.post('/:id/play', (req, res) => {
  const game = prepare('SELECT * FROM games WHERE id = ? AND is_active = 1')
    .get(req.params.id);

  if (!game) {
    return res.status(404).json({ error: 'Game not found' });
  }

  prepare('UPDATE games SET visit_count = visit_count + 1 WHERE id = ?').run(req.params.id);

  res.json({ success: true, gameId: game.id, mapData: JSON.parse(game.map_data) });
});

module.exports = router;