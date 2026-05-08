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
const { attachGameServer } = require('./websocket/gameServer');
const { attachChatServer } = require('./websocket/chatServer');

const app = express();
const server = http.createServer(app);
const sessionMiddleware = session({
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
app.use(express.static(path.join(__dirname, '..', 'public')));

app.use('/api/auth', auth);
app.use('/api/games', games);
app.use('/api/catalog', catalog);
app.use('/api/avatar', avatar);
app.use('/api/users', users);
app.use('/api/admin', admin);
app.use('/api/economy', economy);

app.get('/health', (req, res) => res.json({ ok: true, name: 'Novus Worlds' }));
app.get('/admin', (req, res) => res.redirect('/admin/index.html'));
app.get('*', (req, res) => res.sendFile(path.join(__dirname, '..', 'public', 'index.html')));

attachGameServer(server);
attachChatServer(server);

const port = process.env.PORT || 3000;
server.listen(port, () => console.log(`Novus Worlds running on port ${port}`));
