/**
 * Фикстуры стенда листа печати workout (Ш3, docs/plans/workout-print-plan.md §8.2): настоящие листы
 * тренера из архива `docs/workouts/sheets/` (копия — витрина не читает файлы вне `client/`) и
 * искусственные длинные. Уровень везде Intermediate (id 37).
 */
import type { Workout, WorkoutLine, WorkoutSet } from '../../projects/hub-groups-project/types';

export const FIXTURE_LEVEL_ID = 37;
export const FIXTURE_LEVEL = { rank: 2, name: 'Intermediate' };
export const FIXTURE_GROUP = 'דולפין נתניה מאסטרס';

/** `docs/workouts/sheets/2026-11-22-2000.json` (короткий: 3 сета, 9 строк), ячейки только Intermediate. */
export const SHEET_2211: Workout = {
  note: null,
  sets: [
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":4,"distance":100,"text":"1-חת' 1-(25 סגנון + 75 חת') 1-(50 סגנון + 50 חת') 1-(75 סגנון + 25 חת')","intensity":null,"per_level":{"37":{"send_off":"rest :10","reps":null}}},{"reps":6,"distance":50,"text":"חתירה 1-(25 כלב+25 מלא) 1-(25 מסרק+25 מלא) 1-(25 יד' ישרות+25 מלא)","intensity":null,"per_level":{"37":{"send_off":"rest :10","reps":null}}},{"reps":8,"distance":25,"text":"חתירה הגברה 1-4, 4V-1V, לשמור על כמות תנועות","intensity":"1-4V","per_level":{"37":{"send_off":":30","reps":null}}}]},
    {"title":"זומרס","repeat":1,"level_ids":null,"lines":[{"reps":4,"distance":100,"text":"מעורב 100 כל סגנון (מי ששוחה 3 בלי פרפר)","intensity":null,"per_level":{"37":{"send_off":"rest :15","reps":null}}},{"reps":6,"distance":50,"text":"הלוך יד אחת חזור יד שניה. פרפר\\גב\\חתירה (מי ששוחה 4 רק גב\\חת')","intensity":null,"per_level":{"37":{"send_off":null,"reps":null}}},{"reps":8,"distance":25,"text":"רגליים הלוך 2V חזור 4V","intensity":null,"per_level":{"37":{"send_off":":30","reps":null}}}]},
    {"title":"כפות","repeat":1,"level_ids":null,"lines":[{"reps":4,"distance":100,"text":"חתירה","intensity":"2V","per_level":{"37":{"send_off":"1:45","reps":null}}},{"reps":6,"distance":50,"text":"גב","intensity":"3V","per_level":{"37":{"send_off":"1:10","reps":null}}},{"reps":8,"distance":25,"text":"1-פרפר או חזה 4V, 1-חתירה 2V","intensity":null,"per_level":{"37":{"send_off":":35","reps":null}}}]},
  ],
};

/** `docs/workouts/sheets/2026-11-24-2000.json` (длинный: 7 сетов, 17 строк; главный по листу — ④ «מרכזי»), ячейки только Intermediate. */
export const SHEET_2411: Workout = {
  note: null,
  sets: [
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":200,"text":"חימום חופשי","intensity":null,"per_level":null}]},
    {"title":"זומרס","repeat":4,"level_ids":null,"lines":[{"reps":2,"distance":25,"text":"חתירה בלי נשימה","intensity":null,"per_level":null},{"reps":1,"distance":50,"text":"רגליים","intensity":null,"per_level":null},{"reps":1,"distance":50,"text":"מעורב","intensity":null,"per_level":null}]},
    {"title":"כפות","repeat":1,"level_ids":null,"lines":[{"reps":4,"distance":50,"text":"(25 תחושת מים + 25 מלא) אפשר שנורקל","intensity":null,"per_level":{"37":{"send_off":"rest :10","reps":null}}},{"reps":1,"distance":400,"text":"(75 חתירה + 25 חזה)","intensity":null,"per_level":null}]},
    {"title":"מרכזי","repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":100,"text":"","intensity":"1V","per_level":{"37":{"send_off":"rest :20","reps":null}}},{"reps":2,"distance":100,"text":"","intensity":"3V","per_level":{"37":{"send_off":"rest :30","reps":null}}},{"reps":2,"distance":50,"text":"","intensity":"1V","per_level":{"37":{"send_off":"rest :20","reps":null}}}]},
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":100,"text":"","intensity":"2V","per_level":{"37":{"send_off":"rest :20","reps":null}}},{"reps":2,"distance":50,"text":"","intensity":"4V","per_level":{"37":{"send_off":"rest :30","reps":null}}},{"reps":2,"distance":50,"text":"","intensity":"1V","per_level":{"37":{"send_off":"rest :20","reps":null}}}]},
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":100,"text":"","intensity":"3V","per_level":{"37":{"send_off":"rest :30","reps":null}}},{"reps":2,"distance":25,"text":"","intensity":"5V","per_level":{"37":{"send_off":"rest :30","reps":null}}},{"reps":2,"distance":50,"text":"","intensity":"1V","per_level":{"37":{"send_off":"rest :20","reps":null}}}]},
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":50,"text":"קל","intensity":null,"per_level":null},{"reps":1,"distance":null,"text":"זינוקיםכיפרים","intensity":null,"per_level":null}]},
  ],
};

/** `docs/workouts/sheets/2026-11-26-1945.json` (средний: 5 сетов, 11 строк; главный по листу — ④ «מרכזי · סט שני עם זומרס»), ячейки только Intermediate. */
export const SHEET_2611: Workout = {
  note: null,
  sets: [
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":3,"distance":100,"text":"50 חתירה + 50 גב","intensity":null,"per_level":{"37":{"send_off":"rest :10","reps":null}}}]},
    {"title":"שנורקל","repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":50,"text":"כלב קטן","intensity":null,"per_level":{"37":{"send_off":"rest :10","reps":null}}},{"reps":1,"distance":100,"text":"כלב גדול","intensity":null,"per_level":{"37":{"send_off":"rest :15","reps":null}}},{"reps":1,"distance":150,"text":"ידיים רפויות","intensity":null,"per_level":{"37":{"send_off":"rest :20","reps":null}}},{"reps":1,"distance":200,"text":"חתירה ארוך","intensity":null,"per_level":null}]},
    {"title":"כפות","repeat":1,"level_ids":null,"lines":[{"reps":8,"distance":50,"text":"חתירה: 4 כפות אמות, 4 כפות רגיל, הכל 2V","intensity":"2V","per_level":{"37":{"send_off":"rest :20","reps":null}}}]},
    {"title":"מרכזי · סט שני עם זומרס","repeat":2,"level_ids":null,"lines":[{"reps":1,"distance":200,"text":"חתירה","intensity":"2V","per_level":null},{"reps":2,"distance":100,"text":"חתירה 1-3V, 1-1V","intensity":null,"per_level":{"37":{"send_off":"rest :20","reps":null}}},{"reps":2,"distance":50,"text":"1-4V, 1-1V","intensity":null,"per_level":{"37":{"send_off":"rest :30","reps":null}}},{"reps":2,"distance":25,"text":"1 חזק ללא נשימה, 1 קל על הגב","intensity":null,"per_level":{"37":{"send_off":"rest :15","reps":null}}}]},
    {"title":null,"repeat":1,"level_ids":null,"lines":[{"reps":1,"distance":200,"text":"שחרור","intensity":null,"per_level":null}]},
  ],
};

/** Копия листа с отметкой «главный» у сетов `nos` (номера с 1). */
export function withMain(workout: Workout, nos: number[]): Workout {
  return { ...workout, sets: workout.sets.map((s, i) => ({ ...s, main: nos.includes(i + 1) || undefined })) };
}

const line = (reps: number, distance: number | null, text: string, sendOff: string | null, intensity: string | null = null): WorkoutLine => ({
  reps, distance, text, intensity, per_level: sendOff ? { '37': { send_off: sendOff, reps: null } } : null,
});

const HE_TEXTS = ['חתירה', 'גב', 'חזה', 'פרפר', 'מעורב', 'רגליים עם קרש', 'ידיים עם מצוף', 'חתירה ארוך', 'כלב קטן', 'סנפירים'];
const SEND_OFFS = ['1:45', 'rest :15', '2:10', 'rest :20', '1:30', '3× 1:50', 'rest :30', '0:55'];

/** Искусственный лист: `sets` сетов по `perSet` строк, ивритский; главный — `mainNo` (0 — нет). */
function synthetic(sets: number, perSet: number, mainNo = 0): Workout {
  const out: WorkoutSet[] = [];
  let n = 0;
  for (let s = 1; s <= sets; s += 1) {
    const lines: WorkoutLine[] = [];
    for (let i = 0; i < perSet; i += 1, n += 1) {
      const intensity = n % 3 === 0 ? 'V2' : n % 5 === 0 ? '1-3V' : null;
      lines.push(line(1 + (n % 6), [50, 100, 25, 200, 75][n % 5], HE_TEXTS[n % HE_TEXTS.length], SEND_OFFS[n % SEND_OFFS.length], intensity));
    }
    out.push({ title: s % 2 === 0 ? `סט ${s}` : null, repeat: s === mainNo ? 2 : 1, level_ids: null, lines, main: s === mainNo || undefined });
  }
  return { note: null, sets: out };
}

/** Длинный: 8 сетов × 5 = 40 строк, главный ⑤. */
export const LONG_40 = synthetic(8, 5, 5);
/** Огромный: 16 сетов × 14 = 224 строки — не влезает и на полу (предупреждение, В13). */
export const HUGE_224 = synthetic(16, 14);

/** Английский лист: змейка кубов от верхнего левого угла. */
export const ENGLISH: Workout = {
  note: 'Easy pace on the warm-up, hold the times on the main set.',
  sets: [
    { title: 'Warm-up', repeat: 1, level_ids: null, lines: [line(1, 400, 'free easy', null), line(4, 50, 'kick', 'rest :15', 'V1')] },
    { title: 'Drill', repeat: 1, level_ids: null, lines: [line(8, 50, 'catch-up / fist', 'rest :10')] },
    { title: 'Pull', repeat: 1, level_ids: null, lines: [line(4, 100, 'pull buoy, paddles', '1:40', 'V2')] },
    { title: 'Main', repeat: 2, level_ids: null, lines: [line(4, 100, 'free', '1:35', '2-3V'), line(4, 50, 'back', '0:55', 'V3')] },
    { title: 'Cool-down', repeat: 1, level_ids: null, lines: [line(1, 200, 'easy choice', null)] },
  ],
};
