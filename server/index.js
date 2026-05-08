const http = require('http');
const path = require('path');
const express = require('express');
const session = require('express-session');
require('./db');

const auth = require('./routes/auth');
const games = require('./routes/games');
const catalog = require('./routes/catalog');
const avatar = require('./routes/avatar');
const users = require('./routes/users');
const admin = require('./routes/admin');
const economy = require('./routes/economy');
const forum = require('./routes/forum');
const { attachGameServer } = require('./websocket/gameServer');
const db = require('./db');

const app = express();
const server = http.createServer(app);
class SQLiteSessionStore extends session.Store {
  get(sid, cb) {
    try {
      const row = db.prepare('SELECT sess FROM sessions WHERE sid = ? AND expired > CURRENT_TIMESTAMP').get(sid);
      cb(null, row ? JSON.parse(row.sess) : null);
    } catch (err) { cb(err); }
  }
  set(sid, sess, cb) {
    try {
      const expires = new Date(sess.cookie?.expires || Date.now() + 24 * 60 * 60 * 1000).toISOString();
      db.prepare('INSERT INTO sessions (sid, sess, expired) VALUES (?, ?, ?) ON CONFLICT(sid) DO UPDATE SET sess = excluded.sess, expired = excluded.expired').run(sid, JSON.stringify(sess), expires);
      cb?.();
    } catch (err) { cb?.(err); }
  }
  destroy(sid, cb) {
    try { db.prepare('DELETE FROM sessions WHERE sid = ?').run(sid); cb?.(); } catch (err) { cb?.(err); }
  }
}

const sessionMiddleware = session({
  store: new SQLiteSessionStore(),
  secret: process.env.SESSION_SECRET || 'novus-worlds-2008-change-me',
  resave: false,
  saveUninitialized: false,
  cookie: { sameSite: 'lax', maxAge: 24 * 60 * 60 * 1000 }
});

app.use(express.json({ limit: '10mb' }));
app.use(express.urlencoded({ extended: true }));
app.use(sessionMiddleware);
app.use('/admin', (req, res, next) => {
  if (req.path.endsWith('.html') || req.path === '/' || req.path === '') {
    if (!req.session.user || !req.session.user.is_admin) return res.redirect('/login.html');
  }
  next();
});
app.use('/vendor/three', express.static(path.join(__dirname, '..', 'node_modules', 'three')));
app.use(express.static(path.join(__dirname, '..', 'public')));

app.use('/api/auth', auth);
app.use('/api/games', games);
app.use('/api/catalog', catalog);
app.use('/api/avatar', avatar);
app.use('/api/users', users);
app.use('/api/admin', admin);
app.use('/api/economy', economy);
app.use('/api/forum', forum);

app.get('/health', (req, res) => res.json({ ok: true, name: 'Novus Worlds' }));
app.get('/admin', (req, res) => res.redirect('/admin/index.html'));
app.get('*', (req, res) => res.sendFile(path.join(__dirname, '..', 'public', 'index.html')));

attachGameServer(server);

const port = process.env.PORT || 3000;
server.listen(port, () => console.log(`Novus Worlds running on port ${port}`));
