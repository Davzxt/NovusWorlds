const express = require('express');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

router.get('/threads', (req, res) => {
  const threads = db.prepare(`
    SELECT forum_threads.*, users.username,
      (SELECT COUNT(*) FROM forum_posts WHERE forum_posts.thread_id = forum_threads.id) AS replies
    FROM forum_threads
    JOIN users ON users.id = forum_threads.user_id
    ORDER BY updated_at DESC
    LIMIT 50
  `).all();
  res.json({ threads });
});

router.post('/threads', requireAuth, (req, res) => {
  const title = String(req.body.title || '').trim().slice(0, 80);
  const body = String(req.body.body || '').trim().slice(0, 2000);
  if (title.length < 3 || body.length < 3) return res.status(400).json({ error: 'Titulo e texto sao obrigatorios.' });
  const info = db.prepare('INSERT INTO forum_threads (user_id, title, body) VALUES (?, ?, ?)').run(req.session.user.id, title, body);
  res.json({ id: info.lastInsertRowid });
});

router.get('/threads/:id', (req, res) => {
  const thread = db.prepare('SELECT forum_threads.*, users.username FROM forum_threads JOIN users ON users.id = forum_threads.user_id WHERE forum_threads.id = ?').get(req.params.id);
  if (!thread) return res.status(404).json({ error: 'Topico nao encontrado.' });
  const posts = db.prepare('SELECT forum_posts.*, users.username FROM forum_posts JOIN users ON users.id = forum_posts.user_id WHERE thread_id = ? ORDER BY created_at ASC').all(req.params.id);
  res.json({ thread, posts });
});

router.post('/threads/:id/posts', requireAuth, (req, res) => {
  const body = String(req.body.body || '').trim().slice(0, 2000);
  if (body.length < 2) return res.status(400).json({ error: 'Mensagem vazia.' });
  db.prepare('INSERT INTO forum_posts (thread_id, user_id, body) VALUES (?, ?, ?)').run(req.params.id, req.session.user.id, body);
  db.prepare('UPDATE forum_threads SET updated_at = CURRENT_TIMESTAMP WHERE id = ?').run(req.params.id);
  res.json({ ok: true });
});

module.exports = router;
