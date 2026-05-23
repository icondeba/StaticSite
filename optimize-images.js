const sharp = require('sharp');
const fs = require('fs');
const path = require('path');

const PRODUCTS_DIR = path.join(__dirname, 'public/assets/images/products');
const MAX_PX = 900;
const WEBP_QUALITY = 82;

async function processDir(dir) {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      await processDir(fullPath);
    } else if (/\.(png|jpg|jpeg)$/i.test(entry.name)) {
      const webpName = entry.name.replace(/\.(png|jpg|jpeg)$/i, '.webp');
      const webpPath = path.join(dir, webpName);
      try {
        const info = await sharp(fullPath)
          .resize({ width: MAX_PX, height: MAX_PX, fit: 'inside', withoutEnlargement: true })
          .webp({ quality: WEBP_QUALITY })
          .toFile(webpPath);
        const origKB = Math.round(fs.statSync(fullPath).size / 1024);
        const newKB  = Math.round(info.size / 1024);
        console.log(`${origKB}KB → ${newKB}KB  ${path.relative(PRODUCTS_DIR, webpPath)}`);
        fs.unlinkSync(fullPath);
      } catch (e) {
        console.error(`SKIP ${fullPath}: ${e.message}`);
      }
    }
  }
}

processDir(PRODUCTS_DIR).then(() => console.log('\nDone.')).catch(console.error);
