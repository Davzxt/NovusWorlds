const chatServers = new Map();
const MAX_MESSAGES = 50;
const BAD_WORDS = ['fuck', 'shit', 'ass', 'bitch', 'damn', 'hell', 'cock', 'dick', 'porn', 'sex'];

function setupChatWebSocket(wss, db) {
  wss.on('connection', (ws, req) => {
    let userId = null;
    let username = null;
    let isAdmin = false;
    
    ws.isAlive = true;
    ws.on('pong', () => { ws.isAlive = true; });

    ws.on('message', (data) => {
      try {
        const msg = JSON.parse(data);
        handleMessage(msg, ws, { userId, username, isAdmin }, db, wss);
      } catch (e) {
        console.error('Invalid message:', e);
      }
    });

    ws.on('close', () => {
      if (username) {
        broadcast(wss, { type: 'user_left', username, timestamp: Date.now() });
      }
    });

    ws.send(JSON.stringify({ type: 'welcome', message: 'Connected to Novus Worlds Chat' }));
  });

  const interval = setInterval(() => {
    wss.clients.forEach(ws => {
      if (!ws.isAlive) {
        return ws.terminate();
      }
      ws.isAlive = false;
      ws.ping();
    });
  }, 30000);

  wss.on('close', () => clearInterval(interval));
}

function handleMessage(msg, ws, user, db, wss) {
  switch (msg.type) {
    case 'auth':
      if (!msg.sessionToken) {
        ws.send(JSON.stringify({ type: 'error', message: 'Authentication required' }));
        return;
      }

      const session = db.prepare(`
        SELECT u.id, u.username, u.is_admin, u.is_banned
        FROM users u
        JOIN sessions s ON s.sess LIKE '%' || u.username || '%'
        WHERE s.expired > datetime('now')
        LIMIT 1
      `).get();

      if (!session) {
        ws.send(JSON.stringify({ type: 'error', message: 'Authentication required' }));
        return;
      }

      if (session.is_banned) {
        ws.send(JSON.stringify({ type: 'error', message: 'You are banned from chat' }));
        return;
      }

      user.userId = session.id;
      user.username = session.username;
      user.isAdmin = session.is_admin === 1;

      ws.send(JSON.stringify({ 
        type: 'authenticated', 
        username: user.username,
        isAdmin: user.isAdmin
      }));
      break;

    case 'message':
      if (!user.username) {
        ws.send(JSON.stringify({ type: 'error', message: 'Authentication required' }));
        return;
      }

      let message = msg.message.trim();
      if (!message || message.length > 200) {
        return;
      }

      BAD_WORDS.forEach(word => {
        const regex = new RegExp(word, 'gi');
        message = message.replace(regex, '*'.repeat(word.length));
      });

      broadcast(wss, { 
        type: 'message', 
        username: user.username,
        message: message,
        timestamp: Date.now()
      });
      break;

    case 'history':
      const messages = chatServers.get('global') || [];
      ws.send(JSON.stringify({ type: 'history', messages }));
      break;
  }
}

function broadcast(wss, msg) {
  const data = JSON.stringify(msg);
  wss.clients.forEach(ws => {
    if (ws.readyState === 1) {
      ws.send(data);
    }
  });
}

  const globalMessages = chatServers.get('global') || [];
  globalMessages.push(msg);
  if (globalMessages.length > MAX_MESSAGES) {
    globalMessages.shift();
  }
  chatServers.set('global', globalMessages);
}

module.exports = { setupChatWebSocket };