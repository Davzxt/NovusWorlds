const bcrypt = require('bcrypt');
const { db, initializeDatabase } = require('./db');

async function seed() {
  initializeDatabase();

  console.log('Running seed...');

  const existingAdmin = db.prepare('SELECT id FROM users WHERE username = ?').get('NovusWorlds');
  
  if (!existingAdmin) {
    const passwordHash = await bcrypt.hash('admin2008', 12);
    
    db.prepare(`
      INSERT INTO users (username, password_hash, email, novux, is_admin, avatar_data)
      VALUES (?, ?, ?, 10000, 1, '{"head":"#f5d0a8","torso":"#f5d0a8","left_arm":"#f5d0a8","right_arm":"#f5d0a8","left_leg":"#f5d0a8","right_leg":"#f5d0a8","hat":null,"face":null,"shirt":null,"pants":null}')
    `).run('NovusWorlds', passwordHash, 'admin@novusworlds.com');

    console.log('Admin account created: NovusWorlds / admin2008');
  } else {
    console.log('Admin account already exists');
  }

  const settings = [
    ['platform_name', 'Novus Worlds'],
    ['registration_bonus', '100'],
    ['daily_bonus', '10'],
    ['allow_registrations', 'true'],
    ['maintenance_mode', 'false'],
    ['maintenance_message', '']
  ];

  const stmt = db.prepare('INSERT OR IGNORE INTO platform_settings (key, value) VALUES (?, ?)');
  settings.forEach(([key, value]) => stmt.run(key, value));

  console.log('Platform settings initialized');
  console.log('Seed completed!');
}

seed().catch(console.error).finally(() => {
  process.exit(0);
});