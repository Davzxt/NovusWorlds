const crypto = require('crypto');
const express = require('express');
const db = require('../db');

const router = express.Router();
const tickets = new Map();

function baseUrl(req) {
  return `${req.protocol}://${req.get('host')}`;
}

function parseJson(value, fallback = {}) {
  try { return JSON.parse(value || ''); } catch { return fallback; }
}

function absolute(req, url) {
  if (!url) return null;
  if (/^https?:\/\//i.test(url) || url.startsWith('data:')) return url;
  return baseUrl(req) + url;
}

function legacyType(item) {
  if (item.type === 'face') return 'FaceDecal';
  if (item.type === 'shirt') return 'ShirtTemplate';
  if (item.type === 'pants') return 'PantsTemplate';
  if (item.type === 'hat') return item.legacy_mesh_url ? 'HatMesh' : 'HatNeedsMeshConversion';
  return 'Unknown';
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
    colors: avatar.colors || { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' },
    items: items.map(item => ({
      id: item.id,
      name: item.name,
      type: item.type,
      legacyType: legacyType(item),
      textureUrl: absolute(req, item.asset_url),
      modelUrl: absolute(req, item.model_url),
      hatTransform: parseJson(item.hat_transform, {}),
      compatible: item.type !== 'hat' || Boolean(item.legacy_mesh_url),
      note: item.type === 'hat' && !item.legacy_mesh_url ? 'GLTF/GLB precisa ser convertido para mesh/acessorio legado antes de funcionar no client 2008/2012.' : null
    }))
  };
}

router.post('/tickets', (req, res) => {
  const user = getUserByRequest(req);
  const gameId = Number(req.body.gameId || req.query.gameId || 1);
  const game = db.prepare('SELECT id, title FROM games WHERE id = ? AND is_active = 1').get(gameId);
  if (!game) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  const ticket = crypto.randomBytes(24).toString('hex');
  tickets.set(ticket, { userId: user.id, username: user.username, gameId, createdAt: Date.now() });
  res.json({
    ticket,
    gameId,
    username: user.username,
    protocolUrl: `novus://join?ticket=${ticket}&gameId=${gameId}&baseUrl=${encodeURIComponent(baseUrl(req))}`,
    joinScriptUrl: `${baseUrl(req)}/api/legacy/join-script?ticket=${ticket}`
  });
});

router.get('/join-script', (req, res) => {
  const ticket = String(req.query.ticket || '');
  const entry = tickets.get(ticket);
  if (!entry || Date.now() - entry.createdAt > 5 * 60 * 1000) return res.status(403).type('text/plain').send('-- invalid ticket');
  const host = baseUrl(req);
  const script = `
-- Novus Worlds legacy join script
local baseUrl = "${host}"
local ticket = "${ticket}"
local gameId = ${entry.gameId}
local username = "${entry.username.replace(/"/g, '')}"

pcall(function()
  game:GetService("Players").LocalPlayer.Name = username
end)

-- Launcher/client adapter should fetch:
-- baseUrl .. "/api/legacy/avatar?ticket=" .. ticket
-- baseUrl .. "/api/legacy/place/" .. gameId
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
    lines.push(`  <Item id="${item.id}" type="${item.type}" legacyType="${item.legacyType}" compatible="${item.compatible}" texture="${escapeXml(item.textureUrl || '')}" model="${escapeXml(item.modelUrl || '')}" />`);
  }
  lines.push('</Avatar>');
  res.type('application/xml').send(lines.join('\n'));
});

router.get('/place/:id', (req, res) => {
  const game = db.prepare('SELECT * FROM games WHERE id = ? AND is_active = 1').get(req.params.id);
  if (!game) return res.status(404).json({ error: 'Jogo nao encontrado.' });
  const map = parseJson(game.map_data, {});
  res.json({
    id: game.id,
    title: game.title,
    format: 'NovusMapJsonForLegacyAdapter',
    note: 'O launcher precisa converter este JSON para .rbxl/.rbxlx ou inserir as Parts via script no client antigo.',
    map
  });
});

router.get('/assets/:id', (req, res) => {
  const item = db.prepare('SELECT * FROM catalog_items WHERE id = ? AND is_active = 1').get(req.params.id);
  if (!item) return res.status(404).json({ error: 'Asset nao encontrado.' });
  res.json({
    id: item.id,
    name: item.name,
    type: item.type,
    legacyType: legacyType(item),
    textureUrl: absolute(req, item.asset_url),
    modelUrl: absolute(req, item.model_url),
    hatTransform: parseJson(item.hat_transform, {}),
    compatible: item.type !== 'hat' || Boolean(item.legacy_mesh_url)
  });
});

function escapeXml(value) {
  return String(value ?? '').replace(/[<>&"']/g, c => ({ '<': '&lt;', '>': '&gt;', '&': '&amp;', '"': '&quot;', "'": '&apos;' }[c]));
}

module.exports = router;
