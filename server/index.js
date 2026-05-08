const express = require('express');
const session = require('express-session');
const path = require('path');
const http = require('http');
const { WebSocketServer } = require('ws');
const { initializeDatabase, prepare, saveDatabase } = require('./db');
const bcrypt = require('bcrypt');

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
const SESSION_SECRET = process.env.SESSION_SECRET || 'novus-secret-key-2024';
const isProduction = process.env.NODE_ENV === 'production';

app.use(express.json());
app.use(express.urlencoded({ extended: true }));

app.use(session({
  secret: SESSION_SECRET,
  resave: false,
  saveUninitialized: false,
  cookie: {
    maxAge: 1000 * 60 * 60 * 24 * 30,
    httpOnly: true,
    secure: isProduction,
    sameSite: isProduction ? 'none' : 'lax'
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

app.get('/admin/catalog', (req, res) => res.sendFile(path.join(__dirname, '../public/admin/catalog.html')));
app.get('/settings', (req, res) => res.sendFile(path.join(__dirname, '../public/settings.html')));
app.get('/friends', (req, res) => res.sendFile(path.join(__dirname, '../public/friends.html')));
app.get('/chat', (req, res) => res.sendFile(path.join(__dirname, '../public/chat.html')));
app.get('/profile', (req, res) => res.sendFile(path.join(__dirname, '../public/profile.html')));
app.get('/avatar', (req, res) => res.sendFile(path.join(__dirname, '../public/avatar.html')));
app.get('/studio', (req, res) => res.sendFile(path.join(__dirname, '../public/studio.html')));
app.get('/admin', (req, res) => res.sendFile(path.join(__dirname, '../public/admin/index.html')));
app.get('/', (req, res) => res.sendFile(path.join(__dirname, '../public/index.html')));

app.use((req, res) => {
  res.status(404).sendFile(path.join(__dirname, '../public/index.html'));
});

app.use((err, req, res, next) => {
  console.error(err.stack);
  res.status(500).json({ error: 'Something went wrong!' });
});

async function doSeed() {
  const existingAdmin = prepare('SELECT id FROM users WHERE username = ?').get('NovusWorlds');
  let adminId = existingAdmin ? existingAdmin.id : null;
  
  if (!existingAdmin) {
    const passwordHash = await bcrypt.hash('admin2008', 12);
    prepare(`INSERT INTO users (username, password_hash, email, novux, is_admin, avatar_data) VALUES (?, ?, ?, 10000, 1, '{"head":"#f5d0a8","torso":"#f5d0a8","left_arm":"#f5d0a8","right_arm":"#f5d0a8","left_leg":"#f5d0a8","right_leg":"#f5d0a8","hat":null,"face":null,"shirt":null,"pants":null}')`).run('NovusWorlds', passwordHash, 'admin@novusworlds.com');
    adminId = prepare('SELECT last_insert_rowid() as id').get().id;
    console.log('Admin created: NovusWorlds / admin2008');
  }

  const testGame = prepare("SELECT id FROM games WHERE title = ?").get('Physics Test World');
  if (!testGame && adminId) {
    const mapData = JSON.stringify({
      name: 'Physics Test World',
      version: 1,
      objects: [
        { id: 'baseplate', type: 'Part', name: 'Baseplate', position: { x: 0, y: -1, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, size: { x: 100, y: 2, z: 100 }, color: '#6B8E23', material: 'Grass', anchored: true, canCollide: true, transparency: 0 },
        { id: 'ramp1', type: 'Part', name: 'Ramp', position: { x: 10, y: 2, z: 10 }, rotation: { x: 0, y: 45, z: 15 }, size: { x: 10, y: 1, z: 15 }, color: '#888888', material: 'Stone', anchored: true, canCollide: true, transparency: 0 }
      ],
      spawnPoints: [{ x: 0, y: 3, z: 20 }],
      ambient: '#404040',
      skyColor: '#87CEEB'
    });
    prepare('INSERT INTO games (title, description, creator_id, map_data, is_active, is_featured, visit_count) VALUES (?, ?, ?, ?, 1, 1, 100)').run('Physics Test World', 'Test physics!', adminId, mapData);
    console.log('Test game created');
  }
}

async function startServer() {
  await initializeDatabase();
  await doSeed();
  
  const wss = new WebSocketServer({ server, path: '/ws/game' });
  const chatWss = new WebSocketServer({ server, path: '/ws/chat' });

  setupGameWebSocket(wss);
  setupChatWebSocket(chatWss);

  server.listen(PORT, '0.0.0.0', () => {
    console.log(`Novus Worlds running on port ${PORT}`);
  });
}

startServer();

module.exports = { app, server };