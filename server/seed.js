const bcrypt = require('bcrypt');
const { initializeDatabase, prepare, saveDatabase } = require('./db');

async function seed() {
  await initializeDatabase();

  console.log('Running seed...');

  const existingAdmin = prepare('SELECT id FROM users WHERE username = ?').get('NovusWorlds');
  let adminId = existingAdmin ? existingAdmin.id : null;
  
  if (!existingAdmin) {
    const passwordHash = await bcrypt.hash('admin2008', 12);
    
    prepare(`
      INSERT INTO users (username, password_hash, email, novux, is_admin, avatar_data)
      VALUES (?, ?, ?, 10000, 1, '{"head":"#f5d0a8","torso":"#f5d0a8","left_arm":"#f5d0a8","right_arm":"#f5d0a8","left_leg":"#f5d0a8","right_leg":"#f5d0a8","hat":null,"face":null,"shirt":null,"pants":null}')
    `).run('NovusWorlds', passwordHash, 'admin@novusworlds.com');

    adminId = prepare('SELECT last_insert_rowid() as id').get().id;
    console.log('Admin account created: NovusWorlds / admin2008');
  } else {
    console.log('Admin account already exists');
  }

  const testGame = prepare("SELECT id FROM games WHERE title = ?").get('Physics Test World');
  if (!testGame && adminId) {
    const mapData = {
      name: 'Physics Test World',
      version: 1,
      objects: [
        {
          id: 'baseplate',
          type: 'Part',
          name: 'Baseplate',
          position: { x: 0, y: -1, z: 0 },
          rotation: { x: 0, y: 0, z: 0 },
          size: { x: 100, y: 2, z: 100 },
          color: '#6B8E23',
          material: 'Grass',
          anchored: true,
          canCollide: true,
          transparency: 0
        },
        {
          id: 'ramp1',
          type: 'Part',
          name: 'Ramp',
          position: { x: 10, y: 2, z: 10 },
          rotation: { x: 0, y: 45, z: 15 },
          size: { x: 10, y: 1, z: 15 },
          color: '#888888',
          material: 'Stone',
          anchored: true,
          canCollide: true,
          transparency: 0
        },
        {
          id: 'wall1',
          type: 'Part',
          name: 'Wall',
          position: { x: -10, y: 3, z: 0 },
          rotation: { x: 0, y: 0, z: 0 },
          size: { x: 1, y: 6, z: 20 },
          color: '#B22222',
          material: 'Brick',
          anchored: true,
          canCollide: true,
          transparency: 0
        },
        {
          id: 'platform1',
          type: 'Part',
          name: 'Moving Platform',
          position: { x: 0, y: 5, z: -15 },
          rotation: { x: 0, y: 0, z: 0 },
          size: { x: 8, y: 1, z: 8 },
          color: '#4169E1',
          material: 'Plastic',
          anchored: false,
          canCollide: true,
          transparency: 0
        },
        {
          id: 'spawn1',
          type: 'Part',
          name: 'SpawnArea',
          position: { x: 0, y: 1, z: 20 },
          rotation: { x: 0, y: 0, z: 0 },
          size: { x: 10, y: 1, z: 10 },
          color: '#90EE90',
          material: 'Plastic',
          anchored: true,
          canCollide: true,
          transparency: 0
        }
      ],
      spawnPoints: [
        { x: 0, y: 3, z: 20 },
        { x: 10, y: 3, z: -10 },
        { x: -10, y: 3, z: 0 }
      ],
      ambient: '#404040',
      skyColor: '#87CEEB'
    };

    prepare(`
      INSERT INTO games (title, description, creator_id, map_data, is_active, is_featured, visit_count)
      VALUES (?, ?, ?, ?, 1, 1, 100)
    `).run('Physics Test World', 'Test the physics! Jump ramps, climb walls, explore platforms.', adminId, JSON.stringify(mapData));

    console.log('Test game created: Physics Test World');
  } else {
    console.log('Test game already exists');
  }

  const settings = [
    ['platform_name', 'Novus Worlds'],
    ['registration_bonus', '100'],
    ['daily_bonus', '10'],
    ['allow_registrations', 'true'],
    ['maintenance_mode', 'false'],
    ['maintenance_message', '']
  ];

  settings.forEach(([key, value]) => {
    prepare('INSERT OR IGNORE INTO platform_settings (key, value) VALUES (?, ?)').run(key, value);
  });

  console.log('Platform settings initialized');
  console.log('Seed completed!');
}

seed().catch(console.error).finally(() => {
  process.exit(0);
});