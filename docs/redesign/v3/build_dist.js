const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const baseDir = path.resolve(__dirname);
const distDir = path.join(baseDir, 'dist');

console.log('==> Building UI distribution for WebShell...');

// 1. Run assemble.js
execSync(`node "${path.join(baseDir, 'assemble.js')}"`, { stdio: 'inherit' });

// 2. Prepare dist directory
if (fs.existsSync(distDir)) {
  fs.rmSync(distDir, { recursive: true, force: true });
}
fs.mkdirSync(distDir, { recursive: true });

function copyRecursive(src, dest) {
  const stat = fs.statSync(src);
  if (stat.isDirectory()) {
    fs.mkdirSync(dest, { recursive: true });
    for (const item of fs.readdirSync(src)) {
      copyRecursive(path.join(src, item), path.join(dest, item));
    }
  } else {
    fs.copyFileSync(src, dest);
  }
}

// 3. Copy assets, screens, src, tokens.css, index.html to dist/
copyRecursive(path.join(baseDir, 'assets'), path.join(distDir, 'assets'));
copyRecursive(path.join(baseDir, 'screens'), path.join(distDir, 'screens'));
if (fs.existsSync(path.join(baseDir, 'src'))) {
  copyRecursive(path.join(baseDir, 'src'), path.join(distDir, 'src'));
}
fs.copyFileSync(path.join(baseDir, 'tokens.css'), path.join(distDir, 'tokens.css'));
fs.copyFileSync(path.join(baseDir, 'index.html'), path.join(distDir, 'index.html'));

console.log(`✅ UI distribution ready at: ${distDir}`);
