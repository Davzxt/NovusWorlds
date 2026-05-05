const express = require('express');
const { prepare } = require('../db');
const { requireAuth } = require('../middleware/auth');

const router = express.Router();

router.get('/', requireAuth, (req, res) => {
  const user = prepare('SELECT avatar_data FROM users WHERE id = ?')
    .get(req.session.userId);

  res.json({ avatarData: JSON.parse(user.avatar_data || '{}') });
});

router.post('/save', requireAuth, (req, res) => {
  const { avatarData } = req.body;

  if (!avatarData) {
    return res.status(400).json({ error: 'Avatar data required' });
  }

  prepare('UPDATE users SET avatar_data = ? WHERE id = ?')
    .run(JSON.stringify(avatarData), req.session.userId);

  res.json({ success: true });
});

router.get('/equip/:itemId', requireAuth, (req, res) => {
  const owned = prepare('SELECT * FROM user_inventory WHERE user_id = ? AND item_id = ?')
    .get(req.session.userId, req.params.itemId);

  if (!owned) {
    return res.status(400).json({ error: 'You do not own this item' });
  }

  const item = prepare('SELECT * FROM catalog_items WHERE id = ?').get(req.params.itemId);
  const user = prepare('SELECT avatar_data FROM users WHERE id = ?')
    .get(req.session.userId);

  const avatarData = JSON.parse(user.avatar_data || '{}');

  if (item.type === 'hat') {
    if (!avatarData.hat) avatarData.hat = [];
    if (!avatarData.hat.includes(parseInt(req.params.itemId))) {
      if (avatarData.hat.length >= 3) {
        return res.status(400).json({ error: 'Maximum 3 hats allowed' });
      }
      avatarData.hat.push(parseInt(req.params.itemId));
    }
  } else if (item.type === 'face') {
    avatarData.face = parseInt(req.params.itemId);
  } else if (item.type === 'shirt') {
    avatarData.shirt = parseInt(req.params.itemId);
  } else if (item.type === 'pants') {
    avatarData.pants = parseInt(req.params.itemId);
  }

  prepare('UPDATE users SET avatar_data = ? WHERE id = ?')
    .run(JSON.stringify(avatarData), req.session.userId);

  res.json({ success: true, avatarData });
});

router.get('/unequip/:itemId', requireAuth, (req, res) => {
  const item = prepare('SELECT * FROM catalog_items WHERE id = ?').get(req.params.itemId);
  const user = prepare('SELECT avatar_data FROM users WHERE id = ?')
    .get(req.session.userId);

  const avatarData = JSON.parse(user.avatar_data || '{}');

  if (item.type === 'hat') {
    if (avatarData.hat) {
      avatarData.hat = avatarData.hat.filter(id => id !== parseInt(req.params.itemId));
    }
  } else if (item.type === 'face') {
    delete avatarData.face;
  } else if (item.type === 'shirt') {
    delete avatarData.shirt;
  } else if (item.type === 'pants') {
    delete avatarData.pants;
  }

  prepare('UPDATE users SET avatar_data = ? WHERE id = ?')
    .run(JSON.stringify(avatarData), req.session.userId);

  res.json({ success: true, avatarData });
});

router.get('/preview/:itemId', requireAuth, (req, res) => {
  const item = prepare('SELECT * FROM catalog_items WHERE id = ?').get(req.params.itemId);

  if (!item) {
    return res.status(404).json({ error: 'Item not found' });
  }

  res.json({ item });
});

module.exports = router;