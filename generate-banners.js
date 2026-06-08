const sharp = require('sharp');
const path  = require('path');
const fs    = require('fs');

const DIR = path.join(__dirname, 'public/assets/images');

// Convert each existing source to WebP at the given output name.
// Desktop source is wide landscape → resize to 1920px wide.
// Mobile/tablet sources are already properly cropped portraits → just re-encode to WebP.
const JOBS = [
  // Slide 1 (banner-2)
  { src: 'banner-2.jpg',           out: 'banner-2-desktop.webp', maxW: 1920, quality: 85 },
  { src: 'banner-2-tablet-v3.jpg', out: 'banner-2-tablet.webp',  maxW: null, quality: 82 },
  { src: 'banner-2-mobile-v3.jpg', out: 'banner-2-mobile.webp',  maxW: null, quality: 80 },
  // Slide 2 (banner-3)
  { src: 'banner-3.jpg',           out: 'banner-3-desktop.webp', maxW: 1920, quality: 85 },
  { src: 'banner-3-tablet-v3.jpg', out: 'banner-3-tablet.webp',  maxW: null, quality: 82 },
  { src: 'banner-3-mobile-v3.jpg', out: 'banner-3-mobile.webp',  maxW: null, quality: 80 },
];

async function run() {
  for (const job of JOBS) {
    const srcPath = path.join(DIR, job.src);
    const outPath = path.join(DIR, job.out);
    if (!fs.existsSync(srcPath)) { console.warn(`SKIP (not found): ${job.src}`); continue; }

    const origKB = Math.round(fs.statSync(srcPath).size / 1024);
    const meta   = await sharp(srcPath).metadata();

    let pipeline = sharp(srcPath);
    if (job.maxW && meta.width > job.maxW) {
      pipeline = pipeline.resize({ width: job.maxW, withoutEnlargement: true });
    }
    const info = await pipeline.webp({ quality: job.quality }).toFile(outPath);
    const newKB = Math.round(info.size / 1024);
    console.log(`${job.src.padEnd(28)} → ${job.out.padEnd(28)}  ${origKB}KB → ${newKB}KB  (${meta.width}×${meta.height})`);
  }
  console.log('\nDone.');
}

run().catch(err => { console.error(err); process.exit(1); });
