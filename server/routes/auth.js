const express = require('express');
const bcrypt = require('bcrypt');
const { db } = require('../db');
const { requireAuth, requireGuest } = require('../middleware/auth');

const router = express.Router();

router.post('/register', requireGuest, async (req, res) => {
  try {
    const { username, password, confirmPassword, email, dob } = req.body;

    if (!username || !password || !confirmPassword) {
      return res.status(400).json({ error: 'All fields are required' });
    }

    if (username.length < 3 || username.length > 20) {
      return res.status(400).json({ error: 'Username must be 3-20 characters' });
    }

    if (/\s/.test(username)) {
      return res.status(400).json({ error: 'Username cannot contain spaces' });
    }

    if (password.length < 8) {
      return res.status(400).json({ error: 'Password must be at least 8 characters' });
    }

    if (password !== confirmPassword) {
      return res.status(400).json({ error: 'Passwords do not match' });
    }

    const existingUser = db.prepare('SELECT id FROM users WHERE username = ?').get(username);
    if (existingUser) {
      return res.status(400).json({ error: 'Username already taken' });
    }

    const passwordHash = await bcrypt.hash(password, 12);

    const result = db.prepare(`
      INSERT INTO users (username, password_hash, email, novux, is_admin, avatar_data)
      VALUES (?, ?, ?, 100, 0, '{"head":"#f5d0a8","torso":"#f5d0a8","left_arm":"#f5d0a8","right_arm":"#f5d0a8","left_leg":"#f5d0a8","right_leg":"#f5d0a8","hat":null,"face":null,"shirt":null,"pants":null}')
    `).run(username, passwordHash, email || null);

    db.prepare(`
      INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
      VALUES (NULL, ?, 100, 'bonus', 'Registration bonus')
    `).run(result.lastInsertRowid);

    const user = db.prepare(`
      SELECT id, username, novux, is_admin, avatar_data, created_at
      FROM users WHERE id = ?
    `).get(result.lastInsertRowid);

    req.session.userId = user.id;
    req.session.username = user.username;
    req.session.isAdmin = user.is_admin === 1;

    res.json({
      success: true,
      user: {
        id: user.id,
        username: user.username,
        novux: user.novux,
        isAdmin: user.is_admin === 1,
        avatarData: JSON.parse(user.avatar_data || '{}')
      }
    });
  } catch (error) {
    console.error('Register error:', error);
    res.status(500).json({ error: 'Registration failed' });
  }
});

router.post('/login', requireGuest, async (req, res) => {
  try {
    const { username, password } = req.body;

    if (!username || !password) {
      return res.status(400).json({ error: 'Username and password required' });
    }

    const user = db.prepare('SELECT * FROM users WHERE username = ?').get(username);

    if (!user) {
      return res.status(401).json({ error: 'Invalid username or password' });
    }

    if (user.is_banned === 1) {
      const now = new Date().toISOString();
      if (user.ban_expires && new Date(user.ban_expires) < new Date()) {
        db.prepare('UPDATE users SET is_banned = 0, ban_reason = NULL, ban_expires = NULL WHERE id = ?').run(user.id);
      } else {
        return res.status(403).json({ error: user.ban_reason || 'Account banned' });
      }
    }

    const validPassword = await bcrypt.compare(password, user.password_hash);
    if (!validPassword) {
      return res.status(401).json({ error: 'Invalid username or password' });
    }

    db.prepare('UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = ?').run(user.id);

    req.session.userId = user.id;
    req.session.username = user.username;
    req.session.isAdmin = user.is_admin === 1;

    const today = new Date().toDateString();
    const lastLogin = user.last_login ? new Date(user.last_login).toDateString() : null;

    let dailyBonus = 0;
    if (lastLogin !== today) {
      dailyBonus = 10;
      db.prepare('UPDATE users SET novux = novux + 10 WHERE id = ?').run(user.id);
      db.prepare(`
        INSERT INTO transactions (from_user_id, to_user_id, amount, type, description)
        VALUES (NULL, ?, 10, 'daily', 'Daily login bonus')
      `).run(user.id);
    }

    res.json({
      success: true,
      user: {
        id: user.id,
        username: user.username,
        novux: user.novux + dailyBonus,
        isAdmin: user.is_admin === 1,
        avatarData: JSON.parse(user.avatar_data || '{}')
      },
      dailyBonus: dailyBonus
    });
  } catch (error) {
    console.error('Login error:', error);
    res.status(500).json({ error: 'Login failed' });
  }
});

router.post('/logout', (req, res) => {
  req.session.destroy((err) => {
    if (err) {
      return res.status(500).json({ error: 'Logout failed' });
    }
    res.json({ success: true });
  });
});

router.get('/session', (req, res) => {
  if (!req.session.userId) {
    return res.status(401).json({ authenticated: false });
  }

  const user = db.prepare(`
    SELECT id, username, novux, is_admin, avatar_data, created_at
    FROM users WHERE id = ?
  `).get(req.session.userId);

  if (!user) {
    req.session.destroy();
    return res.status(401).json({ authenticated: false });
  }

  res.json({
    authenticated: true,
    user: {
      id: user.id,
      username: user.username,
      novux: user.novux,
      isAdmin: user.is_admin === 1,
      avatarData: JSON.parse(user.avatar_data || '{}')
    }
  });
});

module.exports = router;