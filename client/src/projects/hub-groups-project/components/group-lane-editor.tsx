import React, { useMemo, useState } from 'react';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevel, HubGroupLevels, LanePlan, LanePlanInput, LanePlanSwimmer } from '../types';
import { SwimmerName } from './group-lane-board';
import { lanePlansApi } from './lane-plans-api';
import { levelColor } from './level-color';

/**
 * Редактор плана дорожек — тренер (docs/plans/lane-plans-plan.md, L3).
 *
 * Три корзины без флагов, как в модели: дорожка N, «Unassigned» (пришёл, не разложен) и
 * «Not today» (снят). Перенос — перетаскиванием на компьютере и выпадашкой у каждого
 * пловца (на телефоне drag неудобен). Порядок внутри дорожки = кто ведёт; ↑ поднимает.
 * «Distribute» раскладывает пришедших по уровням дорожек на сервере и НЕ сохраняет —
 * сохраняет Save. «Auto lanes» — шаг раньше: тренер задал только число дорожек, сервер сам
 * делит их между уровнями по числу пришедших (соседние уровни при нехватке дорожек — вместе) и
 * раскладывает людей; задания дорожек остаются на своих номерах. Дорожки сверх числа не выбрасываются из черновика: уменьшил и вернул —
 * задания и люди на месте; пока дорожка убрана, её люди показываются и сохраняются как
 * Unassigned (считается на лету, а не переносом — иначе два быстрых «−» теряли шаг).
 *
 * «Copy from previous» (L5) — тот же редактор с заготовкой `template`: прошлый план целиком
 * (дорожки, уровни, задания, заметка, расстановка), но только нынешний состав — ушедшие из
 * группы выпадают (в новый план сервер их и не примет), новенькие встают в Unassigned.
 * Ничего не пишется, пока тренер не нажмёт Save.
 */

const MAX_LANES = 12;

type Target = number | 'unassigned' | 'out';

interface LaneDraft {
  levelId: number | null;
  workout: string;
}

interface Buckets {
  /** Индекс — номер дорожки − 1, длина MAX_LANES. */
  lanes: number[][];
  unassigned: number[];
  out: number[];
}

const inputCls =
  'min-w-0 rounded-[9px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-2.5 py-[6px] text-[12.5px] text-[var(--t-text)]';
const ghostBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40';
const primaryBtn =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent)] bg-[var(--t-accent)] px-3 py-[6px] text-[12px] font-extrabold text-[var(--t-accent-ink)] disabled:cursor-not-allowed disabled:opacity-50';
const iconBtn =
  'cursor-pointer rounded-[7px] border border-[var(--t-border)] bg-transparent px-1.5 py-[2px] text-[11px] font-extrabold text-[var(--t-text-2)] hover:bg-[var(--t-surface2)] disabled:cursor-not-allowed disabled:opacity-30';

const emptyLanes = (): LaneDraft[] => Array.from({ length: MAX_LANES }, () => ({ levelId: null, workout: '' }));

/**
 * Черновик: из сохранённого плана, из заготовки (копия прошлого) или пустой (весь состав — в
 * Unassigned). Не разложенный никуда из состава: в своём плане — «Not today» (пришёл в группу
 * после сохранения), в копии — Unassigned (новенький, тренер должен его заметить).
 */
function initialDraft(
  plan: LanePlan | null, template: LanePlan | null, levels: HubGroupLevels, laneCountHint: number,
) {
  const lanes = emptyLanes();
  const buckets: Buckets = { lanes: Array.from({ length: MAX_LANES }, () => []), unassigned: [], out: [] };
  const info = new Map<number, LanePlanSwimmer>();

  // Текущий уровень — из карточки уровней (свежее снимка).
  const levelOf = new Map(levels.swimmers.map((s) => [s.swimmerId, s.levelId]));
  const remember = (s: LanePlanSwimmer) =>
    info.set(s.swimmer_id, { ...s, level_id: levelOf.has(s.swimmer_id) ? levelOf.get(s.swimmer_id) : s.level_id });

  levels.swimmers.forEach((s) => remember({
    swimmer_id: s.swimmerId, name: s.name, name_en: s.nameEn, birth_year: s.birthYear, level_id: s.levelId,
  }));

  const source = plan ?? template;
  const roster = new Set(levels.swimmers.map((s) => s.swimmerId));
  // В копию — только нынешний состав: ушедшего в новый план сервер не примет.
  const keep = (s: LanePlanSwimmer) => plan != null || roster.has(s.swimmer_id);

  if (source) {
    source.lanes.forEach((l) => {
      lanes[l.lane_no - 1] = { levelId: l.level?.id ?? null, workout: l.workout ?? '' };
      l.swimmers.filter(keep).forEach((s) => { remember(s); buckets.lanes[l.lane_no - 1].push(s.swimmer_id); });
    });
    source.unassigned.filter(keep).forEach((s) => { remember(s); buckets.unassigned.push(s.swimmer_id); });
    source.not_today.filter(keep).forEach((s) => { remember(s); buckets.out.push(s.swimmer_id); });
    const placed = new Set([...buckets.lanes.flat(), ...buckets.unassigned, ...buckets.out]);
    const rest = levels.swimmers.map((s) => s.swimmerId).filter((id) => !placed.has(id));
    if (plan) buckets.out.push(...rest);
    else buckets.unassigned.push(...rest);
  } else {
    buckets.unassigned = levels.swimmers.map((s) => s.swimmerId);
  }

  return {
    laneCount: source?.lane_count ?? laneCountHint,
    note: source?.note ?? '',
    lanes,
    buckets,
    info,
  };
}

function GroupLaneEditor({
  groupId, date, plan, template = null, levels, laneCountHint, onSaved, onCancel,
}: {
  groupId: number;
  date: string;
  /** null — новый план. */
  plan: LanePlan | null;
  /** Заготовка нового плана — копия прошлого («Copy from previous»). */
  template?: LanePlan | null;
  levels: HubGroupLevels;
  /** Число дорожек нового плана (как в прошлом плане). */
  laneCountHint: number;
  onSaved: (plan: LanePlan) => void;
  onCancel: () => void;
}) {
  const init = useMemo(
    () => initialDraft(plan, template, levels, laneCountHint),
    [plan, template, levels, laneCountHint],
  );
  const [laneCount, setLaneCount] = useState(init.laneCount);
  const [note, setNote] = useState(init.note);
  const [lanes, setLanes] = useState<LaneDraft[]>(init.lanes);
  const [buckets, setBuckets] = useState<Buckets>(init.buckets);
  const [saving, setSaving] = useState(false);
  const [distributing, setDistributing] = useState(false);
  /** Что подтверждаем: повторный Distribute или Auto lanes поверх уже заданного. */
  const [confirm, setConfirm] = useState<'distribute' | 'auto' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState<Target | null>(null);

  const info = init.info;
  const levelById = useMemo(() => new Map(levels.levels.map((l) => [l.id, l])), [levels]);
  const laneNos = Array.from({ length: laneCount }, (_, i) => i + 1);

  const dotColor = (swimmerId: number) => {
    const levelId = info.get(swimmerId)?.level_id;
    const level = levelId != null ? levelById.get(levelId) : undefined;
    return level ? levelColor(level) : 'var(--t-border)';
  };

  // ── Перенос ───────────────────────────────────────────────────────────────

  const move = (swimmerId: number, target: Target) => setBuckets((b) => {
    const strip = (list: number[]) => list.filter((id) => id !== swimmerId);
    const next: Buckets = { lanes: b.lanes.map(strip), unassigned: strip(b.unassigned), out: strip(b.out) };
    if (target === 'unassigned') next.unassigned.push(swimmerId);
    else if (target === 'out') next.out.push(swimmerId);
    else next.lanes[target - 1].push(swimmerId);
    return next;
  });

  const raise = (laneNo: number, index: number) => setBuckets((b) => {
    const lane = [...b.lanes[laneNo - 1]];
    [lane[index - 1], lane[index]] = [lane[index], lane[index - 1]];
    const lanesNext = [...b.lanes];
    lanesNext[laneNo - 1] = lane;
    return { ...b, lanes: lanesNext };
  });

  const changeLaneCount = (delta: number) =>
    setLaneCount((c) => Math.min(MAX_LANES, Math.max(1, c + delta)));

  /** Unassigned на экране и в сохранении = своя корзина + люди убранных дорожек. */
  const unassigned = [...buckets.unassigned, ...buckets.lanes.slice(laneCount).flat()];

  const patchLane = (laneNo: number, patch: Partial<LaneDraft>) =>
    setLanes((ls) => ls.map((l, i) => (i === laneNo - 1 ? { ...l, ...patch } : l)));

  // ── Distribute / Save ─────────────────────────────────────────────────────

  const lanesInput = () => laneNos.map((no) => ({
    lane_no: no,
    level_id: lanes[no - 1].levelId,
    workout: lanes[no - 1].workout.trim() || null,
  }));

  const present = () => [...buckets.lanes.slice(0, laneCount).flat(), ...unassigned];

  const distribute = async () => {
    setDistributing(true);
    setError(null);
    const result = await lanePlansApi.distribute(groupId, {
      lane_count: laneCount, lanes: lanesInput(), swimmer_ids: present(),
    });
    setDistributing(false);
    setConfirm(null);
    if (!result.ok || !result.data) {
      setError(result.error ?? 'Could not distribute. Try again.');
      return;
    }
    place(result.data.swimmers);
  };

  /** Раскладка с сервера → корзины; «Not today» не трогаем. */
  const place = (swimmers: LanePlanInput['swimmers']) => {
    const next: Buckets = { lanes: Array.from({ length: MAX_LANES }, () => []), unassigned: [], out: buckets.out };
    swimmers.forEach((s) => {
      if (s.lane_no == null) next.unassigned.push(s.swimmer_id);
      else next.lanes[s.lane_no - 1].push(s.swimmer_id);
    });
    setBuckets(next);
  };

  const autoLanes = async () => {
    setDistributing(true);
    setError(null);
    const result = await lanePlansApi.autoLanes(groupId, { lane_count: laneCount, swimmer_ids: present() });
    setDistributing(false);
    setConfirm(null);
    if (!result.ok || !result.data) {
      setError(result.error ?? 'Could not set up the lanes. Try again.');
      return;
    }
    const levelOf = new Map(result.data.lanes.map((l) => [l.lane_no, l.level_id]));
    // Уровни — на все видимые дорожки (лишним — «без уровня»), задания остаются на своих номерах.
    setLanes((ls) => ls.map((l, i) => (i < laneCount ? { ...l, levelId: levelOf.get(i + 1) ?? null } : l)));
    place(result.data.swimmers);
  };

  const askAutoLanes = () => {
    // Уже заданы уровни дорожек или кто-то разложен — предупреждаем, что это заменится.
    const touched = laneNos.some((no) => lanes[no - 1].levelId != null || buckets.lanes[no - 1].length > 0);
    if (touched) setConfirm('auto');
    else void autoLanes();
  };

  const askDistribute = () => {
    if (!laneNos.some((no) => lanes[no - 1].levelId != null)) {
      setError('Pick a level for at least one lane first — Distribute places swimmers by level.');
      return;
    }
    // Уже кто-то разложен — предупреждаем, что ручные переносы пропадут.
    if (buckets.lanes.slice(0, laneCount).some((l) => l.length > 0)) setConfirm('distribute');
    else void distribute();
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    const input: LanePlanInput = {
      lane_count: laneCount,
      note: note.trim() || null,
      lanes: lanesInput(),
      swimmers: [
        ...laneNos.flatMap((no) => buckets.lanes[no - 1].map((id) => ({ swimmer_id: id, lane_no: no }))),
        ...unassigned.map((id) => ({ swimmer_id: id, lane_no: null })),
      ],
    };
    const result = await lanePlansApi.save(groupId, date, input);
    setSaving(false);
    if (result.ok && result.data) onSaved(result.data);
    else setError(result.error ?? 'Could not save. Try again.');
  };

  // ── Вёрстка ───────────────────────────────────────────────────────────────

  const dropProps = (target: Target) => ({
    onDragOver: (e: React.DragEvent) => { e.preventDefault(); setDragOver(target); },
    onDragLeave: () => setDragOver((t) => (t === target ? null : t)),
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(null);
      const id = Number(e.dataTransfer.getData('text/plain'));
      if (id) move(id, target);
    },
  });
  const dropCls = (target: Target) => (dragOver === target ? 'outline outline-2 outline-[var(--t-accent)]' : '');

  const where = (swimmerId: number): Target => {
    const lane = buckets.lanes.findIndex((l) => l.includes(swimmerId));
    if (lane >= 0) return lane < laneCount ? lane + 1 : 'unassigned';
    return buckets.unassigned.includes(swimmerId) ? 'unassigned' : 'out';
  };

  const swimmerRow = (swimmerId: number, lane?: { no: number; index: number }) => {
    const s = info.get(swimmerId);
    if (!s) return null;
    const current = where(swimmerId);
    return (
      <li
        key={swimmerId}
        draggable
        onDragStart={(e) => { e.dataTransfer.setData('text/plain', String(swimmerId)); e.dataTransfer.effectAllowed = 'move'; }}
        className="flex cursor-grab items-center gap-1.5 rounded-[8px] border border-[var(--t-border-2)] bg-[var(--t-card)] px-2 py-[5px] active:cursor-grabbing"
      >
        <span className="inline-block h-[9px] w-[9px] shrink-0 rounded-full" style={{ background: dotColor(swimmerId) }} />
        <span className={`min-w-0 flex-1 truncate text-[12.5px] font-bold ${s.left_group ? 'text-[var(--t-text-3)]' : 'text-[var(--t-text)]'}`}>
          <SwimmerName swimmer={s} />
        </span>
        {lane && lane.index > 0 && (
          <button type="button" className={iconBtn} onClick={() => raise(lane.no, lane.index)} aria-label="Move up in the lane" title="Move up — the first swimmer leads">↑</button>
        )}
        <select
          value={String(current)}
          onChange={(e) => {
            const v = e.target.value;
            move(swimmerId, v === 'unassigned' || v === 'out' ? v : Number(v));
          }}
          aria-label={`Where ${s.name || s.name_en} swims`}
          className="max-w-[108px] shrink-0 cursor-pointer rounded-[7px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-1.5 py-[3px] text-[11.5px] text-[var(--t-text)]"
        >
          {laneNos.map((no) => <option key={no} value={no}>Lane {no}</option>)}
          <option value="unassigned">Unassigned</option>
          <option value="out">Not today</option>
        </select>
      </li>
    );
  };

  const presentCount = present().length;

  return (
    <div>
      {/* Число дорожек и заметка */}
      <div className="flex flex-wrap items-end gap-3">
        <div>
          <div className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-3)]">Lanes</div>
          <div className="mt-1 flex items-center gap-1">
            <button type="button" className={iconBtn} onClick={() => changeLaneCount(-1)} disabled={laneCount <= 1} aria-label="Fewer lanes">−</button>
            <span className="hp-mono w-[28px] text-center text-[16px] font-black text-[var(--t-text)]">{laneCount}</span>
            <button type="button" className={iconBtn} onClick={() => changeLaneCount(1)} disabled={laneCount >= MAX_LANES} aria-label="More lanes">+</button>
          </div>
        </div>
        <label className="flex min-w-[200px] flex-1 flex-col">
          <span className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-3)]">Note for everyone</span>
          <input
            value={note}
            onChange={(e) => setNote(e.target.value)}
            maxLength={500}
            placeholder="e.g. Warm-up together 400 easy"
            className={`${inputCls} mt-1`}
          />
        </label>
        <span className="flex flex-wrap gap-2">
          <button
            type="button"
            className={ghostBtn}
            onClick={askAutoLanes}
            disabled={distributing || presentCount === 0}
            title="Split the lanes between levels by how many are coming, then place everyone"
          >
            Auto lanes
          </button>
          <button type="button" className={ghostBtn} onClick={askDistribute} disabled={distributing || presentCount === 0}>
            {distributing ? 'Working…' : 'Distribute by level'}
          </button>
        </span>
      </div>

      {/* Дорожки */}
      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2 min-[1200px]:grid-cols-3">
        {laneNos.map((no) => {
          const lane = lanes[no - 1];
          const level = lane.levelId != null ? levelById.get(lane.levelId) : undefined;
          const prev = no > 1 ? lanes[no - 2] : null;
          return (
            <section
              key={no}
              {...dropProps(no)}
              className={`flex min-w-0 flex-col gap-2 rounded-[12px] border border-[var(--t-border)] bg-[var(--t-nested)] p-3 ${dropCls(no)}`}
              style={{ borderLeft: `4px solid ${level ? levelColor(level) : 'var(--t-border)'}` }}
              aria-label={`Lane ${no}`}
            >
              <header className="flex items-center gap-2">
                <span className="hp-mono text-[20px] font-black leading-none text-[var(--t-text)]">{no}</span>
                <select
                  value={lane.levelId ?? ''}
                  onChange={(e) => patchLane(no, { levelId: e.target.value === '' ? null : Number(e.target.value) })}
                  aria-label={`Level of lane ${no}`}
                  className={`${inputCls} ml-auto cursor-pointer`}
                >
                  <option value="">No level</option>
                  {levels.levels.map((l: HubGroupLevel) => <option key={l.id} value={l.id}>{l.rank} · {l.name}</option>)}
                </select>
              </header>
              <textarea
                value={lane.workout}
                onChange={(e) => patchLane(no, { workout: e.target.value })}
                rows={3}
                maxLength={4000}
                placeholder="Workout, e.g. 8×100 free @1:40"
                aria-label={`Workout of lane ${no}`}
                className={`${inputCls} resize-y`}
              />
              {/* Копия задания — только соседу того же уровня (дорожки 2–3 одного уровня). */}
              {prev && prev.levelId === lane.levelId && prev.workout.trim() !== '' && prev.workout !== lane.workout && (
                <button type="button" className="self-start cursor-pointer border-0 bg-transparent p-0 text-[11.5px] font-extrabold text-[var(--t-accent)] underline" onClick={() => patchLane(no, { workout: prev.workout })}>
                  Same workout as lane {no - 1}
                </button>
              )}
              <ul className="m-0 flex min-h-[36px] list-none flex-col gap-1 p-0">
                {buckets.lanes[no - 1].map((id, index) => swimmerRow(id, { no, index }))}
                {buckets.lanes[no - 1].length === 0 && (
                  <li className="py-1 text-[11.5px] text-[var(--t-text-3)]">Drop swimmers here</li>
                )}
              </ul>
            </section>
          );
        })}
      </div>

      {/* Корзины */}
      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
        <section {...dropProps('unassigned')} className={`rounded-[12px] border border-dashed border-[var(--t-border)] p-3 ${dropCls('unassigned')}`}>
          <div className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-2)]">
            Unassigned <span className="hp-mono text-[var(--t-text-3)]">{unassigned.length}</span>
          </div>
          <div className="text-[11px] text-[var(--t-text-3)]">coming today, no lane yet</div>
          <ul className="m-0 mt-2 flex min-h-[36px] list-none flex-col gap-1 p-0">
            {unassigned.map((id) => swimmerRow(id))}
          </ul>
        </section>
        <section {...dropProps('out')} className={`rounded-[12px] border border-dashed border-[var(--t-border)] p-3 ${dropCls('out')}`}>
          <div className="text-[11px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-2)]">
            Not today <span className="hp-mono text-[var(--t-text-3)]">{buckets.out.length}</span>
          </div>
          <div className="text-[11px] text-[var(--t-text-3)]">not in this plan · members don’t see this list</div>
          <ul className="m-0 mt-2 flex min-h-[36px] list-none flex-col gap-1 p-0">
            {buckets.out.map((id) => swimmerRow(id))}
          </ul>
        </section>
      </div>

      {error && <p className="m-0 mt-3 text-[12px] font-bold text-[var(--t-danger)]" role="alert">{error}</p>}

      <div className="mt-3 flex flex-wrap justify-end gap-2">
        <button type="button" className={ghostBtn} onClick={onCancel} disabled={saving}>Cancel</button>
        <button type="button" className={primaryBtn} onClick={save} disabled={saving}>
          {saving ? 'Saving…' : 'Save plan'}
        </button>
      </div>

      {confirm === 'distribute' && (
        <ConfirmDialog
          title="Re-distribute everyone?"
          confirmLabel="Distribute"
          busyLabel="Distributing…"
          onConfirm={async () => { await distribute(); return { success: true }; }}
          onClose={() => setConfirm(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            Everyone coming today is placed again by level. Manual moves will be lost.
            People in “Not today” stay there.
          </p>
        </ConfirmDialog>
      )}
      {confirm === 'auto' && (
        <ConfirmDialog
          title="Set up the lanes automatically?"
          confirmLabel="Auto lanes"
          busyLabel="Working…"
          onConfirm={async () => { await autoLanes(); return { success: true }; }}
          onClose={() => setConfirm(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            The level of every lane and who swims where are replaced: lanes are split between levels
            by how many are coming, the strongest on the lowest numbers, neighbouring levels share a
            lane when there are too few. Workouts stay on their lanes; “Not today” stays as is.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default GroupLaneEditor;
