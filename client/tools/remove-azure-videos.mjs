import fs from 'node:fs/promises';
import path from 'node:path';

async function main() {
  const distDir = path.resolve(process.cwd(), 'dist');
  const videoDir = path.join(distDir, 'video');

  try {
    const stat = await fs.stat(videoDir);
    if (!stat.isDirectory()) {
      return;
    }
  } catch {
    return;
  }

  await fs.rm(videoDir, { recursive: true, force: true });
  console.log(`[azure] removed ${videoDir}`);
}

main().catch((error) => {
  console.error('[azure] failed to remove dist/video', error);
  process.exitCode = 1;
});
