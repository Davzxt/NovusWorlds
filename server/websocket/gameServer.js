const gameServers = new Map();
const MAX_PLAYERS = 20;

function setupGameWebSocket(wss, db) {
  wss.on('connection', (ws, req) => {
    const urlParts = req.url.split('/');
    const gameId = urlParts[urlParts.length - 1];
    
    let serverInstance = gameServers.get(gameId);
    if (!serverInstance) {
      serverInstance = {
        gameId,
        clients: new Map(),
        spawnPoints: [{ x: 0, y: 5, z: 0 }],
        nextPlayerId: 1
      };
      gameServers.set(gameId, serverInstance);
    }

    if (serverInstance.clients.size >= MAX_PLAYERS) {
      ws.send(JSON.stringify({ type: 'error', message: 'Server full' }));
      ws.close();
      return;
    }

    const playerId = serverInstance.nextPlayerId++;
    const player = {
      id: playerId,
      ws,
      userId: null,
      username: 'Player' + playerId,
      position: { x: 0, y: 5, z: 0 },
      rotation: { x: 0, y: 0, z: 0 },
      animation: 'idle',
      avatarData: {}
    };
    serverInstance.clients.set(playerId, player);

    ws.isAlive = true;
    ws.playerId = playerId;
    ws.gameId = gameId;

    ws.on('pong', () => { ws.isAlive = true; });

    ws.on('message', (data) => {
      try {
        const msg = JSON.parse(data);
        handleMessage(msg, player, serverInstance, db);
      } catch (e) {
        console.error('Invalid message:', e);
      }
    });

    ws.on('close', () => {
      broadcast(serverInstance, { type: 'player_leave', playerId: player.id });
      serverInstance.clients.delete(playerId);
      
      const players = [];
      serverInstance.clients.forEach(p => {
        players.push({
          id: p.id,
          userId: p.userId,
          username: p.username,
          position: p.position,
          rotation: p.rotation,
          animation: p.animation,
          avatarData: p.avatarData
        });
      });
      broadcast(serverInstance, { type: 'world_state', players });

      if (serverInstance.clients.size === 0) {
        gameServers.delete(gameId);
      }
    });

    const players = [];
    serverInstance.clients.forEach(p => {
      if (p.id !== playerId) {
        players.push({
          id: p.id,
          userId: p.userId,
          username: p.username,
          position: p.position,
          rotation: p.rotation,
          animation: p.animation,
          avatarData: p.avatarData
        });
      }
    });
    ws.send(JSON.stringify({ type: 'world_state', players }));
    
    broadcast(serverInstance, { 
      type: 'player_join', 
      player: {
        id: player.id,
        userId: player.userId,
        username: player.username,
        position: player.position,
        rotation: player.rotation,
        animation: player.animation,
        avatarData: player.avatarData
      }
    });
  });

  const interval = setInterval(() => {
    wss.clients.forEach(ws => {
      if (!ws.isAlive) {
        const serverInstance = gameServers.get(ws.gameId);
        if (serverInstance) {
          serverInstance.clients.delete(ws.playerId);
        }
        return ws.terminate();
      }
      ws.isAlive = false;
      ws.ping();
    });
  }, 30000);

  wss.on('close', () => clearInterval(interval));
}

function handleMessage(msg, player, serverInstance, db) {
  switch (msg.type) {
    case 'join':
      player.userId = msg.userId;
      player.username = msg.username || player.username;
      player.avatarData = msg.avatarData || {};
      
      const spawnIndex = (player.id - 1) % serverInstance.spawnPoints.length;
      const spawn = serverInstance.spawnPoints[spawnIndex];
      player.position = { ...spawn };
      
      broadcast(serverInstance, { 
        type: 'player_join', 
        player: {
          id: player.id,
          userId: player.userId,
          username: player.username,
          position: player.position,
          rotation: player.rotation,
          animation: player.animation,
          avatarData: player.avatarData
        }
      });
      break;

    case 'move':
      player.position = msg.position || player.position;
      player.rotation = msg.rotation || player.rotation;
      player.animation = msg.animation || 'idle';
      
      broadcast(serverInstance, { 
        type: 'player_update', 
        playerId: player.id,
        position: player.position,
        rotation: player.rotation,
        animation: player.animation
      }, player.id);
      break;

    case 'chat':
      broadcast(serverInstance, { 
        type: 'chat_broadcast', 
        from: player.username,
        message: msg.message,
        timestamp: Date.now()
      });
      break;

    case 'leave':
      player.ws.close();
      break;

    case 'spawn':
      if (serverInstance.spawnPoints.length > 0) {
        const spawnIndex = Math.floor(Math.random() * serverInstance.spawnPoints.length);
        player.position = { ...serverInstance.spawnPoints[spawnIndex] };
      }
      break;
  }
}

function broadcast(serverInstance, msg, excludePlayerId = null) {
  const data = JSON.stringify(msg);
  serverInstance.clients.forEach((player, id) => {
    if (id !== excludePlayerId && player.ws.readyState === 1) {
      player.ws.send(data);
    }
  });
}

function getGameServer(gameId) {
  return gameServers.get(gameId);
}

module.exports = { setupGameWebSocket, getGameServer };