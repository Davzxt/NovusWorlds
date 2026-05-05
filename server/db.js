const initSqlJs = require('sql.js');
const fs = require('fs');
const path = require('path');

const dbPath = path.join(__dirname, '..', 'novus.db');
let db = null;
let SQL = null;

async function initializeDatabase() {
  if (!SQL) {
    SQL = await initSqlJs();
  }
  
  if (fs.existsSync(dbPath)) {
    const fileBuffer = fs.readFileSync(dbPath);
    db = new SQL.Database(fileBuffer);
  } else {
    db = new SQL.Database();
  }

  db.run(`
    CREATE TABLE IF NOT EXISTS users (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      username TEXT UNIQUE NOT NULL,
      password_hash TEXT NOT NULL,
      email TEXT,
      novux INTEGER DEFAULT 100,
      is_admin INTEGER DEFAULT 0,
      is_banned INTEGER DEFAULT 0,
      ban_reason TEXT,
      ban_expires TEXT,
      avatar_data TEXT DEFAULT '{}',
      created_at TEXT DEFAULT CURRENT_TIMESTAMP,
      last_login TEXT
    )
  `;

  db.run(`
    CREATE TABLE IF NOT EXISTS games (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      title TEXT NOT NULL,
      description TEXT,
      creator_id INTEGER,
      map_data TEXT NOT NULL,
      thumbnail_url TEXT,
      is_active INTEGER DEFAULT 1,
      is_featured INTEGER DEFAULT 0,
      visit_count INTEGER DEFAULT 0,
      max_players INTEGER DEFAULT 20,
      created_at TEXT DEFAULT CURRENT_TIMESTAMP,
      updated_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS catalog_items (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT NOT NULL,
      description TEXT,
      type TEXT NOT NULL,
      price INTEGER NOT NULL DEFAULT 0,
      creator_id INTEGER,
      asset_url TEXT,
      thumbnail_url TEXT,
      hat_transform TEXT DEFAULT '{}',
      is_limited INTEGER DEFAULT 0,
      limited_quantity INTEGER,
      is_active INTEGER DEFAULT 1,
      sales_count INTEGER DEFAULT 0,
      created_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS user_inventory (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      user_id INTEGER,
      item_id INTEGER,
      purchased_at TEXT DEFAULT CURRENT_TIMESTAMP,
      UNIQUE(user_id, item_id)
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS transactions (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      from_user_id INTEGER,
      to_user_id INTEGER,
      amount INTEGER NOT NULL,
      type TEXT,
      description TEXT,
      created_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS friendships (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      requester_id INTEGER,
      receiver_id INTEGER,
      status TEXT DEFAULT 'pending',
      created_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS reports (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      reporter_id INTEGER,
      reported_user_id INTEGER,
      content_type TEXT,
      content_id INTEGER,
      reason TEXT,
      status TEXT DEFAULT 'pending',
      created_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS promo_codes (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      code TEXT UNIQUE NOT NULL,
      novux_amount INTEGER NOT NULL,
      uses_remaining INTEGER,
      expires_at TEXT,
      created_at TEXT DEFAULT CURRENT_TIMESTAMP
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS platform_settings (
      key TEXT PRIMARY KEY,
      value TEXT
    )
  `);

  db.run(`
    CREATE TABLE IF NOT EXISTS sessions (
      sid TEXT PRIMARY KEY,
      sess TEXT NOT NULL,
      expired TEXT NOT NULL
    )
  `);

  saveDatabase();
  console.log('Database initialized successfully');
  
  return db;
}

function saveDatabase() {
  if (db) {
    const data = db.export();
    const buffer = Buffer.from(data);
    fs.writeFileSync(dbPath, buffer);
  }
}

function getDb() {
  return db;
}

function prepare(sql) {
  return {
    run: (...params) => {
      db.run(sql, params);
      saveDatabase();
      return { lastInsertRowid: db.exec("SELECT last_insert_rowid()")[0]?.values[0][0] };
    },
    get: (...params) => {
      const stmt = db.prepare(sql);
      stmt.bind(params);
      if (stmt.step()) {
        const row = stmt.getAsObject();
        stmt.free();
        return row;
      }
      stmt.free();
      return undefined;
    },
    all: (...params) => {
      const stmt = db.prepare(sql);
      stmt.bind(params);
      const results = [];
      while (stmt.step()) {
        results.push(stmt.getAsObject());
      }
      stmt.free();
      return results;
    }
  };
}

module.exports = { initializeDatabase, getDb, prepare, saveDatabase };