const WebSocket = require('ws');
const db = require('../db');

const rooms = new Map();
const profanity = ['porra', 'caralho', 'merda'];

function safeChat(message) {
  let text = String(message || '').slice(0, 140);
  for (const word of profanity) text = text.replace(new RegExp(word, 'gi'), '****');
  return text;
}

function getRoom(gameId) {
  const key = String(gameId || 'lobby');
  if (!rooms.has(key)) rooms.set(key, { players: new Map() });
  return rooms.get(key);
}

function broadcast(room, data, except) {
  const msg = JSON.stringify(data);
  for (const player of room.players.values()) {
    if (player.ws !== except && player.ws.readyState === WebSocket.OPEN) player.ws.send(msg);
  }
}

function attachGameServer(server) {
  const wss = new WebSocket.Server({ noServer: true });
  server.on('upgrade', (req, socket, head) => {
    const url = new URL(req.url, 'http://localhost');
    if (!url.pathname.startsWith('/ws/game/')) return;
    wss.handleUpgrade(req, socket, head, (ws) => wss.emit('connection', ws, req));
  });

  wss.on('connection', (ws, req) => {
    const gameId = new URL(req.url, 'http://localhost').pathname.split('/').pop();
    const room = getRoom(gameId);
    let self = null;
    ws.on('message', (raw) => {
      let data;
      try { data = JSON.parse(raw); } catch { return; }
      if (data.type === 'join') {
        if (room.players.size >= 20) return ws.send(JSON.stringify({ type: 'error', message: 'Sala cheia' }));
        self = {
          id: String(data.userId || cryptoRandom()),
          username: String(data.username || 'Guest').slice(0, 20),
          avatar: data.avatarData || {},
          ws,
          position: data.position || { x: 0, y: 4, z: 0 },
          rotation: { x: 0, y: 0, z: 0 },
          animation: 'idle',
          chat: ''
        };
        room.players.set(self.id, self);
        db.prepare('UPDATE games SET visit_count = visit_count + 1 WHERE id = ?').run(gameId);
        ws.send(JSON.stringify({ type: 'world_state', players: [...room.players.values()].map(packPlayer) }));
        broadcast(room, { type: 'player_join', player: packPlayer(self) }, ws);
      }
      if (!self) return;
      if (data.type === 'move') {
        self.position = data.position;
        self.rotation = data.rotation;
        self.animation = data.animation || 'idle';
      }
      if (data.type === 'chat') {
        const message = safeChat(data.message);
        self.chat = message;
        broadcast(room, { type: 'chat_broadcast', from: self.username, playerId: self.id, message, timestamp: Date.now() });
        ws.send(JSON.stringify({ type: 'chat_broadcast', from: self.username, playerId: self.id, message, timestamp: Date.now() }));
        setTimeout(() => { if (self) self.chat = ''; }, 5000);
      }
    });
    ws.on('close', () => {
      if (!self) return;
      room.players.delete(self.id);
      broadcast(room, { type: 'player_leave', playerId: self.id });
    });
  });

  setInterval(() => {
    for (const room of rooms.values()) broadcast(room, { type: 'world_state', players: [...room.players.values()].map(packPlayer) });
  }, 50);
}

function packPlayer(p) {
  return { id: p.id, username: p.username, avatar: p.avatar, position: p.position, rotation: p.rotation, animation: p.animation, chat: p.chat };
}

function cryptoRandom() {
  return Math.random().toString(36).slice(2);
}

module.exports = { attachGameServer };
