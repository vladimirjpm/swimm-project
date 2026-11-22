// Проверка листа тренировки перед занесением (docs/workouts/README.md).
//
//   node docs/workouts/tools/workout-check.mjs parse  <лист.txt>   — что поймёт «Paste» сайта (CF7)
//   node docs/workouts/tools/workout-check.mjs totals <workout.json> — метры по уровням группы 17
//
// Берёт настоящий разборщик и подсчёт клиента, без копий логики. Node ≥ 23.5 (как у тестов workout).

import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const dir = path.resolve(here, '../../../client/src/projects/hub-groups-project/components/trainings/workout');
const url = (f) => pathToFileURL(path.join(dir, f)).href;

await import(url('ts-resolve-hook.mjs'));

// Уровни группы 17 (Dolphin Netanya Masters) сверху вниз: Advanced, Intermediate, Developing, Beginner.
const LEVELS = [
  [37, 'Advanced'],
  [38, 'Intermediate'],
  [39, 'Developing'],
  [36, 'Beginner'],
];

const [mode, file] = process.argv.slice(2);
if (!mode || !file) {
  console.error('usage: workout-check.mjs parse <sheet.txt> | totals <workout.json>');
  process.exit(2);
}

if (mode === 'parse') {
  const { parseWorkoutText } = await import(url('workout-parse.ts'));
  const r = parseWorkoutText(readFileSync(file, 'utf8'), LEVELS.map(([id]) => id));
  console.log(JSON.stringify(r, null, 1));
} else if (mode === 'totals') {
  const { levelMeters } = await import(url('workout-totals.ts'));
  const w = JSON.parse(readFileSync(file, 'utf8'));
  for (const [id, name] of LEVELS) console.log(`${name.padEnd(12)} ${levelMeters(w, id)} m`);
} else {
  console.error(`unknown mode: ${mode}`);
  process.exit(2);
}
