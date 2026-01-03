import { promises as fs } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const projectRoot = path.resolve(__dirname, '..');
const iconsDir = path.join(projectRoot, 'public', 'images', 'club-icon');
const outFile = path.join(projectRoot, 'public', 'data', 'club-icons-manifest.json');

const normalizeNewlines = (text) => text.replaceAll('\r\n', '\n');

const run = async () => {
  const entries = await fs.readdir(iconsDir, { withFileTypes: true });
  const files = entries
    .filter((entry) => entry.isFile())
    .map((entry) => entry.name)
    .filter((name) => name.toLowerCase().endsWith('.png'))
    .sort((a, b) => a.localeCompare(b));

  const manifest = { files };
  const nextContent = JSON.stringify(manifest, null, 2) + '\n';

  let prevContent = null;
  try {
    prevContent = await fs.readFile(outFile, 'utf8');
  } catch {
    // ignore
  }

  if (prevContent !== null && normalizeNewlines(prevContent) === normalizeNewlines(nextContent)) {
    return;
  }

  await fs.mkdir(path.dirname(outFile), { recursive: true });
  await fs.writeFile(outFile, nextContent, 'utf8');
  // eslint-disable-next-line no-console
  console.log(`Generated club icons manifest: ${files.length} files -> ${outFile}`);
};

run().catch((err) => {
  // eslint-disable-next-line no-console
  console.error('Failed to generate club icons manifest', err);
  process.exitCode = 1;
});
