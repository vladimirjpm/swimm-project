import React, { useState } from 'react';
import type { GroupTrainingSchedule, GroupTrainingSlot } from '../types';
import { DAY_SHORT } from './group-training-slots';

/**
 * Редактор регулярного расписания группы — карточка таба `Admin` (слоты шапки, 09.09.2026).
 *
 * Форма маленькая и всегда предзаполнена текущим состоянием, поэтому шлём её ЦЕЛИКОМ
 * (`PUT /api/me/hub-groups/{id}/training-schedule` — полная замена, как у настроек
 * отображения). Пустой список слотов = убрать расписание, слоты шапки тогда скрываются.
 *
 * Дни недели — ISO (1 = Mon … 7 = Sun), как в JSON и на сервере; порядок кнопок начинается
 * с воскресенья, потому что неделя в Израиле начинается с него.
 */

/** Токен antiforgery: свой кэш на модуль — как у остальных мутирующих клиентов проекта. */
let cachedToken: string | null = null;

async function apiPut(url: string, body: unknown): Promise<{ ok: boolean; error?: string }> {
  if (!cachedToken) {
    try {
      const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
      cachedToken = r.ok ? (await r.json()).token ?? null : null;
    } catch {
      cachedToken = null;
    }
  }
  if (!cachedToken) return { ok: false };
  try {
    const r = await fetch(url, {
      method: 'PUT',
      credentials: 'include',
      headers: { 'Content-Type': 'application/json', 'X-XSRF-TOKEN': cachedToken },
      body: JSON.stringify(body),
    });
    if (r.ok) return { ok: true };
    cachedToken = null;
    const data = await r.json().catch(() => ({}));
    return { ok: false, error: (data as { error?: string }).error };
  } catch {
    cachedToken = null;
    return { ok: false };
  }
}

/** Порядок кнопок дней: неделя в Израиле начинается с воскресенья. */
const DAY_ORDER = [7, 1, 2, 3, 4, 5, 6];

const inputCls =
  'rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-[6px] text-[12.5px] text-[var(--t-text)]';

function GroupScheduleEditor({
  groupId, schedule,
}: {
  groupId: number;
  schedule?: GroupTrainingSchedule | null;
}) {
  const [slots, setSlots] = useState<GroupTrainingSlot[]>(schedule?.slots ?? []);
  const [place, setPlace] = useState(schedule?.place ?? '');
  const [poolType, setPoolType] = useState(schedule?.pool_type ?? '');
  const [note, setNote] = useState(schedule?.note ?? '');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const dayOn = (day: number) => slots.some((s) => s.day === day);

  // Клик по дню: включить — со временем предыдущего слота (обычно оно одно на всю неделю),
  // выключить — убрать все занятия этого дня.
  const toggleDay = (day: number) => {
    setSaved(false);
    setSlots((prev) => {
      if (prev.some((s) => s.day === day)) return prev.filter((s) => s.day !== day);
      const sample = prev[0];
      return [...prev, { day, start: sample?.start ?? '18:00', end: sample?.end ?? null }]
        .sort((a, b) => a.day - b.day);
    });
  };

  const setTime = (day: number, field: 'start' | 'end', value: string) => {
    setSaved(false);
    setSlots((prev) => prev.map((s) => (s.day === day
      ? { ...s, [field]: field === 'end' && value === '' ? null : value }
      : s)));
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    const result = await apiPut(`/api/me/hub-groups/${groupId}/training-schedule`, {
      // Ключи snake_case — как их объявляет серверный DTO (JsonPropertyName).
      // camelCase здесь молча терялся бы: биндер не нашёл бы поле и оставил null.
      slots, place, pool_type: poolType, note,
    });
    setSaving(false);
    if (result.ok) {
      setSaved(true);
      // Расписание живёт в шапке страницы, а шапка пришла прошлым ответом — честный способ
      // показать новое состояние целиком это перечитать страницу (как у настроек отображения).
      window.location.reload();
    } else {
      setError(result.error ?? 'Could not save. Try again.');
    }
  };

  return (
    <div className="deep-card">
      <div className="deep-card-title">Training schedule</div>
      <div className="deep-card-sub mt-1">shown in the page header · “Next training” is computed from it</div>

      <div className="mt-3 flex flex-wrap gap-1.5">
        {DAY_ORDER.map((day) => (
          <button
            key={day}
            type="button"
            onClick={() => toggleDay(day)}
            aria-pressed={dayOn(day)}
            className={`hp-mono cursor-pointer rounded-[9px] border px-3 py-[7px] text-[12px] font-extrabold ${
              dayOn(day)
                ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)] text-[var(--t-accent)]'
                : 'border-[var(--t-border)] bg-transparent text-[var(--t-text-3)]'
            }`}
          >
            {DAY_SHORT[day]}
          </button>
        ))}
      </div>

      {slots.length > 0 && (
        <div className="mt-3 flex flex-col gap-2">
          {slots.map((s) => (
            <div key={s.day} className="flex flex-wrap items-center gap-2">
              <span className="hp-mono w-[42px] text-[12px] font-extrabold text-[var(--t-text-2)]">
                {DAY_SHORT[s.day]}
              </span>
              <input
                type="time"
                value={s.start}
                onChange={(e) => setTime(s.day, 'start', e.target.value)}
                className={inputCls}
              />
              <span className="text-[12px] text-[var(--t-text-3)]">–</span>
              <input
                type="time"
                value={s.end ?? ''}
                onChange={(e) => setTime(s.day, 'end', e.target.value)}
                className={inputCls}
                title="End time is optional"
              />
            </div>
          ))}
        </div>
      )}

      <div className="mt-3 flex flex-col gap-2">
        <input
          value={place}
          onChange={(e) => { setPlace(e.target.value); setSaved(false); }}
          placeholder="Where (pool name)"
          className={inputCls}
        />
        <input
          value={poolType}
          onChange={(e) => { setPoolType(e.target.value); setSaved(false); }}
          placeholder="Pool (25m / 50m)"
          className={`${inputCls} max-w-[160px]`}
        />
        <input
          value={note}
          onChange={(e) => { setNote(e.target.value); setSaved(false); }}
          placeholder="Note — e.g. “summer schedule in WhatsApp”"
          className={inputCls}
        />
      </div>

      {error && <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]">{error}</p>}
      {saved && !error && <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-accent)]">Saved</p>}

      <div className="mt-3 flex items-center gap-2">
        <button
          type="button"
          onClick={save}
          disabled={saving}
          className="hp-mono cursor-pointer rounded-[9px] border-none bg-[var(--t-accent)] px-4 py-[8px] text-[12px] font-extrabold text-[var(--t-accent-ink)] disabled:opacity-50"
        >
          {saving ? 'Saving…' : 'Save'}
        </button>
        <span className="text-[11px] text-[var(--t-text-3)]">
          No days selected = no schedule; the header slots disappear.
        </span>
      </div>
    </div>
  );
}

export default GroupScheduleEditor;
