const crypto = require('crypto');
const express = require('express');
const db = require('../db');

const router = express.Router();
const tickets = new Map();
const TICKET_TTL_MS = 30 * 60 * 1000;

function baseUrl(req) {
  return `${req.protocol}://${req.get('host')}`;
}

function gameServerHost(req) {
  return process.env.NOVUS_GODOT_HOST || req.hostname || '127.0.0.1';
}

function gameServerPort() {
  return Number(process.env.NOVUS_GODOT_PORT || 53640);
}

function parseJson(value, fallback = {}) {
  try { return JSON.parse(value || ''); } catch { return fallback; }
}

function absolute(req, url) {
  if (!url) return null;
  const value = String(url).trim();
  if (!value) return null;
  if (/^https?:\/\//i.test(value) || value.startsWith('data:')) return value;
  if (value.startsWith('/')) return baseUrl(req) + value;
  if (/^(face|hat|shirt|pants|head)-[A-Za-z0-9_-]+$/i.test(value)) return null;
  return `${baseUrl(req)}/${value.replace(/^\/+/, '')}`;
}

function hatModelUrl(item) {
  if (item.type !== 'hat') return item.model_url;
  return item.model_url || (/\.(gltf|glb|obj|mesh)$/i.test(String(item.asset_url || '')) ? item.asset_url : null);
}

function getUserByRequest(req) {
  if (req.session.user) return db.prepare('SELECT * FROM users WHERE id = ?').get(req.session.user.id);
  const username = String(req.query.username || 'Guest').replace(/[^A-Za-z0-9_]/g, '').slice(0, 20) || 'Guest';
  return { id: 0, username, avatar_data: '{}', novux: 0 };
}

function getResolvedAvatar(user, req) {
  const avatar = parseJson(user.avatar_data, {});
  const items = [];
  const byId = id => db.prepare('SELECT * FROM catalog_items WHERE id = ? AND is_active = 1').get(id);

  for (const id of (avatar.hats || []).slice(0, 3)) {
    const item = byId(Number(id));
    if (item) items.push(item);
  }
  for (const key of ['face', 'shirt', 'pants']) {
    const raw = avatar[key];
    const item = Number(raw) ? byId(Number(raw)) : db.prepare('SELECT * FROM catalog_items WHERE name = ? AND type = ?').get(raw, key);
    if (item) items.push(item);
  }

  return {
    userId: user.id,
    username: user.username,
    rig: 'R6',
    face: avatar.face || 'happy',
    colors: avatar.colors || { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' },
    items: items.map(item => ({
      id: item.id,
      name: item.name,
      type: item.type,
      textureUrl: absolute(req, item.type === 'hat' && hatModelUrl(item) === item.asset_url ? item.thumbnail_url : item.asset_url),
      modelUrl: absolute(req, hatModelUrl(item)),
      assetUrl: absolute(req, item.asset_url),
      thumbnailUrl: absolute(req, item.thumbnail_url),
      hatTransform: parseJson(item.hat_transform, {})
    }))
  };
}

router.post('/tickets', (req, res) => {
  const user = getUserByRequest(req);
  const gameId = Number(req.body.gameId || req.query.gameId || 1);
  const game = db.prepare('SELECT id, title, creator_id, is_active FROM games WHERE id = ?').get(gameId);
  if (!game) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  const canPrivateTest = req.session.user && (req.session.user.is_admin || Number(game.creator_id) === Number(req.session.user.id));
  if (!game.is_active && !canPrivateTest) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  const ticket = crypto.randomBytes(24).toString('hex');
  tickets.set(ticket, { userId: user.id, username: user.username, gameId, privateTest: !game.is_active && canPrivateTest, createdAt: Date.now() });
  res.json({
    ticket,
    gameId,
    username: user.username,
    serverHost: gameServerHost(req),
    serverPort: gameServerPort(),
    joinDataUrl: `${baseUrl(req)}/api/legacy/tickets/${ticket}`,
    protocolUrl: `novus://join?ticket=${ticket}&gameId=${gameId}&baseUrl=${encodeURIComponent(baseUrl(req))}&server=${encodeURIComponent(gameServerHost(req))}&port=${gameServerPort()}`,
    placeUrl: `${baseUrl(req)}/api/legacy/place/${gameId}?ticket=${ticket}`
  });
});

router.get('/tickets/:ticket', (req, res) => {
  const ticket = String(req.params.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry || Date.now() - entry.createdAt > TICKET_TTL_MS) return res.status(403).json({ error: 'Ticket invalido.' });
  const user = entry.userId ? db.prepare('SELECT * FROM users WHERE id = ?').get(entry.userId) : getUserByRequest(req);
  res.json({
    ticket,
    gameId: entry.gameId,
    username: entry.username,
    baseUrl: baseUrl(req),
    serverHost: gameServerHost(req),
    serverPort: gameServerPort(),
    avatar: getResolvedAvatar(user, req),
    placeUrl: `${baseUrl(req)}/api/legacy/place/${entry.gameId}?ticket=${ticket}`
  });
});

router.post('/studio-tickets', (req, res) => {
  const user = getUserByRequest(req);
  if (!req.session.user) return res.status(401).json({ error: 'Login necessario.' });
  const gameId = req.body.gameId ? Number(req.body.gameId) : null;
  if (gameId) {
    const game = db.prepare('SELECT id, creator_id FROM games WHERE id = ?').get(gameId);
    if (!game) return res.status(404).json({ error: 'Projeto nao encontrado.' });
    if (game.creator_id !== req.session.user.id && !req.session.user.is_admin) return res.status(403).json({ error: 'Sem permissao.' });
  }
  const ticket = crypto.randomBytes(24).toString('hex');
  tickets.set(ticket, { userId: user.id, username: user.username, gameId: gameId || 0, mode: 'studio', isAdmin: !!req.session.user.is_admin, createdAt: Date.now() });
  res.json({
    ticket,
    gameId,
    username: user.username,
    protocolUrl: `novus-studio://edit?ticket=${ticket}&gameId=${gameId || ''}&baseUrl=${encodeURIComponent(baseUrl(req))}`,
    projectUrl: `${baseUrl(req)}/api/legacy/studio-project?ticket=${ticket}`
  });
});

router.get('/join-script', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry || Date.now() - entry.createdAt > TICKET_TTL_MS) return res.status(403).type('text/plain').send('-- invalid ticket');
  const host = baseUrl(req);
  const script = `
-- Novus Worlds join ticket
local baseUrl = "${host}"
local ticket = "${ticket}"
local gameId = ${entry.gameId}
local username = "${entry.username.replace(/"/g, '')}"
`;
  res.type('text/plain').send(script.trim());
});

router.get('/avatar', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry && !req.session.user) return res.status(403).json({ error: 'Ticket invalido.' });
  const user = entry?.userId ? db.prepare('SELECT * FROM users WHERE id = ?').get(entry.userId) : getUserByRequest(req);
  res.json({ avatar: getResolvedAvatar(user, req) });
});

router.get('/avatar.xml', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry && !req.session.user) return res.status(403).type('application/xml').send('<error>Ticket invalido</error>');
  const user = entry?.userId ? db.prepare('SELECT * FROM users WHERE id = ?').get(entry.userId) : getUserByRequest(req);
  const avatar = getResolvedAvatar(user, req);
  const lines = [
    '<?xml version="1.0" encoding="utf-8"?>',
    '<Avatar rig="R6">',
    `  <Username>${escapeXml(avatar.username)}</Username>`,
    `  <Colors head="${avatar.colors.head}" torso="${avatar.colors.torso}" arms="${avatar.colors.arms}" legs="${avatar.colors.legs}" />`
  ];
  for (const item of avatar.items) {
    lines.push(`  <Item id="${item.id}" type="${item.type}" texture="${escapeXml(item.textureUrl || '')}" model="${escapeXml(item.modelUrl || '')}" />`);
  }
  lines.push('</Avatar>');
  res.type('application/xml').send(lines.join('\n'));
});

router.get('/place/:id', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  const allowTicket = entry && Number(entry.gameId) === Number(req.params.id) && Date.now() - entry.createdAt <= TICKET_TTL_MS;
  const game = allowTicket
    ? db.prepare('SELECT * FROM games WHERE id = ?').get(req.params.id)
    : db.prepare('SELECT * FROM games WHERE id = ? AND is_active = 1').get(req.params.id);
  if (!game) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  const map = parseJson(game.map_data, {});
  res.json({
    id: game.id,
    title: game.title,
    format: 'NovusMapJson',
    map
  });
});

router.get('/studio-project', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry || entry.mode !== 'studio' || Date.now() - entry.createdAt > TICKET_TTL_MS) return res.status(403).json({ error: 'Ticket invalido.' });
  const requestedGameId = Number(req.query.gameId || entry.gameId || 0);
  let game = null;
  if (requestedGameId) {
    game = db.prepare('SELECT * FROM games WHERE id = ?').get(requestedGameId);
    if (!game) return res.status(404).json({ error: 'Projeto nao encontrado.' });
    if (game.creator_id !== entry.userId && !entry.isAdmin) return res.status(403).json({ error: 'Sem permissao.' });
    entry.gameId = requestedGameId;
  }
  res.json({
    mode: 'studio',
    username: entry.username,
    gameId: game?.id || entry.gameId || null,
    title: game?.title || 'Novo Mundo',
    description: game?.description || '',
    thumbnail_url: game?.thumbnail_url || '',
    maxPlayers: game?.max_players || 20,
    map: game ? parseJson(game.map_data, {}) : {
      name: 'Novo Mundo',
      version: 1,
      objects: [{ id: 'baseplate', type: 'Part', name: 'Baseplate', position: { x: 0, y: -0.5, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, size: { x: 80, y: 1, z: 80 }, color: '#6B8E23', material: 'Grass', anchored: true, canCollide: true, transparency: 0, children: [] }],
      spawnPoints: [{ x: 0, y: 3, z: 0 }],
      ambient: '#404040',
      skyColor: '#87CEEB'
    }
  });
});

router.get('/studio-games', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry || entry.mode !== 'studio' || Date.now() - entry.createdAt > TICKET_TTL_MS) return res.status(403).json({ error: 'Ticket invalido.' });
  const games = db.prepare(`
    SELECT id, title, description, thumbnail_url, is_active, updated_at
    FROM games
    WHERE creator_id = ?
    ORDER BY updated_at DESC, id DESC
    LIMIT 80
  `).all(entry.userId);
  res.json({ games });
});

router.post('/studio-project/save', (req, res) => {
  try {
    const ticket = String(req.body.ticket || req.query.ticket || '');
    const entry = tickets.get(ticket);
    if (!entry || entry.mode !== 'studio' || Date.now() - entry.createdAt > TICKET_TTL_MS) return res.status(403).json({ error: 'Ticket invalido.' });
    if (!entry.userId) return res.status(401).json({ error: 'Login necessario.' });

    const title = String(req.body.title || 'Novo Mundo').trim().slice(0, 80) || 'Novo Mundo';
    const description = String(req.body.description || '').trim().slice(0, 500);
    const publish = req.body.publish === true || req.body.publish === 'true';
    const maxPlayers = Math.max(1, Math.min(20, Number(req.body.maxPlayers || req.body.max_players || 20)));
    const thumbnailUrl = String(req.body.thumbnail_url || req.body.thumbnailUrl || '').slice(0, 2_000_000);
    const incomingId = Number(req.body.gameId || entry.gameId || 0);
    const map = req.body.map_data && typeof req.body.map_data === 'object' ? req.body.map_data : {
      name: title,
      version: 1,
      objects: [],
      scripts: [],
      spawnPoints: [{ x: 0, y: 4, z: 0 }],
      ambient: '#404040',
      skyColor: '#87CEEB'
    };
    map.name = title;
    const mapJson = JSON.stringify(map, null, 2);

    let id = incomingId;
    if (id) {
      const game = db.prepare('SELECT * FROM games WHERE id = ?').get(id);
      if (!game) return res.status(404).json({ error: 'Projeto nao encontrado.' });
      if (game.creator_id !== entry.userId) return res.status(403).json({ error: 'Sem permissao.' });
      db.prepare(`
        UPDATE games
        SET title = ?, description = ?, map_data = ?, thumbnail_url = COALESCE(NULLIF(?, ''), thumbnail_url), max_players = ?, is_active = CASE WHEN ? THEN 1 ELSE is_active END, updated_at = CURRENT_TIMESTAMP
        WHERE id = ?
      `).run(title, description, mapJson, thumbnailUrl, maxPlayers, publish ? 1 : 0, id);
    } else {
      const info = db.prepare(`
        INSERT INTO games (title, description, creator_id, map_data, thumbnail_url, is_active, max_players)
        VALUES (?, ?, ?, ?, ?, ?, ?)
      `).run(title, description, entry.userId, mapJson, thumbnailUrl || '/assets/textures/game-default.svg', publish ? 1 : 0, maxPlayers);
      id = info.lastInsertRowid;
      entry.gameId = id;
    }
    db.prepare('INSERT INTO activity_log (type, message) VALUES (?, ?)').run('studio', publish ? `Jogo publicado pelo Studio: ${title}` : `Projeto salvo pelo Studio: ${title}`);
    return res.json({ ok: true, id, published: publish });
  } catch (err) {
    console.error('[studio-project/save]', err);
    return res.status(500).json({ error: err?.message || 'Erro ao salvar projeto.' });
  }
});

router.get('/assets/:id', (req, res) => {
  const item = db.prepare('SELECT * FROM catalog_items WHERE id = ? AND is_active = 1').get(req.params.id);
  if (!item) return res.status(404).json({ error: 'Asset nao encontrado.' });
  res.json({
    id: item.id,
    name: item.name,
    type: item.type,
    textureUrl: absolute(req, item.type === 'hat' && hatModelUrl(item) === item.asset_url ? item.thumbnail_url : item.asset_url),
    modelUrl: absolute(req, hatModelUrl(item)),
    assetUrl: absolute(req, item.asset_url),
    thumbnailUrl: absolute(req, item.thumbnail_url),
    hatTransform: parseJson(item.hat_transform, {})
  });
});

function escapeXml(value) {
  return String(value ?? '').replace(/[<>&"']/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[c]));
}

module.exports = router;
