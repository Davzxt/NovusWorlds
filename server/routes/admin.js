const express = require('express');
const bcrypt = require('bcrypt');
const multer = require('multer');
const path = require('path');
const db = require('../db');
const { requireAdmin } = require('../middleware/admin');
const router = express.Router();

const storage = multer.diskStorage({
  destination: (req, file, cb) => cb(null, path.join(__dirname, '..', '..', 'public', 'uploads', 'catalog')),
  filename: (req, file, cb) => cb(null, `${Date.now()}-${file.originalname.replace(/[^A-Za-z0-9_.-]/g, '')}`)
});
const upload = multer({ storage, limits: { fileSize: 8 * 1024 * 1024 } });

router.use(requireAdmin);

router.get('/stats', (req, res) => {
  const one = (sql) => Object.values(db.prepare(sql).get())[0];
  res.json({
    stats: {
      users: one('SELECT COUNT(*) c FROM users WHERE deleted_at IS NULL'),
      online: one("SELECT COUNT(*) c FROM users WHERE last_login > datetime('now','-10 minutes')"),
      games: one('SELECT COUNT(*) c FROM games'),
      items: one('SELECT COUNT(*) c FROM catalog_items'),
      novux: one('SELECT COALESCE(SUM(novux),0) c FROM users'),
      reports: one("SELECT COUNT(*) c FROM reports WHERE status = 'pending'"),
      today: one("SELECT COUNT(*) c FROM users WHERE date(created_at) = date('now')")
    },
    activity: db.prepare('SELECT * FROM activity_log ORDER BY created_at DESC LIMIT 10').all(),
    registrations: db.prepare("SELECT date(created_at) day, COUNT(*) count FROM users WHERE created_at > datetime('now','-7 days') GROUP BY date(created_at)").all(),
    topGames: db.prepare('SELECT title, visit_count FROM games ORDER BY visit_count DESC LIMIT 5').all()
  });
});

router.get('/users', (req, res) => {
  const q = `%${String(req.query.q || '').trim()}%`;
  res.json({ users: db.prepare('SELECT id, username, novux, is_admin, is_banned, created_at, last_login FROM users WHERE username LIKE ? AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 100').all(q) });
});

router.post('/users/:id/action', async (req, res) => {
  const id = Number(req.params.id);
  const action = req.body.action;
  if (action === 'ban') db.prepare('UPDATE users SET is_banned = 1, ban_reason = ?, ban_expires = datetime(CURRENT_TIMESTAMP, ?) WHERE id = ?').run(req.body.reason || 'Banido pela equipe.', req.body.duration || '+7 days', id);
  if (action === 'unban') db.prepare('UPDATE users SET is_banned = 0, ban_reason = NULL, ban_expires = NULL WHERE id = ?').run(id);
  if (action === 'promote') db.prepare('UPDATE users SET is_admin = 1 WHERE id = ?').run(id);
  if (action === 'demote') db.prepare('UPDATE users SET is_admin = 0 WHERE id = ?').run(id);
  if (action === 'novux') db.prepare('UPDATE users SET novux = MAX(0, novux + ?) WHERE id = ?').run(Number(req.body.amount || 0), id);
  if (action === 'notice') db.prepare('UPDATE users SET login_notice = ? WHERE id = ?').run(String(req.body.message || '').slice(0, 400), id);
  if (action === 'password') db.prepare('UPDATE users SET password_hash = ? WHERE id = ?').run(await bcrypt.hash(String(req.body.password || 'change-me-2008'), 12), id);
  if (action === 'delete') db.prepare("UPDATE users SET deleted_at = CURRENT_TIMESTAMP, username = 'deleted_' || id WHERE id = ?").run(id);
  db.prepare('INSERT INTO activity_log (type, message) VALUES (?, ?)').run('admin', `Acao admin em usuario ${id}: ${action}`);
  res.json({ ok: true });
});

router.get('/games', (req, res) => {
  res.json({ games: db.prepare('SELECT games.*, users.username creator FROM games JOIN users ON users.id = games.creator_id ORDER BY created_at DESC LIMIT 100').all() });
});

router.post('/games/:id/action', (req, res) => {
  const action = req.body.action;
  if (action === 'feature') db.prepare('UPDATE games SET is_featured = 1 WHERE id = ?').run(req.params.id);
  if (action === 'unfeature') db.prepare('UPDATE games SET is_featured = 0 WHERE id = ?').run(req.params.id);
  if (action === 'disable') db.prepare('UPDATE games SET is_active = 0 WHERE id = ?').run(req.params.id);
  if (action === 'enable') db.prepare('UPDATE games SET is_active = 1 WHERE id = ?').run(req.params.id);
  if (action === 'delete') db.prepare('DELETE FROM games WHERE id = ?').run(req.params.id);
  res.json({ ok: true });
});

router.get('/catalog', (req, res) => {
  res.json({ items: db.prepare('SELECT catalog_items.*, users.username creator FROM catalog_items JOIN users ON users.id = catalog_items.creator_id ORDER BY created_at DESC LIMIT 100').all() });
});

router.post('/catalog/add', upload.fields([{ name: 'asset', maxCount: 1 }, { name: 'model', maxCount: 1 }]), (req, res) => {
  const f = req.files || {};
  const type = ['hat', 'face', 'shirt', 'pants'].includes(req.body.type) ? req.body.type : 'hat';
  const asset = f.asset?.[0] ? `/uploads/catalog/${f.asset[0].filename}` : null;
  const model = f.model?.[0] ? `/uploads/catalog/${f.model[0].filename}` : null;
  const name = String(req.body.name || '').trim().slice(0, 80);
  if (!name) return res.status(400).json({ error: 'Nome obrigatorio.' });
  const transform = req.body.hat_transform || '{"position":{"x":0,"y":3.38,"z":0},"rotation":{"x":0,"y":0,"z":0},"scale":{"x":1,"y":1,"z":1}}';
  const facePixels = type === 'face' && String(req.body.face_pixels || '').startsWith('data:image/png;base64,') ? String(req.body.face_pixels) : null;
  const assetUrl = facePixels || asset;
  const info = db.prepare('INSERT INTO catalog_items (name, description, type, price, creator_id, asset_url, model_url, thumbnail_url, hat_transform, is_limited, limited_quantity) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)')
    .run(name, String(req.body.description || '').slice(0, 500), type, Number(req.body.price || 0), req.session.user.id, assetUrl, model, assetUrl || '/assets/textures/item-default.svg', transform, req.body.is_limited ? 1 : 0, req.body.limited_quantity || null);
  db.prepare('INSERT INTO activity_log (type, message) VALUES (?, ?)').run('catalog', `Novo item: ${name}`);
  res.json({ ok: true, id: info.lastInsertRowid });
});

router.post('/catalog/:id/action', (req, res) => {
  if (req.body.action === 'toggle') db.prepare('UPDATE catalog_items SET is_active = CASE is_active WHEN 1 THEN 0 ELSE 1 END WHERE id = ?').run(req.params.id);
  if (req.body.action === 'delete') db.prepare('DELETE FROM catalog_items WHERE id = ?').run(req.params.id);
  res.json({ ok: true });
});

router.get('/reports', (req, res) => res.json({ reports: db.prepare('SELECT * FROM reports ORDER BY created_at DESC LIMIT 100').all() }));
router.post('/reports/:id/action', (req, res) => {
  db.prepare('UPDATE reports SET status = ? WHERE id = ?').run(req.body.status || 'archived', req.params.id);
  res.json({ ok: true });
});

router.get('/settings', (req, res) => res.json({ settings: db.prepare('SELECT * FROM platform_settings').all() }));
router.post('/settings', (req, res) => {
  for (const [key, value] of Object.entries(req.body || {})) db.prepare('INSERT INTO platform_settings (key, value) VALUES (?, ?) ON CONFLICT(key) DO UPDATE SET value = excluded.value').run(key, String(value));
  res.json({ ok: true });
});

router.get('/animations', (req, res) => {
  const rows = db.prepare('SELECT key, name, data, updated_at FROM animation_presets ORDER BY key').all();
  res.json({ animations: rows.map(row => ({ ...row, data: JSON.parse(row.data || '{}') })) });
});

router.post('/animations/:key', (req, res) => {
  const key = String(req.params.key || '').replace(/[^a-z_]/g, '');
  if (!['idle', 'walk', 'jump', 'fall', 'climb'].includes(key)) return res.status(400).json({ error: 'Animacao invalida.' });
  const data = {
    speed: Number(req.body.speed || 1),
    arm: Number(req.body.arm || 0),
    leg: Number(req.body.leg || 0),
    torso: Number(req.body.torso || 0)
  };
  db.prepare(`
    INSERT INTO animation_presets (key, name, data, updated_at)
    VALUES (?, ?, ?, CURRENT_TIMESTAMP)
    ON CONFLICT(key) DO UPDATE SET data = excluded.data, updated_at = CURRENT_TIMESTAMP
  `).run(key, key, JSON.stringify(data));
  res.json({ ok: true });
});

module.exports = router;
