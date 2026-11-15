import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import ConfirmDialog from '../../components/confirm-dialog/confirm-dialog';
import type { HubGroupLevel, HubGroupLevels, LaneChange, LanePlan, LanePlanInput, LanePlanSwimmer } from '../types';
import LaneChip from './group-lane-chip';
import LaneMoveRow from './group-lane-move-row';
import LanePlanStatus from './lane-plan-status';
import { lanePlansApi, shortDay } from './lane-plans-api';
import { levelColor } from './level-color';
import { laneShortNames } from './lane-short-names';
import RollToast, { type RollToastData } from './trainings/coach/roll-toast';
import { useTrainingsTab, type PoolCommand } from './trainings/trainings-context';
import { useLaneAutosave } from './use-lane-autosave';

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
 * Порядок внутри дорожки = кто ведёт.
 *
 * Над дорожками две кнопки (docs/plans/trainings-coach-flow-plan.md, CF2): «Auto-arrange» и
 * «Pool setup». «Auto-arrange» (бывшие Distribute + Auto lanes, ручка `auto-lanes`): тренер задал
 * только число дорожек, сервер сам делит их между уровнями по числу пришедших (соседние уровни при
 * нехватке дорожек — вместе) и раскладывает людей; «Reserve lanes» (параметр плана, решение Влада
 * 14.11.2026) — столько последних дорожек всегда остаются пустыми про запас, даже когда остальных
 * не хватает (уровни тогда сливаются, при лимите дорожки переполняются); задания дорожек остаются на своих номерах. «Pool setup» — шторка `trainings/sheets/pool-setup-sheet.tsx`: число
 * дорожек, «Reserve lanes», «Max per lane», уровень каждой дорожки, «Copy from …», удаление плана; редактор отдаёт
 * ей снимок черновика и принимает правки по «Done».
 *
 * Закрепление (CF2): пловец, которого тренер перенёс на ДРУГУЮ дорожку руками (строка «Move … to»
 * или перетаскивание), закреплён — следующий «Auto-arrange» его не двигает (значок 📌 на плашке).
 * Снять — «Unpin» в строке «Move … to»; перенос в Unassigned / Not today тоже снимает. Смена
 * порядка внутри дорожки («Lead», вставка перед соседом) закреплением не считается. Хранится в
 * плане (`pinned` у пловца), в копию («Copy from …») не переносится.
 *
 * «Max per lane» (лимит людей на дорожку, решение Влада 20.10.2026) уходит серверу: дорожка полна —
 * нижний подуровень её уровня по одному переходит на дорожку следующего уровня; счётчик дорожки
 * тогда «5 / 6», переполненная — цветом тревоги. Дорожки сверх
 * числа не выбрасываются из черновика: уменьшил и вернул — задания и люди на месте; пока дорожка
 * убрана, её люди показываются и сохраняются как Unassigned (считается на лету, а не переносом —
 * иначе два быстрых «−» теряли шаг).
 *
 * «Copy from …» (L5) — тот же редактор с заготовкой `template`: прошлый план целиком (дорожки,
 * уровни, задания, заметка, расстановка), но только нынешний состав — ушедшие из группы выпадают
 * (в новый план сервер их и не примет), новенькие встают в Unassigned. Копия сохраняется сама,
 * как любая правка, — поэтому замена существующего плана идёт через подтверждение.
 *
 * Сохранение (CF3): кнопки Save нет — каждая правка уходит на сервер сама, с паузой после
 * последнего изменения, в черновике и в опубликованном плане (`use-lane-autosave.ts`; вернуть
 * явный Save — там же, редактор не переписывать). «Правка есть» = нормализованный вход сохранения
 * (`LanePlanInput`) не равен последнему сохранённому — сравнение по содержимому, не по ссылкам.
 * Наверх (`onDirtyChange`) уходит только «запись НЕ УДАЛАСЬ»: тогда уход со стола спрашивает
 * «Discard unsaved changes?». После переноса человека — тост с Undo (`RollToast`).
 *
 * Статус плана и единственная кнопка Publish / Unpublish — строка `LanePlanStatus`: в узле
 * `statusHost` (шапка рабочего стола или слот нижней панели каркаса), без узла — под дорожками.
 * Publish досылает правки и открывает диалог `trainings/sheets/publish-sheet.tsx`.
 *
 * Ответ сдвигает пловца на сервере (CF4: «Going» — на дорожку уровня, «Can’t» — в «Not today»):
 * перекличка и карточка человека присылают сдвиг командой `lane-change` (`sendPoolCommand`
 * контекста), редактор повторяет его в черновике — иначе следующее автосохранение вернуло бы
 * пловца назад. Команды `show-swimmer` / `show-lane` — переход из «Needs you» и карточки.
 */

const MAX_LANES = 12;

/**
 * Десктоп: ВСЕ дорожки одной строкой, сколько бы их ни было (решение Влада 09.11.2026: «6 или 8
 * в одну линию» — бассейн виден целиком, как с бортика). Плашки — пилюли с одним именем, поэтому
 * колонке хватает MIN_COLUMN; не влезли — строка прокручивается вбок, а не ломается в сетку.
 * Unassigned — под дорожками во всю ширину.
 */
const MIN_COLUMN = 112;

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
 * Кто в этот день «не в воде» (`away`: On break / Doesn't train / «Can't come») — в новом плане и копии сразу
 * «Not today»: иначе Auto-arrange ставит их на дорожки. Сохранённый план не трогаем —
 * там тренер решил сам.
 */
function initialDraft(
  plan: LanePlan | null, template: LanePlan | null, levels: HubGroupLevels, laneCountHint: number,
  maxPerLaneHint: number | null, reservedLanesHint: number, away: ReadonlySet<number>,
) {
  const lanes = emptyLanes();
  const buckets: Buckets = { lanes: Array.from({ length: MAX_LANES }, () => []), unassigned: [], out: [] };
  const info = new Map<number, LanePlanSwimmer>();
  // Закрепление — только из своего сохранённого плана: в копию не переносится.
  const pinned = new Set<number>();

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
      l.swimmers.filter(keep).forEach((s) => {
        remember(s);
        buckets.lanes[l.lane_no - 1].push(s.swimmer_id);
        if (plan && s.pinned) pinned.add(s.swimmer_id);
      });
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
    reservedLanes: source ? source.reserved_lanes ?? 1 : reservedLanesHint,
    note: source?.note ?? '',
    lanes,
    buckets,
    info,
    pinned,
  };
}

/** Повторить в корзинах сдвиг пловца, сделанный сервером (CF4): убрать отовсюду, поставить на место `after`. */
function applyLaneChange(b: Buckets, change: LaneChange): Buckets {
  const id = change.swimmer_id;
  const strip = (list: number[]) => list.filter((x) => x !== id);
  const next: Buckets = { lanes: b.lanes.map(strip), unassigned: strip(b.unassigned), out: strip(b.out) };
  const { after } = change;
  const list = !after.in_plan ? next.out
    : after.lane_no == null ? next.unassigned
      : next.lanes[after.lane_no - 1];
  if (!list) return b;
  list.splice(Math.min(Math.max(0, after.order_no), list.length), 0, id);
  return next;
}

/** Вход сохранения из черновика: видимые дорожки; люди убранных дорожек — в Unassigned. */
function buildInput(
  laneCount: number, maxPerLane: number | null, reservedLanes: number, note: string, lanes: LaneDraft[],
  buckets: Buckets, pinned: ReadonlySet<number>,
): LanePlanInput {
  const laneNos = Array.from({ length: laneCount }, (_, i) => i + 1);
  const unassigned = [...buckets.unassigned, ...buckets.lanes.slice(laneCount).flat()];
  return {
    lane_count: laneCount,
    max_per_lane: maxPerLane,
    reserved_lanes: reservedLanes,
    note: note.trim() || null,
    lanes: laneNos.map((no) => ({
      lane_no: no, level_id: lanes[no - 1].levelId, workout: lanes[no - 1].workout.trim() || null,
    })),
    swimmers: [
      ...laneNos.flatMap((no) => buckets.lanes[no - 1].map((id) => ({ swimmer_id: id, lane_no: no, pinned: pinned.has(id) }))),
      ...unassigned.map((id) => ({ swimmer_id: id, lane_no: null, pinned: false })),
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
  groupId, date, plan, template = null, levels, laneCountHint, maxPerLaneHint = null, reservedLanesHint = 1, away = NO_AWAY, desktop, toolbarHost,
  statusHost, abandon, copyFrom, onSaved, onSetStatus, onDelete, onCopy, onDirtyChange, onFocusPool,
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
  /** Запас дорожек нового плана (как в прошлом плане); по умолчанию одна. */
  reservedLanesHint?: number;
  /** Кто в этот день «не в воде» — в «Not today» нового плана и копии. Читается при монтировании. */
  away?: ReadonlySet<number>;
  desktop: boolean;
  /** Десктоп: узел шапки вида, куда выносится тулбар (портал); пока его нет — тулбар не рисуем. */
  toolbarHost?: HTMLElement | null;
  /**
   * Куда вынести строку статуса и Publish (портал): `undefined` — некуда, строка стоит под
   * дорожками; `null` — узел ещё не появился, строку пока не рисуем.
   */
  statusHost?: HTMLElement | null;
  /** План удаляют: несохранённые правки при размонтировании не досылать. */
  abandon?: React.MutableRefObject<boolean>;
  /** Дата плана, с которого можно скопировать; null — копировать не с чего. */
  copyFrom: string | null;
  /** План сохранился (автосохранение). Редактор при этом НЕ пересобирается. */
  onSaved: (plan: LanePlan) => void;
  /** Publish / Unpublish в родителе; вернул текст ошибки или null. `notify` — push участникам. */
  onSetStatus: (publish: boolean, notify?: boolean) => Promise<string | null>;
  /** Запросить удаление плана (подтверждение — в родителе). */
  onDelete: () => void;
  /** Загрузить план-источник и пересоздать редактор с копией; вернул текст ошибки или null. */
  onCopy: (sourceDate: string) => Promise<string | null>;
  /** Запись не удалась — есть правки, которых нет на сервере. */
  onDirtyChange: (dirty: boolean) => void;
  /** Показать панель бассейна (телефон: переключатель People / Pool) — «перейти к человеку» из диалога публикации. */
  onFocusPool?: () => void;
}) {
  // Исходный вид черновика: копия (если есть) или сохранённый план. Читается только при монтировании.
  const init = useMemo(
    () => initialDraft(template ? null : plan, template, levels, laneCountHint, maxPerLaneHint, reservedLanesHint, away),
    [plan, template, levels, laneCountHint, maxPerLaneHint, reservedLanesHint, away],
  );
  // С чем сравниваем: сохранённый план (его нет — пустой черновик).
  const base = useMemo(
    () => initialDraft(plan, null, levels, laneCountHint, maxPerLaneHint, reservedLanesHint, away),
    [plan, levels, laneCountHint, maxPerLaneHint, reservedLanesHint, away],
  );

  const [laneCount, setLaneCount] = useState(init.laneCount);
  const [maxPerLane, setMaxPerLane] = useState<number | null>(init.maxPerLane);
  /** Сколько дорожек Auto-arrange оставляет пустыми про запас. */
  const [reservedLanes, setReservedLanes] = useState(init.reservedLanes);
  const [note, setNote] = useState(init.note);
  const [lanes, setLanes] = useState<LaneDraft[]>(init.lanes);
  const [buckets, setBuckets] = useState<Buckets>(init.buckets);
  /** Закреплённые на своей дорожке: Auto-arrange их не двигает. */
  const [pinned, setPinned] = useState<ReadonlySet<number>>(init.pinned);
  const { openSheet, setPoolListener, upcoming, rollIndex, deskSession } = useTrainingsTab();
  // Человек переклички этого занятия по пловцу — для кнопки «Card» строки «Move … to» (CF4).
  const deskRoll = deskSession ? upcoming[rollIndex]?.rsvp ?? null : null;
  const personOf = (swimmerId: number) =>
    deskRoll?.date === date ? deskRoll.people?.find((p) => (p.linked_swimmer_id ?? p.swimmer_id) === swimmerId) ?? null : null;
  const [publishing, setPublishing] = useState(false);
  /** Тост «<имя> → lane 2 · Undo» после переноса; `restore` — корзины и закрепление до него. */
  const [toast, setToast] = useState<(RollToastData & { restore: () => void }) | null>(null);
  const toastId = useRef(0);
  const [distributing, setDistributing] = useState(false);
  /** Что подтверждаем: повторный Auto-arrange поверх заданного либо замену копией. */
  const [confirm, setConfirm] = useState<'auto' | 'copy' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dragOver, setDragOver] = useState<string | null>(null);
  /** Выбранный для переноса пловец — один на весь редактор. */
  const [selected, setSelected] = useState<number | null>(null);
  const [outOpen, setOutOpen] = useState(false);

  const info = useMemo(() => new Map([...base.info, ...init.info]), [base, init]);
  const levelById = useMemo(() => new Map(levels.levels.map((l) => [l.id, l])), [levels]);
  const laneNos = Array.from({ length: laneCount }, (_, i) => i + 1);

  // Подпись плашки: только имя, тёзкам — с буквой фамилии (тёзки — по всему составу и плану).
  const shortName = useMemo(() => {
    const roster = new Map(levels.swimmers.map((s) => [s.swimmerId, s]));
    return laneShortNames([...info.values()].map((s) => {
      const r = roster.get(s.swimmer_id);
      return { id: s.swimmer_id, first: r?.firstName ?? '', last: r?.lastName ?? '', full: s.name || s.name_en };
    }));
  }, [info, levels]);

  const circleColor = (swimmerId: number): string | null => {
    const levelId = info.get(swimmerId)?.level_id;
    const level = levelId != null ? levelById.get(levelId) : undefined;
    return level ? levelColor(level) : null;
  };

  // ── Автосохранение ────────────────────────────────────────────────────────

  const draftInput = buildInput(laneCount, maxPerLane, reservedLanes, note, lanes, buckets, pinned);
  const draftKey = inputKey(draftInput);
  // С чем сравнивать при открытии: сохранённый план; плана нет — сохраняем сразу (и пустой).
  // Читается один раз: дальше «сохранённое» ведёт сам хук.
  const [openedWith] = useState(() => ({
    plan,
    key: plan
      ? inputKey(buildInput(base.laneCount, base.maxPerLane, base.reservedLanes, base.note, base.lanes, base.buckets, base.pinned))
      : null,
  }));
  const autosave = useLaneAutosave({
    groupId, date, draftKey, input: draftInput, plan: openedWith.plan, savedKey: openedWith.key, abandon, onSaved,
  });
  const saveFailed = autosave.status === 'error';

  useEffect(() => { onDirtyChange(saveFailed); }, [saveFailed, onDirtyChange]);
  useEffect(() => () => onDirtyChange(false), [onDirtyChange]);

  // Esc снимает выбор пловца.
  useEffect(() => {
    if (selected == null) return undefined;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setSelected(null); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [selected]);

  // ── Перенос ───────────────────────────────────────────────────────────────

  /** Где пловец сейчас; люди убранных дорожек считаются Unassigned. */
  const where = (swimmerId: number): Target => {
    const lane = buckets.lanes.findIndex((l) => l.includes(swimmerId));
    if (lane >= 0) return lane < laneCount ? lane + 1 : 'unassigned';
    return buckets.unassigned.includes(swimmerId) ? 'unassigned' : 'out';
  };

  const setPin = (swimmerId: number, on: boolean) =>
    setPinned((p) => {
      if (p.has(swimmerId) === on) return p;
      const next = new Set(p);
      if (on) next.add(swimmerId);
      else next.delete(swimmerId);
      return next;
    });

  /**
   * В конец корзины; `before` — вставить перед этим пловцом; `first` — поставить первым («Lead»).
   * Перенос на ДРУГУЮ дорожку закрепляет пловца, в Unassigned / Not today — снимает закрепление;
   * перестановка внутри дорожки его не меняет.
   */
  const move = (swimmerId: number, target: Target, opts?: { before?: number; first?: boolean }) => {
    const from = where(swimmerId);
    if (typeof target !== 'number') setPin(swimmerId, false);
    else if (from !== target) setPin(swimmerId, true);
    // Тост с Undo — когда человек сменил корзину; перестановка внутри дорожки видна и так.
    if (from !== target) {
      const before = { buckets, pinned };
      const s = info.get(swimmerId);
      toastId.current += 1;
      setToast({
        id: toastId.current,
        name: shortName.get(swimmerId) || s?.name || s?.name_en || '',
        answer: null,
        label: target === 'unassigned' ? 'Unassigned' : target === 'out' ? 'Not today' : `lane ${target}`,
        restore: () => { setBuckets(before.buckets); setPinned(before.pinned); },
      });
    }
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

  /** Unassigned на экране и в сохранении = своя корзина + люди убранных дорожек. */
  const unassigned = [...buckets.unassigned, ...buckets.lanes.slice(laneCount).flat()];

  const patchLane = (laneNo: number, patch: Partial<LaneDraft>) =>
    setLanes((ls) => ls.map((l, i) => (i === laneNo - 1 ? { ...l, ...patch } : l)));

  // ── Auto-arrange / Pool setup / Save ──────────────────────────────────────

  const present = () => [...buckets.lanes.slice(0, laneCount).flat(), ...unassigned];

  /** Раскладка с сервера → корзины; «Not today» не трогаем. Закреплены те, кого сервер оставил на месте. */
  const place = (swimmers: LanePlanInput['swimmers']) => {
    const next: Buckets = { lanes: Array.from({ length: MAX_LANES }, () => []), unassigned: [], out: buckets.out };
    swimmers.forEach((s) => {
      if (s.lane_no == null) next.unassigned.push(s.swimmer_id);
      else next.lanes[s.lane_no - 1].push(s.swimmer_id);
    });
    setBuckets(next);
    setPinned(new Set(swimmers.filter((s) => s.pinned && s.lane_no != null).map((s) => s.swimmer_id)));
    setSelected(null);
    // Раскладка заменила расстановку — «Undo» прошлого переноса вернул бы и её.
    setToast(null);
  };

  const autoLanes = async () => {
    setDistributing(true);
    setError(null);
    const result = await lanePlansApi.autoLanes(groupId, {
      lane_count: laneCount, max_per_lane: maxPerLane, reserved_lanes: reservedLanes, swimmer_ids: present(),
      // Закреплённые на видимых дорожках остаются на месте.
      pinned: buckets.lanes.slice(0, laneCount).flatMap((ids, i) =>
        ids.filter((id) => pinned.has(id)).map((id) => ({ swimmer_id: id, lane_no: i + 1 }))),
    });
    setDistributing(false);
    setConfirm(null);
    if (!result.ok || !result.data) {
      setError(result.error ?? 'Could not arrange the lanes. Try again.');
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

  const doCopy = async (): Promise<string | null> => {
    if (!copyFrom) return null;
    setError(null);
    const err = await onCopy(copyFrom);
    if (err) setError(err);
    return err;
  };

  const askCopy = () => {
    // На дату уже есть план — копия заменит его (и сохранится сама): только после подтверждения.
    if (plan != null) setConfirm('copy');
    else void doCopy();
  };

  /** «Перейти к человеку» из диалога публикации: пловец выбран, его корзина на экране. */
  const showSwimmer = (swimmerId: number) => {
    if (where(swimmerId) === 'out') setOutOpen(true);
    setSelected(swimmerId);
    onFocusPool?.();
    setReveal((n) => n + 1);
  };
  // На телефоне бассейн ниже шапки страницы: без прокрутки выбранного не видно (CF4, «Show in pool»).
  // Прокрутка — после отрисовки и после закрытия шторки. `window.scrollTo`, не `scrollIntoView` (как
  // `entity-page.tsx`), и сразу, без `smooth`: плавную прокрутку здесь гасит закрытие шторки (проверено).
  const [reveal, setReveal] = useState(0);
  useEffect(() => {
    if (reveal === 0) return;
    const timer = window.setTimeout(() => {
      const chip = document.querySelector('button[aria-pressed="true"][aria-label^="Move "]');
      if (!chip) return;
      const rect = chip.getBoundingClientRect();
      if (rect.top >= 80 && rect.bottom <= window.innerHeight - 160) return;   // уже на экране
      window.scrollTo({ top: window.scrollY + rect.top - window.innerHeight / 2 });
    }, 120);
    return () => window.clearTimeout(timer);
  }, [reveal]);

  // Команды редактору (CF4): сдвиг с сервера, «показать пловца / дорожку». Свежие функции — через ref.
  const commandRef = useRef<(command: PoolCommand) => void>(() => {});
  commandRef.current = (command) => {
    if (command.kind === 'lane-change') {
      const { change } = command;
      setBuckets((b) => applyLaneChange(b, change));
      setPin(change.swimmer_id, change.after.in_plan && change.after.lane_no != null && change.after.pinned);
      // Undo прошлого переноса в редакторе вернул бы и этот сдвиг.
      setToast(null);
      return;
    }
    if (command.kind === 'pin') {
      if (typeof where(command.swimmerId) === 'number') setPin(command.swimmerId, command.on);
      return;
    }
    if (command.kind === 'show-swimmer') {
      if (info.has(command.swimmerId)) showSwimmer(command.swimmerId);
      return;
    }
    onFocusPool?.();
    window.setTimeout(() => {
      document.querySelector(`[data-lane-card="${command.laneNo}"]`)?.scrollIntoView({ behavior: 'smooth', block: 'nearest', inline: 'center' });
    }, 0);
  };
  useEffect(() => {
    setPoolListener((command) => commandRef.current(command));
    return () => setPoolListener(null);
  }, [setPoolListener]);

  /** Publish: дослать правки, затем диалог с сохранённым планом (его и увидят участники). */
  const askPublish = async () => {
    setPublishing(true);
    setError(null);
    const saved = await autosave.flush();
    setPublishing(false);
    if (!saved) return; // запись не удалась — причина в строке ошибки, Publish ждёт
    openSheet({
      kind: 'publish',
      publish: { groupId, date, plan: saved, onPublish: (notify) => onSetStatus(true, notify), onPerson: showSwimmer },
    });
  };

  const unpublish = async () => {
    setPublishing(true);
    setError(null);
    const err = await onSetStatus(false);
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
        label={shortName.get(swimmerId) || s.name || s.name_en}
        fullName={s.name || s.name_en}
        color={circleColor(swimmerId)}
        subLevel={s.sub_level ?? null}
        pinned={pinned.has(swimmerId) && typeof where(swimmerId) === 'number'}
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
        canUnpin={typeof target === 'number' && pinned.has(selected)}
        onUnpin={() => { setPin(selected, false); setSelected(null); }}
        onLane={(no) => move(selected, no)}
        onUnassign={() => move(selected, 'unassigned')}
        onOut={() => move(selected, 'out')}
        onLead={() => typeof target === 'number' && move(selected, target, { first: true })}
        onCard={(() => {
          const person = personOf(selected);
          return person ? () => { setSelected(null); openSheet({ kind: 'person', person }); } : undefined;
        })()}
      />
    );
  };

  /** Пилюли с переносом строки — и в дорожке, и в Unassigned, и в Not today. */
  const chipList = (ids: number[]) => (
    <div className="flex flex-wrap gap-1.5">
      {ids.map((id) => chip(id))}
    </div>
  );

  const dropHere = desktop && (
    <div
      aria-hidden="true"
      className="flex h-[30px] items-center justify-center rounded-[10px] text-[11px] font-bold"
      style={{ border: '1px dashed var(--deep-pool-border)', color: 'var(--deep-pool-label)' }}
    >
      Drop here
    </div>
  );

  const laneCard = (no: number) => {
    const lane = lanes[no - 1];
    const prev = no > 1 ? lanes[no - 2] : null;
    const ids = buckets.lanes[no - 1];
    const over = maxPerLane != null && ids.length > maxPerLane;
    return (
      <section
        key={no}
        data-lane-card={no}
        {...dropProps(no)}
        className={`flex min-w-0 flex-col rounded-[16px] border ${desktop ? 'min-h-[220px] gap-2 p-2.5' : 'gap-2 px-3 py-2.5'}`}
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
            // Десктоп: колонка узкая (8 дорожек в строку) — уровень второй строкой во всю ширину,
            // номер и счётчик над ним.
            className={`min-w-0 cursor-pointer rounded-[8px] border px-2 text-[12px] font-extrabold ${
              desktop ? 'order-last h-[32px] basis-full' : 'h-[34px] flex-1 basis-[120px]'}`}
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
            {maxPerLane != null ? `${ids.length} / ${maxPerLane}` : ids.length}
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

  const unassignedCard = (
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
        In today’s plan, no lane yet. Auto-arrange places them by level.
      </div>
      {unassigned.length > 0 && chipList(unassigned)}
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
          {buckets.out.length > 0 && chipList(buckets.out)}
          {moveRow('out')}
        </div>
      )}
    </div>
  );

  /** «Pool setup»: шторка получает снимок черновика; правки приходят обратно по «Done». */
  const openPoolSetup = () => openSheet({
    kind: 'pool-setup',
    setup: {
      laneCount,
      maxPerLane,
      reservedLanes,
      laneLevels: lanes.map((l) => l.levelId),
      maxLanes: MAX_LANES,
      levels: levels.levels,
      copyFrom,
      canDelete: plan != null,
      onApply: (next) => {
        setLaneCount(next.laneCount);
        setMaxPerLane(next.maxPerLane);
        setReservedLanes(next.reservedLanes);
        setLanes((ls) => ls.map((l, i) => ({ ...l, levelId: next.laneLevels[i] ?? null })));
        setSelected(null);
        setToast(null);
      },
      onCopy: askCopy,
      onDelete,
    },
  });

  // Две кнопки над дорожками (CF2): раскладка и настройка бассейна.
  const toolBtn = `flex-none cursor-pointer rounded-[10px] border bg-transparent px-3 text-[12px] font-extrabold disabled:cursor-not-allowed disabled:opacity-40 ${desktop ? 'h-[36px]' : 'h-[40px]'}`;
  const toolStyle = { borderColor: 'var(--deep-ow-border)', color: 'var(--deep-ow)' };
  const toolbar = (
    <div className="flex flex-wrap items-center gap-2">
      <button
        type="button"
        className={toolBtn}
        style={toolStyle}
        onClick={askAutoLanes}
        disabled={distributing || present().length === 0}
        title="Split the lanes between levels by how many are coming, then place everyone. Reserved lanes stay empty; pinned swimmers stay."
      >
        {distributing ? 'Working…' : 'Auto-arrange'}
      </button>
      <button type="button" className={toolBtn} style={toolStyle} onClick={openPoolSetup} disabled={distributing}>
        Pool setup
      </button>
    </div>
  );

  // Пока первая запись нового плана не дошла, статуса ещё нет — «No lane plan» без кнопки.
  const statusBar = (
    <LanePlanStatus
      status={plan?.status ?? null}
      unassigned={unassigned.length}
      save={autosave.status}
      busy={publishing}
      onRetry={autosave.retry}
      onPublish={() => { void askPublish(); }}
      onUnpublish={() => { void unpublish(); }}
    />
  );
  const writeError = autosave.error ?? error;

  return (
    <div>
      {/* Запись не удалась: правки остались в редакторе, повтор — здесь и в строке статуса. */}
      {writeError && (
        <p className="m-0 mb-2 flex flex-wrap items-center gap-x-2 text-[12px] font-bold" style={{ color: 'var(--deep-danger)' }} role="alert">
          <span>{writeError}</span>
          {autosave.error && (
            <button
              type="button"
              onClick={autosave.retry}
              className="cursor-pointer border-0 bg-transparent p-0 text-[12px] font-extrabold underline"
              style={{ color: 'inherit' }}
            >
              Retry
            </button>
          )}
        </p>
      )}
      {statusHost && createPortal(statusBar, statusHost)}

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

      {/* Дорожки: телефон — столбиком; десктоп — все одной строкой (MIN_COLUMN, иначе прокрутка вбок), Unassigned — под ними. */}
      {!desktop && (
        <div className="mt-3 flex flex-col gap-3">
          {laneNos.map((no) => laneCard(no))}
          {unassignedCard}
        </div>
      )}
      {desktop && (
        <>
          <div className="mt-3 overflow-x-auto pb-1">
            <div
              className="grid items-start gap-2"
              style={{ gridTemplateColumns: `repeat(${laneCount}, minmax(${MIN_COLUMN}px, 1fr))` }}
            >
              {laneNos.map((no) => laneCard(no))}
            </div>
          </div>
          <div className="mt-3">{unassignedCard}</div>
        </>
      )}

      {notToday}

      {statusHost === undefined && <div className="mt-3">{statusBar}</div>}

      <RollToast
        toast={toast}
        onUndo={() => { toast?.restore(); setToast(null); setSelected(null); }}
        onExpire={() => setToast(null)}
      />

      {confirm === 'auto' && (
        <ConfirmDialog
          title="Arrange the lanes again?"
          confirmLabel="Auto-arrange"
          busyLabel="Working…"
          onConfirm={async () => { await autoLanes(); return { success: true }; }}
          onClose={() => setConfirm(null)}
        >
          <p className="m-0 mt-3 text-[13px] text-[var(--t-text-2)]">
            The level of every lane and who swims where are replaced: lanes are split between levels
            by how many are coming, the strongest on the lowest numbers, neighbouring levels share a
            lane when there are too few. Workouts stay on their lanes; “Not today” stays as is.
          </p>
          {reservedLanes > 0 && (
            <p className="m-0 mt-2 text-[13px] text-[var(--t-text-2)]">
              {reservedLanes === 1 ? 'The last lane is' : `The last ${reservedLanes} lanes are`} kept empty in reserve.
            </p>
          )}
          <p className="m-0 mt-2 text-[13px] text-[var(--t-text-2)]">
            Swimmers you moved by hand (📌) stay on their lanes.
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
            drop out. The copy replaces the saved plan straight away.
          </p>
        </ConfirmDialog>
      )}
    </div>
  );
}

export default GroupLaneEditor;
