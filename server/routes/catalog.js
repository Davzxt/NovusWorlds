const express = require('express');
const { db } = require('../db');
const { requireAuth } = require('../middleware/auth');

const router = express.Router();

router.get('/', (req, res) => {
  const { type = '', minPrice = 0, maxPrice = 999999, sort = 'recent', search = '' } = req.query;
  
  let where = 'WHERE ci.is_active = 1';
  const params = [];
  
  if (type) {
    where += ' AND ci.type = ?';
    params.push(type);
  }
  
  if (minPrice > 0) {
    where += ' AND ci.price >= ?';
    params.push(parseInt(minPrice));
  }
  
  if (maxPrice < 999999) {
    where += ' AND ci.price <= ?';
    params.push(parseInt(maxPrice));
  }
  
  if (search) {
    where += ' AND ci.name LIKE ?';
    params.push(`%${search}%`);
  }

  let orderBy = 'ci.created_at DESC';
  if (sort === 'price_asc') orderBy = 'ci.price ASC';
  if (sort === 'price_desc') orderBy = 'ci.price DESC';
  if (sort === 'popular') orderBy = 'ci.sales_count DESC';

  const items = db.prepare(`
    SELECT ci.id, ci.name, ci.description, ci.type, ci.price, ci.thumbnail_url, ci.sales_count,
           u.username as creator_username
    FROM catalog_items ci
    JOIN users u ON ci.creator_id = u.id
    ${where}
    ORDER BY ${orderBy}
    LIMIT 50
  `).all(...params);

  res.json({ items });
});

router.get('/featured', (req, res) => {
  const items = db.prepare(`
    SELECT ci.id, ci.name, ci.description, ci.type, ci.price, ci.thumbnail_url,
           u.username as creator_username
    FROM catalog_items ci
    JOIN users u ON ci.creator_id = u.id
    WHERE ci.is_active = 1
    ORDER BY ci.sales_count DESC
    LIMIT 6
  `).all();

  res.json({ items });
});

router.get('/:id', (req, res) => {
  const item = db.prepare(`
    SELECT ci.*, u.username as creator_username
    FROM catalog_items ci
    JOIN users u ON ci.creator_id = u.id
    WHERE ci.id = ?
  `).get(req.params.id);

  if (!item) {
    return res.status(404).json({ error: 'Item not found' });
  }

  res.json({ item });
});

router.post('/buy', requireAuth, (req, res) => {
  const { itemId } = req.body;
  const userId = req.session.userId;

  const item = db.prepare('SELECT * FROM catalog_items WHERE id = ? AND is_active = 1')
    .get(itemId);

  if (!item) {
    return res.status(404).json({ error: 'Item not found' });
  }

  const owned = db.prepare('SELECT * FROM user_inventory WHERE user_id = ? AND item_id = ?')
    .get(userId, itemId);

  if (owned) {
    return res.status(400).json({ error: 'You already own this item' });
  }

  const user = db.prepare('SELECT novux FROM users WHERE id = ?').get(userId);

  if (user.novux < item.price) {
    return res.status(400).json({ error: 'Not enough Novux' });
  }

  if (item.is_limited === 1 && item.limited_quantity <= 0) {
    return res.status(400).json({ error: 'Item out of stock' });
  }

  db.prepare('UPDATE users SET novux = novux - ? WHERE id = ?').run(item.price, userId);
  
  db.prepare('INSERT INTO user_inventory (user_id, item_id) VALUES (?, ?)').run(userId, itemId);
  
  db.prepare('UPDATE catalog_items SET sales_count = sales_count + 1 WHERE id = ?').run(itemId);
  
  if (item.is_limited === 1) {
    db.prepare('UPDATE catalog_items SET limited_quantity = limited_quantity - 1 WHERE id = ?')
      .run(itemId);
  }

  const creator = db.prepare('SELECT * FROM users WHERE id = ?').get(item.creator_id);
  const creatorShare = Math.floor(item.price * 0.8);
  
  if (creator && creator.id !== userId) {
    db.prepare('UPDATE users SET novux = novux + ? WHERE id = ?').run(creatorShare, item.creator_id);
    db.prepare(`
      INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
      VALUES (?, ?, ?, 'sale', 'Item sale: ?')
    `).run(userId, item.creator_id, creatorShare, item.name);
  }

  db.prepare(`
    INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
    VALUES (?, NULL, ?, 'purchase', 'Purchased: ?')
  `).run(userId, -item.price, item.name);

  res.json({ success: true });
});

router.get('/user/inventory', requireAuth, (req, res) => {
  const items = db.prepare(`
    SELECT ci.*, ui.purchased_at
    FROM user_inventory ui
    JOIN catalog_items ci ON ui.item_id = ci.id
    WHERE ui.user_id = ?
    ORDER BY ui.purchased_at DESC
  `).all(req.session.userId);

  res.json({ items });
});

router.get('/user/owned/:itemId', requireAuth, (req, res) => {
  const owned = db.prepare('SELECT * FROM user_inventory WHERE user_id = ? AND item_id = ?')
    .get(req.session.userId, req.params.itemId);

  res.json({ owned: !!owned });
});

module.exports = router;