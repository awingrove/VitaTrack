const { test, expect } = require('@playwright/test');
const path = require('path');
const fs = require('fs');

const SCREENSHOTS_DIR = path.join(__dirname, '..', 'screenshots');

async function screenshot(page, testInfo, label) {
  const testFile = path.basename(testInfo.file, '.js');
  const testTitle = testInfo.title.replace(/[^a-zA-Z0-9]/g, '-').replace(/-+/g, '-').toLowerCase();
  const dir = path.join(SCREENSHOTS_DIR, testFile);
  fs.mkdirSync(dir, { recursive: true });

  const filename = `${testTitle}-${label}.png`;
  const filePath = path.join(dir, filename);

  await page.screenshot({ path: filePath, fullPage: true });
  return filePath;
}

module.exports = { screenshot };
