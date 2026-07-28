// global-setup.js
const fs = require('fs');
const path = require('path');

module.exports = async () => {
  // Delete the test database and its WAL/SHM files before each test run
  const testDbPath = path.resolve(__dirname, '../../VitaTrack.Web/VitaTrack.Test.db');
  const testDbShm = testDbPath + '-shm';
  const testDbWal = testDbPath + '-wal';

  for (const file of [testDbPath, testDbPath + '-shm', testDbPath + '-wal']) {
    if (fs.existsSync(file)) {
      fs.unlinkSync(file);
      console.log(`Cleaned test database file: ${file}`);
    }
  }
  
  console.log('Test database cleaned successfully');
};