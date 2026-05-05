const express = require('express');
const session = require('express-session');
const path = require('path');
const http = require('http');
const { WebSocketServer } = require('ws');
const { db, initializeDatabase } = require('./db');

const authRoutes = require('./routes/auth');
const gamesRoutes = require('./routes/games');
const catalogRoutes = require('./routes/catalog');
const avatarRoutes = require('./routes/avatar');
const usersRoutes = require('./routes/users');
const economyRoutes = require('./routes/economy');
const adminRoutes = require('./routes/admin');

const { setupGameWebSocket } = require('./websocket/gameServer');
const { setupChatWebSocket } = require('./websocket/chatServer');

const app = express();
const server = http.createServer(app);

const PORT = process.env.PORT || 3000;
const STRIPE_SECRET_KEY = process.env.STRIPE_SECRET_KEY;
const SESSION_SECRET = process.env.SESSION_SECRET || 'novus-worlds-secret-key-2024';

app.use(express.json());
app.use(express.urlencoded({ extended: true }));

app.use(session({
  secret: SESSION_SECRET,
  resave: false,
  saveUninitialized: false,
  cookie: {
    maxAge: 1000 * 60 * 60 * 24 * 30,
    httpOnly: true,
    secure: process.env.NODE_ENV === 'production'
  }
}));

app.use(express.static(path.join(__dirname, '../public')));

app.use('/api/auth', authRoutes);
app.use('/api/games', gamesRoutes);
app.use('/api/catalog', catalogRoutes);
app.use('/api/avatar', avatarRoutes);
app.use('/api/users', usersRoutes);
app.use('/api/economy', economyRoutes);
app.use('/api/admin', adminRoutes);

app.get('/', (req, res) => {
  res.sendFile(path.join(__dirname, '../public/index.html'));
});

app.use((req, res) => {
  res.status(404).sendFile(path.join(__dirname, '../public/index.html'));
});

app.use((err, req, res, next) => {
  console.error(err.stack);
  res.status(500).json({ error: 'Something went wrong!' });
});

const wss = new WebSocketServer({ server, path: '/ws/game' });
const chatWss = new WebSocketServer({ server, path: '/ws/chat' });

setupGameWebSocket(wss, db);
setupChatWebSocket(chatWss, db);

server.listen(PORT, () => {
  initializeDatabase();
  console.log(`Novus Worlds server running on port ${PORT}`);
  console.log(`WebSocket game server running on /ws/game`);
  console.log(`WebSocket chat server running on /ws/chat`);
  if (STRIPE_SECRET_KEY) {
    console.log(`Stripe payment integration enabled`);
  } else {
    console.log(`WARNING: STRIPE_SECRET_KEY not set - donations disabled`);
  }
});

module.exports = { app, server, db };