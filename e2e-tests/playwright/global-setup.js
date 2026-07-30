// global-setup.js
const fs = require('fs');
const path = require('path');

module.exports = async () => {
  // Delete the test database and its WAL/SHM files before each test run
  const webDir = path.resolve(__dirname, '../../VitaTrack.Web');
  const dbNames = ['VitaTrack.Test.db', 'VitaTrack.db'];

  for (const name of dbNames) {
    for (const suffix of ['', '-shm', '-wal']) {
      const file = path.join(webDir, name + suffix);
      if (fs.existsSync(file)) {
        fs.unlinkSync(file);
        console.log(`Cleaned: ${file}`);
      }
    }
  }
  
  console.log('Test database cleaned successfully');
};