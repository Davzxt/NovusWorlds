const express = require('express');
const { db } = require('../db');
const { requireAuth } = require('../middleware/auth');
const { requireAdmin } = require('../middleware/admin');
const path = require('path');
const multer = require('multer');

const router = express.Router();
router.use(requireAuth);
router.use(requireAdmin);

const storage = multer.diskStorage({
  destination: (req, file, cb) => {
    cb(null, path.join(__dirname, '../../public/uploads/catalog'));
  },
  filename: (req, file, cb) => {
    const uniqueSuffix = Date.now() + '-' + Math.round(Math.random() * 1E9);
    cb(null, uniqueSuffix + path.extname(file.originalname));
  }
});

const upload = multer({ 
  storage,
  limits: { fileSize: 5 * 1024 * 1024 },
  fileFilter: (req, file, cb) => {
    if (file.mimetype.startsWith('image/') || 
        file.mimetype === 'model/gltf-binary' ||
        file.mimetype === 'application/octet-stream') {
      cb(null, true);
    } else {
      cb(new Error('Invalid file type'));
    }
  }
});

router.get('/stats', (req, res) => {
  const totalUsers = db.prepare('SELECT COUNT(*) as count FROM users').get().count;
  const activeUsers = db.prepare(`
    SELECT COUNT(*) as count FROM users 
    WHERE last_login > datetime('now', '-5 minutes')
  `).get().count;
  const totalGames = db.prepare('SELECT COUNT(*) as count FROM games WHERE is_active = 1').get().count;
  const totalItems = db.prepare('SELECT COUNT(*) as count FROM catalog_items WHERE is_active = 1').get().count;
  const totalNovux = db.prepare('SELECT SUM(novux) as total FROM users').get().total || 0;
  const pendingReports = db.prepare("SELECT COUNT(*) as count FROM reports WHERE status = 'pending'").get().count;
  const todayRegistrations = db.prepare(`
    SELECT COUNT(*) as count FROM users 
    WHERE date(created_at) = date('now')
  `).get().count;

  const last7Days = [];
  for (let i = 6; i >= 0; i--) {
    const date = new Date();
    date.setDate(date.getDate() - i);
    const dateStr = date.toISOString().split('T')[0];
    const count = db.prepare(`
      SELECT COUNT(*) as count FROM users WHERE date(created_at) = ?
    `).get(dateStr).count;
    last7Days.push({ date: dateStr, count });
  }

  const topGames = db.prepare(`
    SELECT g.title, g.visit_count, u.username as creator
    FROM games g
    JOIN users u ON g.creator_id = u.id
    WHERE g.is_active = 1
    ORDER BY g.visit_count DESC
    LIMIT 5
  `).all();

  res.json({
    totalUsers,
    activeUsers,
    totalGames,
    totalItems,
    totalNovux,
    pendingReports,
    todayRegistrations,
    last7Days,
    topGames
  });
});

router.get('/users', (req, res) => {
  const { search = '', page = 1 } = req.query;
  const limit = 20;
  const offset = (page - 1) * limit;

  let where = '1=1';
  const params = [];

  if (search) {
    where += ' AND u.username LIKE ?';
    params.push(`%${search}%`);
  }

  const users = db.prepare(`
    SELECT u.id, u.username, u.novux, u.is_admin, u.is_banned, u.created_at, u.last_login,
           (SELECT COUNT(*) FROM games WHERE creator_id = u.id AND is_active = 1) as games_count
    FROM users u
    WHERE ${where}
    ORDER BY u.created_at DESC
    LIMIT ? OFFSET ?
  `).all(...params, limit, offset);

  const total = db.prepare(`SELECT COUNT(*) as count FROM users u WHERE ${where}`).get(...params).count;

  res.json({ users, total, page, totalPages: Math.ceil(total / limit) });
});

router.post('/users/:id/ban', (req, res) => {
  const { reason, duration } = req.body;
  const userId = parseInt(req.params.id);

  const user = db.prepare('SELECT * FROM users WHERE id = ?').get(userId);
  if (!user) {
    return res.status(404).json({ error: 'User not found' });
  }

  let banExpires = null;
  if (duration === '1h') banExpires = new Date(Date.now() + 60 * 60 * 1000).toISOString();
  else if (duration === '24h') banExpires = new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString();
  else if (duration === '7d') banExpires = new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString();
  else if (duration === 'permanent') banExpires = '2099-12-31';

  db.prepare(`
    UPDATE users SET is_banned = 1, ban_reason = ?, ban_expires = ? WHERE id = ?
  `).run(reason || 'Banned by admin', banExpires, userId);

  res.json({ success: true });
});

router.post('/users/:id/unban', (req, res) => {
  db.prepare(`
    UPDATE users SET is_banned = 0, ban_reason = NULL, ban_expires = NULL WHERE id = ?
  `).run(req.params.id);

  res.json({ success: true });
});

router.post('/users/:id/promote', (req, res) => {
  db.prepare('UPDATE users SET is_admin = 1 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/users/:id/demote', (req, res) => {
  db.prepare('UPDATE users SET is_admin = 0 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/users/:id/novux', (req, res) => {
  const { amount, reason } = req.body;
  const userId = parseInt(req.params.id);

  if (!amount) {
    return res.status(400).json({ error: 'Amount required' });
  }

  db.prepare('UPDATE users SET novux = novux + ? WHERE id = ?').run(amount, userId);
  
  db.prepare(`
    INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
    VALUES (NULL, ?, ?, 'admin', ?
  `).run(userId, amount, reason || 'Admin adjustment');

  res.json({ success: true });
});

router.get('/games', (req, res) => {
  const { search = '', page = 1, status = '' } = req.query;
  const limit = 20;
  const offset = (page - 1) * limit;

  let where = '1=1';
  const params = [];

  if (search) {
    where += ' AND g.title LIKE ?';
    params.push(`%${search}%`);
  }

  if (status === 'active') where += ' AND g.is_active = 1';
  if (status === 'inactive') where += ' AND g.is_active = 0';

  const games = db.prepare(`
    SELECT g.*, u.username as creator_username
    FROM games g
    JOIN users u ON g.creator_id = u.id
    WHERE ${where}
    ORDER BY g.created_at DESC
    LIMIT ? OFFSET ?
  `).all(...params, limit, offset);

  const total = db.prepare(`SELECT COUNT(*) as count FROM games g WHERE ${where}`).get(...params).count;

  res.json({ games, total, page, totalPages: Math.ceil(total / limit) });
});

router.post('/games/:id/feature', (req, res) => {
  db.prepare('UPDATE games SET is_featured = 1 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/games/:id/unfeature', (req, res) => {
  db.prepare('UPDATE games SET is_featured = 0 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/games/:id/deactivate', (req, res) => {
  const { reason } = req.body;
  db.prepare('UPDATE games SET is_active = 0 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/games/:id/reactivate', (req, res) => {
  db.prepare('UPDATE games SET is_active = 1 WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.post('/games/:id/delete', (req, res) => {
  db.prepare('DELETE FROM games WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.get('/catalog', (req, res) => {
  const { type = '', search = '', page = 1 } = req.query;
  const limit = 20;
  const offset = (page - 1) * limit;

  let where = '1=1';
  const params = [];

  if (type) {
    where += ' AND ci.type = ?';
    params.push(type);
  }

  if (search) {
    where += ' AND ci.name LIKE ?';
    params.push(`%${search}%`);
  }

  const items = db.prepare(`
    SELECT ci.*, u.username as creator_username
    FROM catalog_items ci
    JOIN users u ON ci.creator_id = u.id
    WHERE ${where}
    ORDER BY ci.created_at DESC
    LIMIT ? OFFSET ?
  `).all(...params, limit, offset);

  const total = db.prepare(`SELECT COUNT(*) as count FROM catalog_items ci WHERE ${where}`).get(...params).count;

  res.json({ items, total, page, totalPages: Math.ceil(total / limit) });
});

router.post('/catalog/add', upload.single('asset'), (req, res) => {
  const { name, description, type, price, is_limited, limited_quantity, hat_transform } = req.body;

  if (!name || !type || price === undefined) {
    return res.status(400).json({ error: 'Name, type and price required' });
  }

  const assetUrl = req.file ? `/uploads/catalog/${req.file.filename}` : null;
  const transform = hat_transform ? JSON.stringify(hat_transform) : '{}';

  const result = db.prepare(`
    INSERT INTO catalog_items (name, description, type, price, creator_id, asset_url, hat_transform, is_limited, limited_quantity)
    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
  `).run(name, description || '', type, parseInt(price), req.session.userId, assetUrl, transform, is_limited === 'true' ? 1 : 0, parseInt(limited_quantity) || null);

  res.json({ success: true, itemId: result.lastInsertRowid });
});

router.put('/catalog/:id', upload.single('asset'), (req, res) => {
  const { name, description, type, price, is_limited, limited_quantity, is_active, hat_transform } = req.body;

  let assetUrl = '';
  if (req.file) {
    assetUrl = `, asset_url = ?`;
  }

  const params = [
    name, description, type, parseInt(price), 
    is_limited === 'true' ? 1 : 0, parseInt(limited_quantity) || null,
    is_active === 'true' ? 1 : 0,
    hat_transform ? JSON.stringify(hat_transform) : null,
    req.params.id
  ];

  let query = `
    UPDATE catalog_items SET
      name = COALESCE(?, name),
      description = COALESCE(?, description),
      type = COALESCE(?, type),
      price = COALESCE(?, price),
      is_limited = COALESCE(?, is_limited),
      limited_quantity = COALESCE(?, limited_quantity),
      is_active = COALESCE(?, is_active)
  `;

  if (req.file) {
    query += `, asset_url = ?`;
    params.unshift(`/uploads/catalog/${req.file.filename}`);
  }

  query += ` WHERE id = ?`;
  params.push(req.params.id);

  db.prepare(query).run(...params);

  res.json({ success: true });
});

router.post('/catalog/:id/delete', (req, res) => {
  db.prepare('DELETE FROM catalog_items WHERE id = ?').run(req.params.id);
  db.prepare('DELETE FROM user_inventory WHERE item_id = ?').run(req.params.id);
  res.json({ success: true });
});

router.get('/reports', (req, res) => {
  const reports = db.prepare(`
    SELECT r.*, 
           u1.username as reporter_username,
           u2.username as reported_username
    FROM reports r
    JOIN users u1 ON r.reporter_id = u1.id
    LEFT JOIN users u2 ON r.reported_user_id = u2.id
    ORDER BY r.created_at DESC
    LIMIT 50
  `).all();

  res.json({ reports });
});

router.post('/reports/:id/resolve', (req, res) => {
  const { action = 'archived' } = req.body;
  db.prepare('UPDATE reports SET status = ? WHERE id = ?').run(action, req.params.id);
  res.json({ success: true });
});

router.get('/promocodes', (req, res) => {
  const codes = db.prepare('SELECT * FROM promo_codes ORDER BY created_at DESC').all();
  res.json({ codes });
});

router.post('/promocodes', (req, res) => {
  const { code, novux_amount, uses_remaining, expires_at } = req.body;

  if (!code || !novux_amount) {
    return res.status(400).json({ error: 'Code and amount required' });
  }

  try {
    db.prepare(`
      INSERT INTO promo_codes (code, novux_amount, uses_remaining, expires_at)
      VALUES (?, ?, ?, ?)
    `).run(code.toUpperCase(), parseInt(novux_amount), parseInt(uses_remaining) || null, expires_at || null);

    res.json({ success: true });
  } catch (error) {
    if (error.message.includes('UNIQUE')) {
      return res.status(400).json({ error: 'Code already exists' });
    }
    throw error;
  }
});

router.delete('/promocodes/:id', (req, res) => {
  db.prepare('DELETE FROM promo_codes WHERE id = ?').run(req.params.id);
  res.json({ success: true });
});

router.get('/settings', (req, res) => {
  const settings = db.prepare('SELECT * FROM platform_settings').all();
  const settingsObj = {};
  settings.forEach(s => { settingsObj[s.key] = s.value; });
  res.json({ settings: settingsObj });
});

router.post('/settings', (req, res) => {
  const { key, value } = req.body;

  if (!key) {
    return res.status(400).json({ error: 'Key required' });
  }

  db.prepare(`
    INSERT OR REPLACE INTO platform_settings (key, value) VALUES (?, ?)
  `).run(key, value);

  res.json({ success: true });
});

module.exports = router;