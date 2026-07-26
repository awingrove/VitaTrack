const fs = require('fs');
const path = require('path');

module.exports = async () => {
  const testDbPath = path.resolve(__dirname, '../../VitaTrack.Web/VitaTrack.Test.db');
  const testDbShm = testDbPath + '-shm';
  const testDbWal = testDbPath + '-wal';

  // Clean the test database before each run
  for (const file of [testDbPath, testDbShm, testDbWal]) {
    if (fs.existsSync(file)) {
      fs.unlinkSync(file);
      console.log(`Cleaned: ${file}`);
    }
  }
};
