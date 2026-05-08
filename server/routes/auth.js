const express = require('express');
const bcrypt = require('bcrypt');
const db = require('../db');
const router = express.Router();

const attempts = new Map();

function cleanUsername(username) {
  return String(username || '').trim();
}

function publicUser(row) {
  if (!row) return null;
  return {
    id: row.id,
    username: row.username,
    novux: row.novux,
    is_admin: !!row.is_admin,
    avatar_data: JSON.parse(row.avatar_data || '{}'),
    created_at: row.created_at,
    login_notice: row.login_notice
  };
}

router.get('/me', (req, res) => {
  if (!req.session.user) return res.json({ user: null });
  const user = db.prepare('SELECT * FROM users WHERE id = ? AND deleted_at IS NULL').get(req.session.user.id);
  req.session.user = publicUser(user);
  res.json({ user: req.session.user });
});

router.post('/register', async (req, res) => {
  const username = cleanUsername(req.body.username);
  const password = String(req.body.password || '');
  const confirm = String(req.body.confirm || '');
  if (!/^[A-Za-z0-9_]{3,20}$/.test(username)) return res.status(400).json({ error: 'Username deve ter 3-20 caracteres, sem espacos.' });
  if (password.length < 8) return res.status(400).json({ error: 'Senha deve ter pelo menos 8 caracteres.' });
  if (password !== confirm) return res.status(400).json({ error: 'Confirmacao de senha diferente.' });
  try {
    const bonus = Number(db.prepare("SELECT value FROM platform_settings WHERE key = 'register_bonus'").get()?.value || 100);
    const hash = await bcrypt.hash(password, 12);
    const avatar = JSON.stringify({ colors: { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' }, hats: [] });
    const info = db.prepare('INSERT INTO users (username, password_hash, novux, avatar_data) VALUES (?, ?, ?, ?)').run(username, hash, bonus, avatar);
    const freeItems = db.prepare("SELECT id FROM catalog_items WHERE price = 0 OR name = 'Classic Visor'").all();
    for (const item of freeItems) db.prepare('INSERT OR IGNORE INTO user_inventory (user_id, item_id) VALUES (?, ?)').run(info.lastInsertRowid, item.id);
    db.prepare('INSERT INTO transactions (to_user_id, amount, type, description) VALUES (?, ?, ?, ?)').run(info.lastInsertRowid, bonus, 'bonus', 'Bonus de registro');
    db.prepare('INSERT INTO activity_log (type, message) VALUES (?, ?)').run('user', `Novo usuario: ${username}`);
    req.session.user = publicUser(db.prepare('SELECT * FROM users WHERE id = ?').get(info.lastInsertRowid));
    res.json({ user: req.session.user });
  } catch (err) {
    res.status(400).json({ error: 'Username ja esta em uso.' });
  }
});

router.post('/login', async (req, res) => {
  const ip = req.ip;
  const now = Date.now();
  const state = attempts.get(ip) || { count: 0, until: 0 };
  if (state.until > now) return res.status(429).json({ error: 'Muitas tentativas. Aguarde um minuto.' });
  const username = cleanUsername(req.body.username);
  const user = db.prepare('SELECT * FROM users WHERE username = ? AND deleted_at IS NULL').get(username);
  const ok = user && await bcrypt.compare(String(req.body.password || ''), user.password_hash);
  if (!ok) {
    state.count += 1;
    if (state.count >= 5) state.until = now + 60000;
    attempts.set(ip, state);
    return res.status(400).json({ error: 'Usuario ou senha invalidos.' });
  }
  if (user.is_banned && (!user.ban_expires || new Date(user.ban_expires) > new Date())) {
    return res.status(403).json({ error: user.ban_reason || 'Conta banida.' });
  }
  attempts.delete(ip);
  if (req.body.remember) req.session.cookie.maxAge = 30 * 24 * 60 * 60 * 1000;
  const today = new Date().toISOString().slice(0, 10);
  if ((user.last_daily || '').slice(0, 10) !== today) {
    db.prepare('UPDATE users SET novux = novux + 10, last_daily = CURRENT_TIMESTAMP WHERE id = ?').run(user.id);
    db.prepare('INSERT INTO transactions (to_user_id, amount, type, description) VALUES (?, 10, ?, ?)').run(user.id, 'daily', 'Login diario');
  }
  db.prepare('UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = ?').run(user.id);
  req.session.user = publicUser(db.prepare('SELECT * FROM users WHERE id = ?').get(user.id));
  res.json({ user: req.session.user });
});

router.post('/logout', (req, res) => {
  req.session.destroy(() => res.json({ ok: true }));
});

module.exports = router;
