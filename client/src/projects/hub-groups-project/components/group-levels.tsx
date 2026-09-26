import React, { useEffect, useMemo, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevel, HubGroupLevels, HubGroupLevelSwimmer } from '../types';
import { levelColor, rankColor } from './level-color';

/**
 * Уровни пловцов группы — карточка таба `Admin` (docs/plans/lane-plans-plan.md, L1).
 *
 * Две части: список уровней (свой у группы; сервер при первом открытии заводит 4 стандартных)
 * и состав с выпадашкой уровня у каждого пловца. Уровень ставится сразу по выбору — тренер
 * проходит 20 человек подряд, кнопка Save на каждого была бы лишней. Список уровней, наоборот,
 * правится черновиком и сохраняется ЦЕЛИКОМ (`PUT …/levels`): порядок в списке = ранг, чего
 * нет в списке — удаляется, и те, кто на нём стоял, остаются «без уровня» — поэтому удаление
 * занятого уровня идёт через подтверждение.
 */

/** Токен antiforgery: свой кэш на модуль — как у остальных мутирующих клиентов проекта. */
let cachedToken: string | null = null;

async function apiPut<T>(url: string, body: unknown): Promise<{ ok: boolean; data?: T; error?: string }> {
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
    if (r.ok) return { ok: true, data: r.status === 204 ? undefined : await r.json().catch(() => undefined) };
    cachedToken = null;
    const data = await r.json().catch(() => ({}));
    return { ok: false, error: (data as { error?: string }).error };
  } catch {
    cachedToken = null;
    return { ok: false };
  }
}

/** Строка черновика: `id` null — новый уровень; `key` — стабильный ключ React. */
interface LevelDraft {
  key: string;
  id: number | null;
  name: string;
  description: string;
  color: string | null;
}

type Filter = 'all' | 'none' | number;

const inputCls =
  'min-w-0 rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-[6px] text-[12.5px] text-[var(--t-text)]';
const ghostBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40';
const iconBtn =
  'cursor-pointer rounded-[7px] border border-[var(--t-border)] bg-transparent px-2 py-[4px] text-[12px] font-extrabold text-[var(--t-text-2)] hover:bg-[var(--t-surface2)] disabled:cursor-not-allowed disabled:opacity-30';

const MAX_LEVELS = 12;

let draftSeq = 0;
const toDraft = (l: HubGroupLevel): LevelDraft => ({
  key: `l${l.id}`, id: l.id, name: l.name, description: l.description ?? '', color: l.color ?? null,
});

function Dot({ color }: { color: string }) {
  return <span className="inline-block h-[10px] w-[10px] shrink-0 rounded-full" style={{ background: color }} />;
}

function GroupLevelsCard({ groupId }: { groupId: number }) {
  const [data, setData] = useState<HubGroupLevels | null>(null);
  const [loadError, setLoadError] = useState(false);
  const [draft, setDraft] = useState<LevelDraft[] | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmRemoval, setConfirmRemoval] = useState<HubGroupLevel[] | null>(null);
  const [filter, setFilter] = useState<Filter>('all');
  const [pendingSwimmer, setPendingSwimmer] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetch(`/api/me/hub-groups/${groupId}/levels`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : Promise.reject(r.status)))
      .then((d: HubGroupLevels) => { if (!cancelled) setData(d); })
      .catch(() => { if (!cancelled) setLoadError(true); });
    return () => { cancelled = true; };
  }, [groupId]);

  const levelById = useMemo(
    () => new Map((data?.levels ?? []).map((l) => [l.id, l])),
    [data],
  );

  if (loadError) {
    return (
      <div className="deep-card">
        <div className="deep-card-title">Levels</div>
        <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]">Could not load levels.</p>
      </div>
    );
  }
  if (!data) {
    return (
      <div className="deep-card">
        <div className="deep-card-title">Levels</div>
        <p className="m-0 mt-2 text-[12px] text-[var(--t-text-3)]">Loading…</p>
      </div>
    );
  }

  // ── Список уровней ────────────────────────────────────────────────────────

  const startEdit = () => { setError(null); setDraft(data.levels.map(toDraft)); };
  const patch = (key: string, p: Partial<LevelDraft>) =>
    setDraft((d) => d && d.map((x) => (x.key === key ? { ...x, ...p } : x)));
  const move = (index: number, delta: number) => setDraft((d) => {
    if (!d) return d;
    const next = [...d];
    const [item] = next.splice(index, 1);
    next.splice(index + delta, 0, item);
    return next;
  });
  const add = () => setDraft((d) => d && [...d, {
    key: `n${++draftSeq}`, id: null, name: '', description: '', color: null,
  }]);
  const remove = (key: string) => setDraft((d) => d && d.filter((x) => x.key !== key));

  const persist = async () => {
    if (!draft) return { success: false };
    setSaving(true);
    setError(null);
    const result = await apiPut<HubGroupLevels>(`/api/me/hub-groups/${groupId}/levels`, {
      levels: draft.map((d) => ({
        id: d.id, name: d.name, description: d.description || null, color: d.color,
      })),
    });
    setSaving(false);
    if (result.ok && result.data) {
      setData(result.data);
      setDraft(null);
      setConfirmRemoval(null);
      // Фильтр по удалённому уровню больше не к чему применять.
      if (typeof filter === 'number' && !result.data.levels.some((l) => l.id === filter)) setFilter('all');
      return { success: true };
    }
    const message = result.error ?? 'Could not save. Try again.';
    setError(message);
    return { success: false, error: message };
  };

  const save = () => {
    if (!draft) return;
    const kept = new Set(draft.map((d) => d.id));
    const removedBusy = data.levels.filter((l) => !kept.has(l.id) && l.swimmerCount > 0);
    if (removedBusy.length > 0) setConfirmRemoval(removedBusy);
    else void persist();
  };

  // ── Состав ────────────────────────────────────────────────────────────────

  const setSwimmerLevel = async (swimmer: HubGroupLevelSwimmer, levelId: number | null) => {
    const before = data;
    // Сразу показываем выбор; не сохранилось — откатываем с ошибкой.
    setData(applySwimmerLevel(data, swimmer.swimmerId, levelId));
    setPendingSwimmer(swimmer.swimmerId);
    setError(null);
    const result = await apiPut(`/api/me/hub-groups/${groupId}/swimmer-levels/${swimmer.swimmerId}`, { levelId });
    setPendingSwimmer(null);
    if (!result.ok) {
      setData(before);
      setError(result.error ?? 'Could not save the level. Try again.');
    }
  };

  const noLevelCount = data.swimmers.filter((s) => s.levelId == null).length;
  const visibleSwimmers = data.swimmers.filter((s) => (
    filter === 'all' ? true : filter === 'none' ? s.levelId == null : s.levelId === filter
  ));

  const chip = (key: Filter, label: React.ReactNode, count: number, color?: string) => {
    const on = filter === key;
    return (
      <button
        key={String(key)}
        type="button"
        onClick={() => setFilter(key)}
        aria-pressed={on}
        className={`inline-flex cursor-pointer items-center gap-1.5 rounded-full border px-2.5 py-[4px] text-[11.5px] font-extrabold ${
          on
            ? 'border-[var(--t-accent)] bg-[var(--t-accent-soft)] text-[var(--t-accent)]'
            : 'border-[var(--t-border)] bg-transparent text-[var(--t-text-2)]'
        }`}
      >
        {color && <Dot color={color} />}
        {label}
        <span className="hp-mono text-[var(--t-text-3)]">{count}</span>
      </button>
    );
  };

  return (
    <div className="deep-card">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="deep-card-title">Levels</div>
          <div className="deep-card-sub mt-1">private to coaches · used to split swimmers into lanes</div>
        </div>
        {!draft && <button type="button" className={ghostBtn} onClick={startEdit}>Edit levels</button>}
      </div>

      {/* Уровни: просмотр или черновик */}
      {!draft ? (
        <ol className="m-0 mt-3 flex list-none flex-col gap-1.5 p-0">
          {data.levels.map((l) => (
            <li key={l.id} className="flex items-baseline gap-2 text-[12.5px]">
              <span className="hp-mono w-[18px] shrink-0 text-right font-extrabold text-[var(--t-text-3)]">{l.rank}</span>
              <span className="self-center"><Dot color={levelColor(l)} /></span>
              <span className="font-black text-[var(--t-text)]">{l.name}</span>
              {l.description && <span className="min-w-0 truncate text-[var(--t-text-2)]">{l.description}</span>}
              <span className="hp-mono ml-auto shrink-0 text-[11.5px] text-[var(--t-text-3)]">
                {l.swimmerCount} {l.swimmerCount === 1 ? 'swimmer' : 'swimmers'}
              </span>
            </li>
          ))}
        </ol>
      ) : (
        <div className="mt-3 flex flex-col gap-2">
          <p className="m-0 text-[11.5px] text-[var(--t-text-3)]">1 is the strongest. Order sets the rank.</p>
          {draft.map((d, i) => (
            <div key={d.key} className="flex flex-wrap items-center gap-1.5 rounded-[10px] border border-[var(--t-border-2)] p-2">
              <span className="hp-mono w-[18px] text-right text-[12px] font-extrabold text-[var(--t-text-3)]">{i + 1}</span>
              <input
                type="color"
                value={d.color ?? rankColor(i + 1)}
                onChange={(e) => patch(d.key, { color: e.target.value })}
                aria-label={`Color of level ${i + 1}`}
                className="h-[26px] w-[30px] cursor-pointer rounded-[6px] border border-[var(--t-border)] bg-transparent p-0"
              />
              <input
                value={d.name}
                onChange={(e) => patch(d.key, { name: e.target.value })}
                placeholder="Name"
                maxLength={50}
                aria-label={`Name of level ${i + 1}`}
                className={`${inputCls} w-[140px] font-bold`}
              />
              <input
                value={d.description}
                onChange={(e) => patch(d.key, { description: e.target.value })}
                placeholder="Description (optional)"
                maxLength={300}
                aria-label={`Description of level ${i + 1}`}
                className={`${inputCls} flex-1 basis-[160px]`}
              />
              <span className="flex gap-1">
                <button type="button" className={iconBtn} onClick={() => move(i, -1)} disabled={i === 0} aria-label="Move up">↑</button>
                <button type="button" className={iconBtn} onClick={() => move(i, 1)} disabled={i === draft.length - 1} aria-label="Move down">↓</button>
                <button
                  type="button"
                  className={iconBtn}
                  onClick={() => remove(d.key)}
                  disabled={draft.length <= 1}
                  aria-label={`Remove level ${i + 1}`}
                  title={draft.length <= 1 ? 'A group needs at least one level' : 'Remove'}
                >
                  ✕
                </button>
              </span>
            </div>
          ))}
          <div className="flex flex-wrap items-center gap-2">
            <button type="button" className={ghostBtn} onClick={add} disabled={draft.length >= MAX_LEVELS}>+ Add level</button>
            <span className="ml-auto flex gap-2">
              <button type="button" className={ghostBtn} onClick={() => { setDraft(null); setError(null); }} disabled={saving}>
                Cancel
              </button>
              <button
                type="button"
                onClick={save}
                disabled={saving}
                className="cursor-pointer rounded-[9px] border border-[var(--t-accent)] bg-[var(--t-accent)] px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent-ink)] disabled:cursor-not-allowed disabled:opacity-50"
              >
                {saving ? 'Saving…' : 'Save levels'}
              </button>
            </span>
          </div>
        </div>
      )}

      {error && <p className="m-0 mt-2 text-[12px] font-bold text-[var(--t-danger)]" role="alert">{error}</p>}

      {/* Состав с уровнями */}
      <div className="mt-4 border-t border-[var(--t-border-2)] pt-3">
        <div className="text-[12px] font-black uppercase tracking-[0.06em] text-[var(--t-text-2)]">Swimmers</div>
        {data.swimmers.length === 0 ? (
          <p className="m-0 mt-2 text-[12px] text-[var(--t-text-3)]">No swimmers in this group yet.</p>
        ) : (
          <>
            <div className="mt-2 flex flex-wrap gap-1.5">
              {chip('all', 'All', data.swimmers.length)}
              {data.levels.map((l) => chip(l.id, l.name, l.swimmerCount, levelColor(l)))}
              {chip('none', 'No level', noLevelCount)}
            </div>
            <ul className="m-0 mt-2 flex list-none flex-col p-0">
              {visibleSwimmers.map((s) => {
                const level = s.levelId != null ? levelById.get(s.levelId) : undefined;
                return (
                  <li key={s.swimmerId} className="flex flex-wrap items-center gap-2 border-b border-[var(--t-border-2)] py-[7px] last:border-b-0">
                    <Dot color={level ? levelColor(level) : 'var(--t-border)'} />
                    <span className="min-w-0 flex-1">
                      <bdi className="text-[13px] font-bold text-[var(--t-text)]">{s.name || s.nameEn}</bdi>
                      <span className="hp-mono ml-2 text-[11px] text-[var(--t-text-3)]">{s.birthYear}</span>
                    </span>
                    <select
                      value={s.levelId ?? ''}
                      onChange={(e) => setSwimmerLevel(s, e.target.value === '' ? null : Number(e.target.value))}
                      disabled={pendingSwimmer === s.swimmerId || draft != null}
                      aria-label={`Level of ${s.name || s.nameEn}`}
                      title={draft != null ? 'Save or cancel the level list first' : undefined}
                      className={`${inputCls} cursor-pointer disabled:opacity-60`}
                    >
                      <option value="">No level</option>
                      {data.levels.map((l) => (
                        <option key={l.id} value={l.id}>{l.rank} · {l.name}</option>
                      ))}
                    </select>
                  </li>
                );
              })}
              {visibleSwimmers.length === 0 && (
                <li className="py-2 text-[12px] text-[var(--t-text-3)]">Nobody here.</li>
              )}
            </ul>
          </>
        )}
      </div>

      {confirmRemoval && (
        <ConfirmDialog
          title="Remove levels with swimmers?"
          confirmLabel="Remove and save"
          busyLabel="Saving…"
          onConfirm={persist}
          onClose={() => setConfirmRemoval(null)}
        >
          <ul className="m-0 mt-3 flex list-none flex-col gap-1 p-0 text-[13px]">
            {confirmRemoval.map((l) => (
              <li key={l.id} className="flex items-center gap-2">
                <Dot color={levelColor(l)} />
                <span className="font-bold">{l.name}</span>
                <span className="text-[var(--t-text-2)]">
                  — {l.swimmerCount} {l.swimmerCount === 1 ? 'swimmer' : 'swimmers'}
                </span>
              </li>
            ))}
          </ul>
          <p className="m-0 mt-3 text-[12.5px] text-[var(--t-text-2)]">
            They will have no level until you pick a new one.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

/** Уровень пловца + пересчёт счётчиков — локально, до ответа сервера. */
function applySwimmerLevel(data: HubGroupLevels, swimmerId: number, levelId: number | null): HubGroupLevels {
  const swimmers = data.swimmers.map((s) => (s.swimmerId === swimmerId ? { ...s, levelId } : s));
  const counts = new Map<number, number>();
  swimmers.forEach((s) => { if (s.levelId != null) counts.set(s.levelId, (counts.get(s.levelId) ?? 0) + 1); });
  return {
    levels: data.levels.map((l) => ({ ...l, swimmerCount: counts.get(l.id) ?? 0 })),
    swimmers,
  };
}

export default GroupLevelsCard;
