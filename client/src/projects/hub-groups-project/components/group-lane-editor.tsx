import React, { useEffect, useLayoutEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevel, HubGroupLevels, LanePlan, LanePlanInput, LanePlanSwimmer } from '../types';
import LaneChip from './group-lane-chip';
import LaneMoveRow from './group-lane-move-row';
import LaneSaveBar, { type SaveBarState } from './group-lane-savebar';
import LaneToolbar, { shortDay } from './group-lane-toolbar';
import { lanePlansApi } from './lane-plans-api';
import { levelColor } from './level-color';

/**
 * Редактор плана дорожек — тренер (docs/plans/lane-plans-plan.md, L3; доведён в Т7 по решению
 * Р6 из docs/plans/trainings-tab-redesign-plan.md, хендофф Trainings Tab: 1e / 6b).
 *
 * Отдельного режима правки нет: открыл Lanes на дату с планом — сразу этот редактор. Три корзины
 * без флагов, как в модели: дорожка N, «Unassigned» (пришёл, не разложен) и «Not today» (снят,
 * свёрнутой строкой под всем). Перенос:
 * - телефон — тап по плашке выбирает пловца, в карточке его дорожки появляется строка
 *   «Move <name> to [1][2][3] [Unassigned] [Not today] [Lead]»;
 * - десктоп — ещё и перетаскивание: бросок на колонку — в конец, бросок НА плашку — вставка ПЕРЕД
 *   ней (так меняется порядок; кнопки «↑» больше нет).
 * Порядок внутри дорожки = кто ведёт. «Distribute» раскладывает пришедших по уровням дорожек на
 * сервере и НЕ сохраняет — сохраняет Save. «Auto lanes» — шаг раньше: тренер задал только число
 * дорожек, сервер сам делит их между уровнями по числу пришедших (соседние уровни при нехватке
 * дорожек — вместе) и раскладывает людей; задания дорожек остаются на своих номерах. «Max per lane»
 * (лимит людей на дорожку, решение Влада 20.10.2026) обе кнопки отдают серверу: дорожка полна —
 * нижний подуровень её уровня по одному переходит на дорожку следующего уровня; счётчик дорожки
 * тогда «5 / 6», переполненная — цветом тревоги. Дорожки сверх
 * числа не выбрасываются из черновика: уменьшил и вернул — задания и люди на месте; пока дорожка
 * убрана, её люди показываются и сохраняются как Unassigned (считается на лету, а не переносом —
 * иначе два быстрых «−» теряли шаг).
 *
 * «Copy from …» (L5) — тот же редактор с заготовкой `template`: прошлый план целиком (дорожки,
 * уровни, задания, заметка, расстановка), но только нынешний состав — ушедшие из группы выпадают
 * (в новый план сервер их и не примет), новенькие встают в Unassigned. Ничего не пишется, пока
 * тренер не нажмёт Save (липкая панель `group-lane-savebar.tsx`).
 *
 * «Есть несохранённые правки» = нормализованный вход сохранения (`LanePlanInput`) черновика не
 * равен входу сохранённого плана — сравнение по содержимому, не по ссылкам на массивы.
 */

const MAX_LANES = 12;

/**
 * Десктоп: дорожки колонками (Р6 п.5 — при ≤ 6), пока колонке достаётся хотя бы MIN_COLUMN px,
 * иначе сетка. Редактор не шире 1118px, и 6 колонок там — по 137px: имя в плашке уже не видно
 * (на 960px — по 100px). Поэтому порог — по ширине, а «≤ 6» — только потолок.
 */
const MAX_COLUMNS = 6;
const MIN_COLUMN = 150;
const UNASSIGNED_COLUMN = 220;
const COLUMN_GAP = 12;

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

const field: React.CSSProperties = {
  borderColor: 'var(--deep-card-border)', background: 'var(--deep-card-bg)', color: 'var(--deep-text)',
};

const NO_AWAY: ReadonlySet<number> = new Set();

const emptyLanes = (): LaneDraft[] => Array.from({ length: MAX_LANES }, () => ({ levelId: null, workout: '' }));

/**
 * Черновик: из сохранённого плана, из заготовки (копия прошлого) или пустой (весь состав — в
 * Unassigned). Не разложенный никуда из состава: в своём плане — «Not today» (пришёл в группу
 * после сохранения), в копии — Unassigned (новенький, тренер должен его заметить).
 * Кто в этот день «не в воде» (`away`: On break / Doesn't train) — в новом плане и копии сразу
 * «Not today»: иначе Auto lanes и Distribute ставят их на дорожки. Сохранённый план не трогаем —
 * там тренер решил сам.
 */
function initialDraft(
  plan: LanePlan | null, template: LanePlan | null, levels: HubGroupLevels, laneCountHint: number,
  maxPerLaneHint: number | null, away: ReadonlySet<number>,
) {
  const lanes = emptyLanes();
  const buckets: Buckets = { lanes: Array.from({ length: MAX_LANES }, () => []), unassigned: [], out: [] };
  const info = new Map<number, LanePlanSwimmer>();

  // Текущий уровень — из карточки уровней (свежее снимка).
  const levelOf = new Map(levels.swimmers.map((s) => [s.swimmerId, s]));
  const remember = (s: LanePlanSwimmer) => {
    const current = levelOf.get(s.swimmer_id);
    info.set(s.swimmer_id, current
      ? { ...s, level_id: current.levelId, sub_level: current.subLevel ?? null }
      : s);
  };

  levels.swimmers.forEach((s) => remember({
    swimmer_id: s.swimmerId, name: s.name, name_en: s.nameEn, birth_year: s.birthYear, level_id: s.levelId,
    sub_level: s.subLevel ?? null,
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

  if (!plan && away.size > 0) {
    const isAway = (id: number) => away.has(id);
    buckets.out.push(...[...buckets.lanes.flat(), ...buckets.unassigned].filter(isAway));
    buckets.lanes = buckets.lanes.map((l) => l.filter((id) => !isAway(id)));
    buckets.unassigned = buckets.unassigned.filter((id) => !isAway(id));
  }

  return {
    laneCount: source?.lane_count ?? laneCountHint,
    maxPerLane: source ? source.max_per_lane ?? null : maxPerLaneHint,
    note: source?.note ?? '',
    lanes,
    buckets,
    info,
  };
}

/** Вход сохранения из черновика: видимые дорожки; люди убранных дорожек — в Unassigned. */
function buildInput(
  laneCount: number, maxPerLane: number | null, note: string, lanes: LaneDraft[], buckets: Buckets,
): LanePlanInput {
  const laneNos = Array.from({ length: laneCount }, (_, i) => i + 1);
  const unassigned = [...buckets.unassigned, ...buckets.lanes.slice(laneCount).flat()];
  return {
    lane_count: laneCount,
    max_per_lane: maxPerLane,
    note: note.trim() || null,
    lanes: laneNos.map((no) => ({
      lane_no: no, level_id: lanes[no - 1].levelId, workout: lanes[no - 1].workout.trim() || null,
    })),
    swimmers: [
      ...laneNos.flatMap((no) => buckets.lanes[no - 1].map((id) => ({ swimmer_id: id, lane_no: no }))),
      ...unassigned.map((id) => ({ swimmer_id: id, lane_no: null })),
    ],
  };
}

/**
 * Ключ сравнения «правки есть / нет». Порядок внутри дорожки значим (первый ведёт), порядок в
 * Unassigned — нет: тренер убрал человека в «Not today» и вернул — это не правка.
 */
function inputKey(input: LanePlanInput): string {
  const placed = input.swimmers.filter((s) => s.lane_no != null);
  const unassigned = input.swimmers.filter((s) => s.lane_no == null).sort((a, b) => a.swimmer_id - b.swimmer_id);
  return JSON.stringify({ ...input, swimmers: [...placed, ...unassigned] });
}

function GroupLaneEditor({
  groupId, date, plan, template = null, levels, laneCountHint, maxPerLaneHint = null, away = NO_AWAY, desktop, toolbarHost,
  copyFrom, onSaved, onSetStatus, onDelete, onCopy, onDirtyChange,
}: {
  groupId: number;
  date: string;
  /** Сохранённый план этой даты; null — плана ещё нет. */
  plan: LanePlan | null;
  /** Заготовка черновика — копия другого плана («Copy from …»); перекрывает `plan` как исходный вид. */
  template?: LanePlan | null;
  levels: HubGroupLevels;
  /** Число дорожек нового плана (как в прошлом плане). */
  laneCountHint: number;
  /** Лимит людей на дорожку нового плана (как в прошлом плане); null — без лимита. */
  maxPerLaneHint?: number | null;
  /** Кто в этот день «не в воде» — в «Not today» нового плана и копии. Читается при монтировании. */
  away?: ReadonlySet<number>;
  desktop: boolean;
  /** Десктоп: узел шапки вида, куда выносится тулбар (портал); пока его нет — тулбар не рисуем. */
  toolbarHost?: HTMLElement | null;
  /** Дата плана, с которого можно скопировать; null — копировать не с чего. */
  copyFrom: string | null;
  onSaved: (plan: LanePlan) => void;
  /** Publish / Unpublish в родителе; вернул текст ошибки или null. */
  onSetStatus: (publish: boolean) => Promise<string | null>;
  /** Запросить удаление плана (подтверждение — в родителе). */
  onDelete: () => void;
  /** Загрузить план-источник и пересоздать редактор с копией; вернул текст ошибки или null. */
  onCopy: (sourceDate: string) => Promise<string | null>;
  onDirtyChange: (dirty: boolean) => void;
}) {
  // Исходный вид черновика: копия (если есть) или сохранённый план. Читается только при монтировании.
  const init = useMemo(
    () => initialDraft(template ? null : plan, template, levels, laneCountHint, maxPerLaneHint, away),
    [plan, template, levels, laneCountHint, maxPerLaneHint, away],
  );
  // С чем сравниваем: сохранённый план (его нет — пустой черновик).
  const base = useMemo(
    () => initialDraft(plan, null, levels, laneCountHint, maxPerLaneHint, away),
    [plan, levels, laneCountHint, maxPerLaneHint, away],
  );

  const [laneCount, setLaneCount] = useState(init.laneCount);
  const [maxPerLane, setMaxPerLane] = useState<number | null>(init.maxPerLane);
  const [note, setNote] = useState(init.note);
  const [lanes, setLanes] = useState<LaneDraft[]>(init.lanes);
  const [buckets, setBuckets] = useState<Buckets>(init.buckets);
  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [distributing, setDistributing] = useState(false);
  /** Что подтверждаем: повторный Distribute / Auto lanes поверх заданного либо замену копией. */
  const [confirm, setConfirm] = useState<'distribute' | 'auto' | 'copy' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState<string | null>(null);
  /** Выбранный для переноса пловец — один на весь редактор. */
  const [selected, setSelected] = useState<number | null>(null);
  const [outOpen, setOutOpen] = useState(false);
  /** Ширина редактора — выбор «колонки или сетка» на десктопе. */
  const [root, setRoot] = useState<HTMLDivElement | null>(null);
  const [width, setWidth] = useState(0);

  useLayoutEffect(() => {
    if (!root) return undefined;
    setWidth(root.clientWidth);
    const ro = new ResizeObserver(() => setWidth(root.clientWidth));
    ro.observe(root);
    return () => ro.disconnect();
  }, [root]);

  const info = useMemo(() => new Map([...base.info, ...init.info]), [base, init]);
  const levelById = useMemo(() => new Map(levels.levels.map((l) => [l.id, l])), [levels]);
  const laneNos = Array.from({ length: laneCount }, (_, i) => i + 1);

  const circleColor = (swimmerId: number): string | null => {
    const levelId = info.get(swimmerId)?.level_id;
    const level = levelId != null ? levelById.get(levelId) : undefined;
    return level ? levelColor(level) : null;
  };

  // ── Несохранённые правки ──────────────────────────────────────────────────

  const draftKey = inputKey(buildInput(laneCount, maxPerLane, note, lanes, buckets));
  const baseKey = useMemo(
    () => inputKey(buildInput(base.laneCount, base.maxPerLane, base.note, base.lanes, base.buckets)),
    [base],
  );
  const dirty = draftKey !== baseKey;
  // Плана ещё нет — сохранять нужно, даже если черновик пуст.
  const needsSave = dirty || plan == null;

  useEffect(() => { onDirtyChange(dirty); }, [dirty, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);

  // Esc снимает выбор пловца.
  useEffect(() => {
    if (selected == null) return undefined;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setSelected(null); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selected]);

  // ── Перенос ───────────────────────────────────────────────────────────────

  /** В конец корзины; `before` — вставить перед этим пловцом; `first` — поставить первым («Lead»). */
  const move = (swimmerId: number, target: Target, opts?: { before?: number; first?: boolean }) => {
    setBuckets((b) => {
      const strip = (list: number[]) => list.filter((id) => id !== swimmerId);
      const next: Buckets = { lanes: b.lanes.map(strip), unassigned: strip(b.unassigned), out: strip(b.out) };
      const list = target === 'unassigned' ? next.unassigned : target === 'out' ? next.out : next.lanes[target - 1];
      const at = opts?.first ? 0 : opts?.before != null ? list.indexOf(opts.before) : -1;
      if (at >= 0) list.splice(at, 0, swimmerId);
      else list.push(swimmerId);
      return next;
    });
    setSelected(null);
  };

  const changeLaneCount = (delta: number) =>
    setLaneCount((c) => Math.min(MAX_LANES, Math.max(1, c + delta)));

  /** Unassigned на экране и в сохранении = своя корзина + люди убранных дорожек. */
  const unassigned = [...buckets.unassigned, ...buckets.lanes.slice(laneCount).flat()];

  const patchLane = (laneNo: number, patch: Partial<LaneDraft>) =>
    setLanes((ls) => ls.map((l, i) => (i === laneNo - 1 ? { ...l, ...patch } : l)));

  // ── Distribute / Auto lanes / Save ────────────────────────────────────────

  const lanesInput = () => buildInput(laneCount, maxPerLane, note, lanes, buckets).lanes;

  const present = () => [...buckets.lanes.slice(0, laneCount).flat(), ...unassigned];

  const distribute = async () => {
    setDistributing(true);
    setError(null);
    const result = await lanePlansApi.distribute(groupId, {
      lane_count: laneCount, max_per_lane: maxPerLane, lanes: lanesInput(), swimmer_ids: present(),
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
    setSelected(null);
  };

  const autoLanes = async () => {
    setDistributing(true);
    setError(null);
    const result = await lanePlansApi.autoLanes(groupId, {
      lane_count: laneCount, max_per_lane: maxPerLane, swimmer_ids: present(),
    });
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

  const doCopy = async (): Promise<string | null> => {
    if (!copyFrom) return null;
    setError(null);
    const err = await onCopy(copyFrom);
    if (err) setError(err);
    return err;
  };

  const askCopy = () => {
    // На дату уже есть план или есть несохранённые правки — заменяем только после подтверждения.
    if (plan != null || dirty) setConfirm('copy');
    else void doCopy();
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    const result = await lanePlansApi.save(groupId, date, buildInput(laneCount, maxPerLane, note, lanes, buckets));
    setSaving(false);
    if (result.ok && result.data) onSaved(result.data);
    else setError(result.error ?? 'Could not save. Try again.');
  };

  const setStatus = async (publish: boolean) => {
    setPublishing(true);
    setError(null);
    const err = await onSetStatus(publish);
    setPublishing(false);
    if (err) setError(err);
  };

  // ── Вёрстка ───────────────────────────────────────────────────────────────

  const dropProps = (target: Target) => ({
    onDragOver: (e: React.DragEvent) => { e.preventDefault(); setDragOver(String(target)); },
    onDragLeave: () => setDragOver((t) => (t === String(target) ? null : t)),
    onDrop: (e: React.DragEvent) => {
      e.preventDefault();
      setDragOver(null);
      const id = Number(e.dataTransfer.getData('text/plain'));
      if (id) move(id, target);
    },
  });
  const dropOutline = (target: Target): React.CSSProperties =>
    (dragOver === String(target) ? { outline: '2px solid var(--deep-accent)' } : {});

  const where = (swimmerId: number): Target => {
    const lane = buckets.lanes.findIndex((l) => l.includes(swimmerId));
    if (lane >= 0) return lane < laneCount ? lane + 1 : 'unassigned';
    return buckets.unassigned.includes(swimmerId) ? 'unassigned' : 'out';
  };

  const chip = (swimmerId: number) => {
    const s = info.get(swimmerId);
    if (!s) return null;
    const beforeKey = `before:${swimmerId}`;
    const dragProps: React.HTMLAttributes<HTMLElement> | undefined = desktop ? {
      draggable: true,
      onDragStart: (e) => { e.dataTransfer.setData('text/plain', String(swimmerId)); e.dataTransfer.effectAllowed = 'move'; },
      onDragOver: (e) => { e.preventDefault(); e.stopPropagation(); setDragOver(beforeKey); },
      onDragLeave: () => setDragOver((t) => (t === beforeKey ? null : t)),
      onDrop: (e) => {
        e.preventDefault();
        e.stopPropagation();
        setDragOver(null);
        const dragged = Number(e.dataTransfer.getData('text/plain'));
        if (dragged && dragged !== swimmerId) move(dragged, where(swimmerId), { before: swimmerId });
      },
    } : undefined;
    return (
      <LaneChip
        key={swimmerId}
        name={s.name}
        nameEn={s.name_en}
        color={circleColor(swimmerId)}
        subLevel={s.sub_level ?? null}
        leftGroup={!!s.left_group}
        selected={selected === swimmerId}
        desktop={desktop}
        onSelect={() => setSelected((cur) => (cur === swimmerId ? null : swimmerId))}
        dragProps={dragProps}
        dropActive={dragOver === beforeKey}
      />
    );
  };

  /** Строка «Move … to» — в карточке той корзины, где стоит выбранный пловец. */
  const moveRow = (target: Target) => {
    if (selected == null || where(selected) !== target) return null;
    const s = info.get(selected);
    if (!s) return null;
    const index = typeof target === 'number' ? buckets.lanes[target - 1].indexOf(selected) : -1;
    return (
      <LaneMoveRow
        name={s.name || s.name_en}
        laneNos={laneNos.filter((no) => no !== target)}
        canUnassign={target !== 'unassigned'}
        canOut={target !== 'out'}
        canLead={typeof target === 'number' && index > 0}
        onLane={(no) => move(selected, no)}
        onUnassign={() => move(selected, 'unassigned')}
        onOut={() => move(selected, 'out')}
        onLead={() => typeof target === 'number' && move(selected, target, { first: true })}
      />
    );
  };

  /** Столбик плашек (десктоп) или ряд с переносом (телефон). `wide` — Unassigned отдельной строкой. */
  const chipList = (ids: number[], wide = false) => (
    <div
      className={desktop && !wide ? 'flex flex-col gap-1.5' : desktop ? 'grid gap-1.5' : 'flex flex-wrap gap-1.5'}
      style={desktop && wide ? { gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' } : undefined}
    >
      {ids.map((id) => chip(id))}
    </div>
  );

  const dropHere = desktop && (
    <div
      aria-hidden="true"
      className="flex h-[36px] items-center justify-center rounded-[10px] text-[11px] font-bold"
      style={{ border: '1px dashed var(--deep-pool-border)', color: 'var(--deep-pool-label)' }}
    >
      Drop here
    </div>
  );

  const laneCard = (no: number, columns: boolean) => {
    const lane = lanes[no - 1];
    const prev = no > 1 ? lanes[no - 2] : null;
    const ids = buckets.lanes[no - 1];
    const over = maxPerLane != null && ids.length > maxPerLane;
    return (
      <section
        key={no}
        {...dropProps(no)}
        className={`flex min-w-0 flex-col rounded-[16px] border ${desktop ? 'gap-2.5 p-3' : 'gap-2 px-3 py-2.5'} ${desktop && columns ? 'min-h-[300px]' : ''}`}
        style={{ borderColor: 'var(--deep-pool-border)', background: 'var(--deep-pool-water)', ...dropOutline(no) }}
        aria-label={`Lane ${no}`}
      >
        <header className="flex flex-wrap items-center gap-2">
          <span className={`leading-none ${desktop ? 'text-[22px]' : 'text-[18px]'}`} style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-pool-label)' }}>
            {no}
          </span>
          <select
            value={lane.levelId ?? ''}
            onChange={(e) => patchLane(no, { levelId: e.target.value === '' ? null : Number(e.target.value) })}
            aria-label={`Level of lane ${no}`}
            className={`min-w-0 flex-1 basis-[120px] cursor-pointer rounded-[8px] border px-2 text-[12px] font-extrabold ${desktop ? 'h-[32px]' : 'h-[34px]'}`}
            style={field}
          >
            <option value="">No level</option>
            {levels.levels.map((l: HubGroupLevel) => <option key={l.id} value={l.id}>{l.rank} · {l.name}</option>)}
          </select>
          {/* С лимитом — «5 / 6»; переполнена (вниз перелиться некому или некуда) — цветом тревоги. */}
          <span
            className="hp-mono ml-auto text-[10.5px]"
            style={{ color: over ? 'var(--t-danger)' : 'var(--t-text-3)', fontWeight: over ? 800 : undefined }}
            title={over ? `More than  in this lane` : undefined}
          >
            {maxPerLane != null
              ? `${ids.length} / ${maxPerLane}`
              : desktop ? `${ids.length} ${ids.length === 1 ? 'swimmer' : 'swimmers'}` : ids.length}
          </span>
        </header>
        <textarea
          value={lane.workout}
          onChange={(e) => patchLane(no, { workout: e.target.value })}
          rows={3}
          maxLength={4000}
          placeholder="Workout, e.g. 8×100 free @1:40"
          aria-label={`Workout of lane ${no}`}
          dir="auto"
          className="hp-mono min-h-[56px] w-full resize-y rounded-[8px] border px-2.5 py-1.5 text-[12px] font-bold leading-[1.5]"
          style={field}
        />
        {/* Копия задания — только соседу того же уровня (дорожки 2–3 одного уровня). */}
        {prev && prev.levelId === lane.levelId && prev.workout.trim() !== '' && prev.workout !== lane.workout && (
          <button
            type="button"
            className="cursor-pointer self-start border-0 bg-transparent p-0 text-[11.5px] font-extrabold underline"
            style={{ color: 'var(--deep-accent)' }}
            onClick={() => patchLane(no, { workout: prev.workout })}
          >
            Same workout as lane {no - 1}
          </button>
        )}
        {ids.length > 0 && chipList(ids)}
        {moveRow(no)}
        {dropHere}
      </section>
    );
  };

  const unassignedCard = (wide: boolean) => (
    <section
      {...dropProps('unassigned')}
      className="flex min-w-0 flex-col gap-2 rounded-[16px] p-3"
      style={{ border: '1px dashed var(--deep-gold-border)', ...dropOutline('unassigned') }}
      aria-label="Unassigned"
    >
      <div className="flex items-center gap-1.5 text-[10px] font-extrabold uppercase tracking-[.07em]" style={{ color: 'var(--deep-gold)' }}>
        <span aria-hidden="true" className="inline-block flex-none rounded-full" style={{ width: 7, height: 7, background: 'var(--deep-gold-bar)' }} />
        Unassigned · {unassigned.length}
      </div>
      <div className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
        In today’s plan, no lane yet. Distribute places them by level.
      </div>
      {unassigned.length > 0 && chipList(unassigned, wide)}
      {moveRow('unassigned')}
      {dropHere}
    </section>
  );

  const notToday = (
    <div {...dropProps('out')} className="mt-3 rounded-[12px]" style={dropOutline('out')}>
      <button
        type="button"
        onClick={() => {
          setOutOpen((o) => !o);
          // Свернули список — выбранный в нём пловец исчез с экрана, выбор снимаем.
          if (outOpen && selected != null && where(selected) === 'out') setSelected(null);
        }}
        aria-expanded={outOpen}
        className="flex min-h-[44px] w-full cursor-pointer items-center gap-1.5 rounded-[12px] bg-transparent px-3 text-left text-[12px] font-extrabold"
        style={{ border: '1px dashed var(--deep-card-border)', color: 'var(--t-text-3)' }}
      >
        <span>Not today · {buckets.out.length}</span>
        <span aria-hidden="true" className={`inline-block transition-transform ${outOpen ? 'rotate-90' : ''}`}>›</span>
      </button>
      {outOpen && (
        <div className="mt-2 flex flex-col gap-2 px-1">
          <div className="text-[11px] font-bold" style={{ color: 'var(--t-text-3)' }}>
            not in this plan · members don’t see this list
          </div>
          {buckets.out.length > 0 && chipList(buckets.out, desktop)}
          {moveRow('out')}
        </div>
      )}
    </div>
  );

  const toolbar = (
    <LaneToolbar
      desktop={desktop}
      laneCount={laneCount}
      minLanes={1}
      maxLanes={MAX_LANES}
      onLaneCount={changeLaneCount}
      maxPerLane={maxPerLane}
      onMaxPerLane={setMaxPerLane}
      working={distributing}
      canRun={present().length > 0}
      onDistribute={askDistribute}
      onAutoLanes={askAutoLanes}
      copyFrom={copyFrom}
      onCopy={askCopy}
      canDelete={plan != null}
      onDelete={onDelete}
    />
  );

  const columnWidth = (width - UNASSIGNED_COLUMN - COLUMN_GAP * laneCount) / laneCount;
  const wide = desktop && (laneCount > MAX_COLUMNS || columnWidth < MIN_COLUMN);
  const saveState: SaveBarState = needsSave ? 'unsaved' : (plan?.status ?? 'draft');

  return (
    <div ref={setRoot}>
      {/* Тулбар: телефон — здесь, десктоп — в шапке вида (портал; пока узла нет — не рисуем). */}
      {desktop ? (toolbarHost ? createPortal(toolbar, toolbarHost) : null) : toolbar}

      <input
        value={note}
        onChange={(e) => setNote(e.target.value)}
        maxLength={500}
        dir="auto"
        placeholder="Note for everyone — e.g. Warm-up together 400 easy"
        aria-label="Note for everyone"
        className={`w-full min-w-0 rounded-[10px] border px-3 text-[12.5px] font-bold ${desktop ? 'mt-0 h-[36px]' : 'mt-2.5 h-[40px]'}`}
        style={field}
      />

      {/* Дорожки (Р6 п.5): телефон — столбиком; десктоп — колонки + Unassigned 220, пока влезают (MIN_COLUMN), иначе сетка. */}
      {!desktop && (
        <div className="mt-3 flex flex-col gap-3">
          {laneNos.map((no) => laneCard(no, false))}
          {unassignedCard(false)}
        </div>
      )}
      {desktop && !wide && (
        <div
          className="mt-3 grid items-start gap-3"
          style={{ gridTemplateColumns: `repeat(${laneCount}, minmax(0, 1fr)) 220px` }}
        >
          {laneNos.map((no) => laneCard(no, true))}
          {unassignedCard(false)}
        </div>
      )}
      {desktop && wide && (
        <>
          <div className="mt-3 grid items-start gap-3" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' }}>
            {laneNos.map((no) => laneCard(no, false))}
          </div>
          <div className="mt-3">{unassignedCard(true)}</div>
        </>
      )}

      {notToday}

      <LaneSaveBar
        state={saveState}
        saving={saving}
        busy={publishing}
        error={error}
        desktop={desktop}
        onSave={save}
        onPublish={() => setStatus(true)}
        onUnpublish={() => setStatus(false)}
      />

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
          {maxPerLane != null && (
            <p className="m-0 mt-2 text-[13px] text-[var(--t-text-2)]">
              With {maxPerLane} per lane, people from the bottom sub-level of a full lane move down to the next
              level’s lane, one by one, while it has room.
            </p>
          )}
        </ConfirmDialog>
      )}
      {confirm === 'copy' && copyFrom && (
        <ConfirmDialog
          title={`Replace this plan with a copy of ${shortDay(copyFrom)}?`}
          confirmLabel="Replace"
          busyLabel="Copying…"
          onConfirm={async () => {
            const err = await doCopy();
            return err ? { success: false, error: err } : { success: true };
          }}
          onClose={() => setConfirm(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            Lanes, levels, workouts and who swims where are copied; today’s people who left the group
            drop out. Nothing is saved until you press Save.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default GroupLaneEditor;
