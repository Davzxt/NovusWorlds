const express = require('express');
const { v4: uuid } = require('uuid');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

function defaultMap(title = 'Novo Mundo') {
  return {
    name: title,
    version: 1,
    objects: [{ id: uuid(), type: 'Part', name: 'Baseplate', position: { x: 0, y: -0.5, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, size: { x: 80, y: 1, z: 80 }, color: '#6B8E23', material: 'Grass', anchored: true, canCollide: true, transparency: 0, children: [] }],
    spawnPoints: [{ x: 0, y: 3, z: 0 }],
    ambient: '#404040',
    skyColor: '#87CEEB'
  };
}

router.get('/', (req, res) => {
  const q = `%${String(req.query.q || '').trim()}%`;
  const order = req.query.sort === 'recent' ? 'created_at DESC' : req.query.sort === 'featured' ? 'is_featured DESC, visit_count DESC' : 'visit_count DESC';
  const rows = db.prepare(`SELECT games.*, users.username AS creator FROM games JOIN users ON users.id = games.creator_id WHERE games.is_active = 1 AND games.title LIKE ? ORDER BY ${order} LIMIT 60`).all(q);
  res.json({ games: rows });
});

router.get('/mine/list', requireAuth, (req, res) => {
  const rows = db.prepare('SELECT id, title, description, thumbnail_url, is_active, visit_count, updated_at FROM games WHERE creator_id = ? ORDER BY updated_at DESC').all(req.session.user.id);
  res.json({ games: rows });
});

router.get('/:id', (req, res) => {
  const row = db.prepare('SELECT games.*, users.username AS creator FROM games JOIN users ON users.id = games.creator_id WHERE games.id = ?').get(req.params.id);
  if (!row) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  row.map_data = JSON.parse(row.map_data);
  const votes = db.prepare('SELECT SUM(value = 1) likes, SUM(value = -1) dislikes FROM game_votes WHERE game_id = ?').get(req.params.id);
  row.likes = votes.likes || 0;
  row.dislikes = votes.dislikes || 0;
  res.json({ game: row });
});

router.post('/', requireAuth, (req, res) => {
  const title = String(req.body.title || 'Novo Mundo').trim().slice(0, 80);
  const description = String(req.body.description || '').trim().slice(0, 500);
  const map = req.body.map_data || defaultMap(title);
  const publish = req.body.publish === true || req.body.publish === 'true';
  const info = db.prepare('INSERT INTO games (title, description, creator_id, map_data, thumbnail_url, is_active) VALUES (?, ?, ?, ?, ?, ?)').run(title, description, req.session.user.id, JSON.stringify(map, null, 2), req.body.thumbnail_url || '/assets/textures/game-default.svg', publish ? 1 : 0);
  db.prepare('INSERT INTO activity_log (type, message) VALUES (?, ?)').run('game', publish ? `Jogo publicado: ${title}` : `Mapa salvo: ${title}`);
  res.json({ id: info.lastInsertRowid });
});

router.put('/:id', requireAuth, (req, res) => {
  const game = db.prepare('SELECT * FROM games WHERE id = ?').get(req.params.id);
  if (!game || (game.creator_id !== req.session.user.id && !req.session.user.is_admin)) return res.status(403).json({ error: 'Sem permissao.' });
  const publish = req.body.publish === true || req.body.publish === 'true';
  db.prepare('UPDATE games SET title = ?, description = ?, map_data = ?, thumbnail_url = COALESCE(?, thumbnail_url), is_active = CASE WHEN ? THEN 1 ELSE is_active END, updated_at = CURRENT_TIMESTAMP WHERE id = ?')
    .run(String(req.body.title || game.title).slice(0, 80), String(req.body.description || game.description || '').slice(0, 500), JSON.stringify(req.body.map_data || JSON.parse(game.map_data), null, 2), req.body.thumbnail_url, publish ? 1 : 0, req.params.id);
  res.json({ ok: true });
});

router.post('/:id/vote', requireAuth, (req, res) => {
  const value = req.body.value === -1 ? -1 : 1;
  db.prepare('INSERT INTO game_votes (user_id, game_id, value) VALUES (?, ?, ?) ON CONFLICT(user_id, game_id) DO UPDATE SET value = excluded.value').run(req.session.user.id, req.params.id, value);
  res.json({ ok: true });
});

module.exports = router;
