const path = require('path');
const bcrypt = require('bcrypt');
const Database = require('better-sqlite3');

const db = new Database(path.join(__dirname, '..', 'novus.sqlite'));
db.pragma('journal_mode = WAL');
db.pragma('foreign_keys = ON');

function migrate() {
  db.exec(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      username TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL,
      email TEXT,
      novux INTEGER DEFAULT 100,
      is_admin INTEGER DEFAULT 0,
      is_banned INTEGER DEFAULT 0,
      ban_reason TEXT,
      ban_expires DATETIME,
      avatar_data TEXT DEFAULT '{}',
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
      last_login DATETIME,
      last_daily DATETIME,
      deleted_at DATETIME,
      login_notice TEXT
    );
    CREATE TABLE IF NOT EXISTS games (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      title TEXT NOT NULL,
      description TEXT,
      creator_id INTEGER REFERENCES users(id),
      map_data TEXT NOT NULL,
      thumbnail_url TEXT,
      is_active INTEGER DEFAULT 1,
      is_featured INTEGER DEFAULT 0,
      visit_count INTEGER DEFAULT 0,
      max_players INTEGER DEFAULT 20,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
      updated_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS catalog_items (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT NOT NULL,
      description TEXT,
      type TEXT NOT NULL CHECK(type IN ('hat','face','shirt','pants')),
      price INTEGER NOT NULL DEFAULT 0,
      creator_id INTEGER REFERENCES users(id),
      asset_url TEXT,
      model_url TEXT,
      thumbnail_url TEXT,
      hat_transform TEXT DEFAULT '{}',
      is_limited INTEGER DEFAULT 0,
      limited_quantity INTEGER,
      is_active INTEGER DEFAULT 1,
      sales_count INTEGER DEFAULT 0,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS user_inventory (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER REFERENCES users(id),
      item_id INTEGER REFERENCES catalog_items(id),
      purchased_at DATETIME DEFAULT CURRENT_TIMESTAMP,
      UNIQUE(user_id, item_id)
    );
    CREATE TABLE IF NOT EXISTS transactions (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      from_user_id INTEGER,
      to_user_id INTEGER,
      amount INTEGER NOT NULL,
      type TEXT,
      description TEXT,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS friendships (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      requester_id INTEGER REFERENCES users(id),
      receiver_id INTEGER REFERENCES users(id),
      status TEXT DEFAULT 'pending',
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      reporter_id INTEGER REFERENCES users(id),
      reported_user_id INTEGER,
      content_type TEXT,
      content_id INTEGER,
      reason TEXT,
      status TEXT DEFAULT 'pending',
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS promo_codes (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      code TEXT UNIQUE NOT NULL,
      novux_amount INTEGER NOT NULL,
      uses_remaining INTEGER,
      expires_at DATETIME,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS platform_settings (
      key TEXT PRIMARY KEY,
      value TEXT
    );
    CREATE TABLE IF NOT EXISTS chat_messages (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER REFERENCES users(id),
      username TEXT,
      room TEXT DEFAULT 'global',
      message TEXT NOT NULL,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
    CREATE TABLE IF NOT EXISTS activity_log (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      type TEXT,
      message TEXT,
      created_at DATETIME DEFAULT CURRENT_TIMESTAMP
    );
  `);
}

function seed() {
  const hash = bcrypt.hashSync('16477709d', 12);
  db.prepare(`
    INSERT INTO users (username, password_hash, novux, is_admin, avatar_data)
    VALUES ('NovusWorlds', ?, 100000, 1, ?)
    ON CONFLICT(username) DO UPDATE SET is_admin = 1, password_hash = excluded.password_hash
  `).run(hash, JSON.stringify({ colors: { head: '#f5cd30', torso: '#0d69ac', arms: '#f5cd30', legs: '#1b2a35' }, hats: [] }));

  const map = JSON.stringify({
    name: 'Classic Baseplate',
    version: 1,
    objects: [
      { id: 'baseplate', type: 'Part', name: 'Baseplate', position: { x: 0, y: -0.5, z: 0 }, rotation: { x: 0, y: 0, z: 0 }, size: { x: 80, y: 1, z: 80 }, color: '#6B8E23', material: 'Grass', anchored: true, canCollide: true, transparency: 0, children: [] },
      { id: 'brick-red', type: 'Part', name: 'Red Brick', position: { x: 5, y: 1, z: -4 }, rotation: { x: 0, y: 0, z: 0 }, size: { x: 4, y: 2, z: 4 }, color: '#c4281c', material: 'Brick', anchored: true, canCollide: true, transparency: 0, children: [] }
    ],
    spawnPoints: [{ x: 0, y: 4, z: 0 }],
    ambient: '#404040',
    skyColor: '#87CEEB'
  });
  const adminId = db.prepare('SELECT id FROM users WHERE username = ?').get('NovusWorlds').id;
  db.prepare(`
    INSERT INTO games (title, description, creator_id, map_data, thumbnail_url, is_featured)
    SELECT 'Classic Baseplate', 'Um mundo inicial para construir, jogar e testar.', ?, ?, '/assets/textures/game-default.svg', 1
    WHERE NOT EXISTS (SELECT 1 FROM games WHERE title = 'Classic Baseplate')
  `).run(adminId, map);
  db.prepare(`
    INSERT INTO catalog_items (name, description, type, price, creator_id, asset_url, thumbnail_url, hat_transform, is_active)
    SELECT 'Classic Visor', 'Um chapeu simples para o avatar R6.', 'hat', 25, ?, '/assets/catalog/classic-visor.gltf', '/assets/textures/item-default.svg',
      '{"position":{"x":0,"y":2.85,"z":0},"rotation":{"x":0,"y":0,"z":0},"scale":{"x":1,"y":1,"z":1}}', 1
    WHERE NOT EXISTS (SELECT 1 FROM catalog_items WHERE name = 'Classic Visor')
  `).run(adminId);
  db.prepare('INSERT OR IGNORE INTO platform_settings (key, value) VALUES (?, ?)').run('platform_name', 'Novus Worlds');
  db.prepare('INSERT OR IGNORE INTO platform_settings (key, value) VALUES (?, ?)').run('register_bonus', '100');
}

migrate();
seed();

module.exports = db;

if (process.argv.includes('--seed')) {
  console.log('Novus Worlds database migrated and seeded.');
}
