const WebSocket = require('ws');
const db = require('../db');

function attachChatServer(server) {
  const clients = new Set();
  const wss = new WebSocket.Server({ noServer: true });
  server.on('upgrade', (req, socket, head) => {
    const url = new URL(req.url, 'http://localhost');
    if (url.pathname !== '/ws/chat') return;
    wss.handleUpgrade(req, socket, head, (ws) => wss.emit('connection', ws));
  });
  wss.on('connection', (ws) => {
    clients.add(ws);
    ws.send(JSON.stringify({ type: 'history', messages: db.prepare('SELECT username, message, created_at FROM chat_messages ORDER BY id DESC LIMIT 50').all().reverse() }));
    ws.on('message', (raw) => {
      let data;
      try { data = JSON.parse(raw); } catch { return; }
      const username = String(data.username || 'Guest').slice(0, 20);
      const message = String(data.message || '').slice(0, 160);
      if (!message) return;
      db.prepare('INSERT INTO chat_messages (username, message) VALUES (?, ?)').run(username, message);
      const payload = JSON.stringify({ type: 'message', username, message, created_at: new Date().toISOString() });
      for (const client of clients) if (client.readyState === WebSocket.OPEN) client.send(payload);
    });
    ws.on('close', () => clients.delete(ws));
  });
}

module.exports = { attachChatServer };
