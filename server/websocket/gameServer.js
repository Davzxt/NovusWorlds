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
  if (!rooms.has(key)) rooms.set(key, { players: new Map(), scripts: loadScripts(key) });
  return rooms.get(key);
}

function loadScripts(gameId) {
  const game = db.prepare('SELECT map_data FROM games WHERE id = ?').get(gameId);
  if (!game) return [];
  try { return JSON.parse(game.map_data).scripts || []; } catch { return []; }
}

function runJoinScripts(room, player) {
  const api = {
    player: {
      teleport: (x, y, z) => { player.position = { x: Number(x), y: Number(y), z: Number(z) }; },
      setHealth: (hp) => { player.health = Math.max(0, Math.min(100, Number(hp))); },
      addScore: (amount) => { player.score = (player.score || 0) + Number(amount || 0); }
    },
    game: { players: [...room.players.values()].map((p) => p.username), workspace: room.workspace || [] }
  };
  for (const script of room.scripts) executeLuauLike(script.source, 'playerJoin', api);
}

function executeLuauLike(source, eventName, api) {
  const text = String(source || '').replace(/\r/g, '');
  const block = text.match(new RegExp(`game\\.on\\(["']${eventName}["'],\\s*function\\(player\\)([\\s\\S]*?)end\\)`, 'm'));
  if (!block) return;
  const vars = new Map();
  for (const raw of block[1].split('\n')) {
    const line = raw.replace(/--.*$/, '').trim();
    if (!line) continue;
    const local = line.match(/^local\s+([A-Za-z_]\w*)\s*=\s*(.+)$/);
    if (local) { vars.set(local[1], evalExpr(local[2], vars)); continue; }
    const call = line.match(/^player:(teleport|setHealth|addScore)\((.*)\)$/);
    if (call && api.player[call[1]]) api.player[call[1]](...splitArgs(call[2]).map((v) => evalExpr(v, vars)));
  }
}

function splitArgs(src) {
  return String(src || '').split(',').map((v) => v.trim()).filter(Boolean);
}

function evalExpr(src, vars) {
  const value = String(src || '').trim();
  if (vars.has(value)) return vars.get(value);
  if (/^[-\d.]+$/.test(value)) return Number(value);
  return value.replace(/^["']|["']$/g, '');
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
    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });
    const gameId = new URL(req.url, 'http://localhost').pathname.split('/').pop();
    const room = getRoom(gameId);
    let self = null;
    ws.on('message', (raw) => {
      let data;
      try { data = JSON.parse(raw); } catch { return; }
      if (data.type === 'join') {
        if (room.players.size >= 20) return ws.send(JSON.stringify({ type: 'error', message: 'Sala cheia' }));
        self = {
          id: String(data.userId || data.guestKey || cryptoRandom()),
          username: String(data.username || 'Guest').slice(0, 20),
          avatar: data.avatarData || {},
          ws,
          position: data.position || { x: 0, y: 4, z: 0 },
          rotation: { x: 0, y: 0, z: 0 },
          animation: 'idle',
          chat: ''
        };
        runJoinScripts(room, self);
        room.players.set(self.id, self);
        const guestKey = data.guestKey ? String(data.guestKey).slice(0, 80) : null;
        const userId = Number(data.userId) || null;
        const inserted = userId
          ? db.prepare('INSERT OR IGNORE INTO game_visits (user_id, game_id) VALUES (?, ?)').run(userId, gameId).changes
          : db.prepare('INSERT OR IGNORE INTO game_visits (guest_key, game_id) VALUES (?, ?)').run(guestKey || cryptoRandom(), gameId).changes;
        if (inserted) db.prepare('UPDATE games SET visit_count = visit_count + 1 WHERE id = ?').run(gameId);
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

  setInterval(() => {
    for (const client of wss.clients) {
      if (!client.isAlive) {
        client.terminate();
        continue;
      }
      client.isAlive = false;
      client.ping();
    }
  }, 30000);
}

function packPlayer(p) {
  return { id: p.id, username: p.username, avatar: p.avatar, position: p.position, rotation: p.rotation, animation: p.animation, chat: p.chat };
}

function cryptoRandom() {
  return Math.random().toString(36).slice(2);
}

function getGameServerStats() {
  return {
    rooms: [...rooms.entries()].map(([gameId, room]) => ({
      gameId,
      players: room.players.size,
      usernames: [...room.players.values()].map(player => player.username)
    })),
    players: [...rooms.values()].reduce((sum, room) => sum + room.players.size, 0)
  };
}

module.exports = { attachGameServer, getGameServerStats };
