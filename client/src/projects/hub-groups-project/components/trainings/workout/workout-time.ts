/**
 * Время выхода в workout (docs/plans/trainings-coach-flow-plan.md, §5.5–5.6, CF6): разбор и запись
 * «:35» / «35» / «1:40», заполнение по шагу. Чистые функции — тесты в `workout-time.test.mjs`.
 *
 * Время выхода хранится СТРОКОЙ (на листе бывают «2:30-40», «rest :20»): считать по шагу можно
 * только то, что похоже на время; остальное не трогается.
 */

/** Похоже на время выхода: «1:40», «:35», «35» (секунды), «0:50». */
const TIME_RX = /^(?:(\d{1,2})?:([0-5]\d)|(\d{1,3}))$/;

/** Секунды из записи времени; не время — null. «35» — 35 с, «1:40» — 100 с. */
export function parseSendOff(raw: string | null | undefined): number | null {
  const m = TIME_RX.exec((raw ?? '').trim());
  if (!m) return null;
  if (m[3] != null) {
    const sec = Number(m[3]);
    // Голое число — секунды до 3 минут; больше — скорее метры или опечатка.
    return sec > 0 && sec < 180 ? sec : null;
  }
  return Number(m[1] ?? 0) * 60 + Number(m[2]);
}

/** Запись как на листе: меньше минуты — «:50», иначе «1:40». */
export function formatSendOff(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  const min = Math.floor(s / 60);
  const sec = String(s % 60).padStart(2, '0');
  return min === 0 ? `:${sec}` : `${min}:${sec}`;
}

export interface StepFill {
  values: string[];
  /** Значение достроено по шагу (подсветка «проверь»). */
  filled: boolean[];
}

/**
 * Заполнение по шагу (§3 решение 9): введены первые времена подряд (не меньше двух) — пустые после
 * них продолжаются с тем же шагом, что между двумя последними введёнными. «:35 | :40 | :45 | _» →
 * «:50». Первые два не время — ничего не меняется. Уже введённое не трогается.
 */
export function fillByStep(input: ReadonlyArray<string | null | undefined>): StepFill {
  const values = input.map((v) => (v ?? '').trim());
  const filled = values.map(() => false);

  let given = 0;
  while (given < values.length && parseSendOff(values[given]) != null) given += 1;
  if (given < 2) return { values, filled };

  const step = parseSendOff(values[given - 1])! - parseSendOff(values[given - 2])!;
  let last = parseSendOff(values[given - 1])!;
  for (let i = given; i < values.length; i += 1) {
    // Введённое дальше не трогаем; если это время — шаг продолжается от него.
    if (values[i] !== '') {
      const own = parseSendOff(values[i]);
      if (own == null) break;
      last = own;
      continue;
    }
    last += step;
    if (last <= 0) break;
    values[i] = formatSendOff(last);
    filled[i] = true;
  }
  return { values, filled };
}
