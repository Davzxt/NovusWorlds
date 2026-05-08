const express = require('express');
const db = require('../db');
const { requireAuth } = require('../middleware/auth');
const router = express.Router();

router.get('/me', requireAuth, (req, res) => {
  const user = db.prepare('SELECT avatar_data FROM users WHERE id = ?').get(req.session.user.id);
  const inventory = db.prepare('SELECT catalog_items.* FROM user_inventory JOIN catalog_items ON catalog_items.id = user_inventory.item_id WHERE user_id = ? AND is_active = 1').all(req.session.user.id);
  res.json({ avatar: JSON.parse(user.avatar_data || '{}'), inventory });
});

router.post('/save', requireAuth, (req, res) => {
  const data = req.body.avatar || {};
  const owned = new Set(db.prepare('SELECT item_id FROM user_inventory WHERE user_id = ?').all(req.session.user.id).map((r) => r.item_id));
  const hats = (data.hats || []).slice(0, 3).filter((id) => owned.has(Number(id))).map(Number);
  const avatar = { colors: data.colors || {}, face: data.face || null, shirt: data.shirt || null, pants: data.pants || null, hats };
  db.prepare('UPDATE users SET avatar_data = ? WHERE id = ?').run(JSON.stringify(avatar), req.session.user.id);
  res.json({ ok: true, avatar });
});

module.exports = router;
