// Чистые функции личного плана (docs/plans/start-list-ticket-plan.md, шаги Т5–Т7).
// Без React, без fetch, без DOM — по тому же правилу, что `start-list-helpers.ts`:
// эти решения проверяются глазами по коду, а не по экрану.

import type { StartListPlan } from './use-start-list-plan';
import type { StartListSwim, StartListSwimmer } from './types';

/**
 * Состав плана «по умолчанию» — из избранного, когда СВОЕГО плана на это соревнование ещё
 * нет (решение Влада 29.08.2026: «дефолтно избранные клубы, но можно выбрать любой»).
 *
 * ⚠ Отфильтровано по тем, кто на этом старте реально заявлен: показывать в плане пловца,
 * которого нет в протоколе, — значит обещать заплывы, которых не будет.
 *
 * ⚠ Вызывать ТОЛЬКО когда `plan === null`. Сохранённый пустой план — это «я всё снял сам»,
 * и подставлять в него избранных нельзя (см. use-start-list-plan).
 */
export function defaultPlanFromFavorites(
  favoriteSwimmerIds: readonly number[],
  favoriteClubIds: readonly number[],
  enteredSwimmerIds: ReadonlySet<number>,
  enteredClubIds: ReadonlySet<number>,
): StartListPlan {
  return {
    swimmer_ids: favoriteSwimmerIds.filter((id) => enteredSwimmerIds.has(id)),
    club_ids: favoriteClubIds.filter((id) => enteredClubIds.has(id)),
    im_coming: false,
    notify_me: false,
  };
}

/** Что показывать: свой план сильнее дефолта, даже если он пустой. */
export function effectivePlan(plan: StartListPlan | null, fallback: StartListPlan): StartListPlan {
  return plan ?? fallback;
}

/** Пустой ли состав — от этого зависит, есть ли что показывать в карточке. */
export function isEmptyPlan(plan: StartListPlan): boolean {
  return plan.swimmer_ids.length === 0 && plan.club_ids.length === 0;
}

/**
 * Дни, в которые плывёт хоть кто-то из выбранных, — по ним гаснут дни-чипы карточки (Т6)
 * и подписываются строки пикера. Ключ дня — календарная дата `YYYY-MM-DD`: у заплыва есть
 * отдельное поле дня, потому что времени может не быть вовсе.
 */
export function swimmerDays(swimmer: StartListSwimmer | undefined): string[] {
  if (!swimmer) return [];
  const days = new Set(swimmer.swims.map((s) => s.comp_date.slice(0, 10)));
  return [...days].sort();
}

/** Подпись кнопки «Show my plan — 2 swimmers + 1 club». Пустой состав — null. */
export function planSummary(plan: StartListPlan): string | null {
  const parts: string[] = [];
  if (plan.swimmer_ids.length > 0)
    parts.push(`${plan.swimmer_ids.length} ${plan.swimmer_ids.length === 1 ? 'swimmer' : 'swimmers'}`);
  if (plan.club_ids.length > 0)
    parts.push(`${plan.club_ids.length} ${plan.club_ids.length === 1 ? 'club' : 'clubs'}`);
  return parts.length > 0 ? parts.join(' + ') : null;
}

/** Строка плана: заплыв плюс то, ЧЕЙ он — от этого зависит цвет и порядок. */
export interface PlanSwim {
  swim: StartListSwim;
  /** true — заплыв выбранного ПЛОВЦА (золото); false — он попал сюда через клуб (cyan). */
  mine: boolean;
}

/**
 * Все заплывы плана одним списком: выбранные пловцы + выбранные клубы.
 *
 * Два правила, которые нельзя переизобрести:
 *
 * 1. **Один и тот же заплыв не двоится.** Пловец может быть выбран лично И входить в
 *    выбранный клуб; ключ — `id` заявки, и личный выбор перебивает клубный (строка
 *    остаётся «моей», золотой).
 * 2. **Порядок — по времени, а заплывы без времени в хвост.** Полночь в источнике значит
 *    «время не назначено» (footgun плана §1.4.2), и такие строки не должны всплывать
 *    в начало дня перед реальными стартами.
 */
export function assemblePlanSwims(
  swimmers: Record<number, StartListSwimmer>,
  clubSwims: readonly StartListSwim[],
  selectedSwimmerIds: readonly number[],
): PlanSwim[] {
  const byId = new Map<number, PlanSwim>();

  for (const swim of clubSwims) byId.set(swim.id, { swim, mine: false });
  for (const id of selectedSwimmerIds) {
    for (const swim of swimmers[id]?.swims ?? []) byId.set(swim.id, { swim, mine: true });
  }

  return [...byId.values()].sort((a, b) => {
    const at = a.swim.heat_start_at ? Date.parse(a.swim.heat_start_at) : Number.MAX_SAFE_INTEGER;
    const bt = b.swim.heat_start_at ? Date.parse(b.swim.heat_start_at) : Number.MAX_SAFE_INTEGER;
    if (at !== bt) return at - bt;
    return (a.swim.event_number ?? Number.MAX_SAFE_INTEGER) - (b.swim.event_number ?? Number.MAX_SAFE_INTEGER);
  });
}

/** День плана для чипов: дата, сколько заплывов и плывёт ли в него кто-то из выбранных. */
export interface PlanDay {
  /** `YYYY-MM-DD` — календарный день, как `comp_date` заявки. */
  date: string;
  swims: number;
}

/**
 * Дни программы с числом заплывов ПЛАНА в каждом. Дни берутся из самой программы, а не из
 * плана: чип пустого дня должен быть виден (погашенным) — иначе непонятно, что день вообще
 * есть, и «а когда же ещё» остаётся без ответа.
 */
export function planDays(programmeDays: readonly string[], swims: readonly PlanSwim[]): PlanDay[] {
  const byDay = new Map<string, PlanSwim[]>();
  for (const s of swims) {
    const day = s.swim.comp_date.slice(0, 10);
    (byDay.get(day) ?? byDay.set(day, []).get(day)!).push(s);
  }
  // Считаем СТРОКИ, а не заявки: ноги эстафеты и несколько выбранных в одном заплыве
  // складываются в одну строку (groupPlanRows), и «183 swims» на чипе спорило бы с
  // полутора сотнями строк под ним.
  return programmeDays.map((date) => ({
    date,
    swims: groupPlanRows(byDay.get(date) ?? []).length,
  }));
}

/**
 * Сколько СТРОК плана приходится на каждую сессию (протокол) — счётчик чипа сессии в зоне
 * фильтров 5d. Считаем строки, а не заявки, по той же причине, что и в `planDays`: ноги
 * эстафеты и несколько выбранных в одном заплыве складываются в одну строку, и число на
 * чипе спорило бы с тем, что видно под ним.
 */
export function planRowsBySession(swims: readonly PlanSwim[]): Map<number, number> {
  const by = new Map<number, number>();
  for (const row of groupPlanRows(swims)) {
    by.set(row.orgCompId, (by.get(row.orgCompId) ?? 0) + 1);
  }
  return by;
}

/** Первый старт плана в этот день — из него строится hero карточки (и ARRIVE BY в Т8). */
export function firstSwimOfDay(swims: readonly PlanSwim[], day: string): PlanSwim | null {
  return swims.find((s) => s.swim.comp_date.slice(0, 10) === day && s.swim.heat_start_at) ?? null;
}

/** Один участник строки D3: имя и его дорожка. У эстафеты — команда, а не отдельная нога. */
export interface PlanRowEntry {
  /** Ключ для React: id заявки (у эстафеты — id первой ноги команды). */
  id: number;
  /** Имя пловца либо название команды-эстафеты (иврит, показывается через dir="auto"). */
  name: string;
  lane: number;
  /** true — это выбранный пловец (золото), false — попал через клуб (cyan). */
  mine: boolean;
  isRelay: boolean;
  /** entered | swum | no-show. */
  status: string;
  /** Посев этой заявки; null — «NT». */
  seedTime: string | null;
  /**
   * Состав команды-эстафеты: все её ноги, отсортированные по id заявки (решение Влада
   * 30.08.2026, «вариант A» — состав виден без нажатий).
   *
   * Пусто у личного заплыва И у эстафеты, про которую источник состава не дал: следя за
   * ОДНИМ пловцом, клиент получает только его ногу (`/swimmers/{id}`), а не всю четвёрку —
   * четыре имени приходят, когда следят за клубом (`/{comp}/clubs/{id}`). Показывать список
   * из одного человека нельзя: он читался бы как «команда из одного».
   *
   * Порядок — по id, а не «нога 1..4»: колонки порядка ног в схеме нет (`CompetitionEntry`),
   * id лишь отражает порядок затягивания протокола. Поэтому имена идут перечислением, БЕЗ
   * нумерации: печатать «leg 3» значило бы утверждать то, чего мы не знаем.
   */
  members: PlanRowMember[];
}

/** Одна нога команды-эстафеты в составе строки. */
export interface PlanRowMember {
  id: number;
  name: string;
  /** true — это пловец из состава плана; он выделяется в перечислении. */
  mine: boolean;
}

/** Строка D3: один заплыв — одна строка, сколько бы выбранных в нём ни плыло. */
export interface PlanRow {
  key: string;
  orgCompId: number;
  orgDisciplineId: number;
  heat: number;
  /** UTC; null — время заплыву не назначено (полночь в источнике). */
  startAt: string | null;
  distance: string;
  styleName: string;
  gender: string;
  ageBand: string | null;
  isRelay: boolean;
  /** Хоть один «мой» в строке — строка золотая. */
  mine: boolean;
  entries: PlanRowEntry[];
}

/** «Girls 9», «Boys 8-9», «Mixed» — категория по-английски. Ивритский `event_category`
 *  источника наружу не идёт: правило «интерфейсные строки только на английском». */
export function bandLabel(gender: string, ageBand: string | null): string {
  const who = gender === 'female' ? 'Girls' : gender === 'male' ? 'Boys' : 'Mixed';
  return ageBand ? `${who} ${ageBand}` : who;
}

/**
 * Группировка заплывов плана в строки D3.
 *
 * Два склеивания, и они РАЗНЫЕ:
 *
 * 1. **Ноги эстафеты → одна команда.** У эстафеты четыре строки с одинаковыми
 *    заплывом+дорожкой и разными пловцами — это одна команда (тот же приём, что
 *    `mergeRelayLanes` в зуме 2). Состав команды сохраняется в `members`, и выбранный
 *    пловец в нём выделяется — иначе непонятно, где в ней «мой».
 * 2. **Несколько выбранных в ОДНОМ заплыве → одна строка** (правило хендоффа §1.3):
 *    время и дисциплина общие, имена идут столбиком, у каждого своя дорожка. Ключ —
 *    `compID + дисциплина + заплыв`: номера дисциплин у разных протоколов совпадают.
 */
export function groupPlanRows(swims: readonly PlanSwim[]): PlanRow[] {
  const rows = new Map<string, PlanRow>();
  // Ноги одной команды: ключ заплыв+дорожка+клуб, как в mergeRelayLanes.
  //
  // Номера ног («leg 3») здесь СОЗНАТЕЛЬНО не считаются: порядка ног в схеме нет
  // (`CompetitionEntry` без колонки порядка), а счёт по приходу строк давал ложь — в срезе
  // клуба ноги приезжают в произвольном порядке, а следя за одним пловцом клиент видит
  // только его ногу и всегда получал «leg 1». Ответ на «где мой в этой четвёрке» даёт
  // выделение его имени в составе (`members`), а не выдуманный номер.
  const teams = new Map<string, { entry: PlanRowEntry }>();

  for (const { swim, mine } of swims) {
    const rowKey = `${swim.org_comp_id}:${swim.org_discipline_id}:${swim.heat}`;
    let row = rows.get(rowKey);
    if (!row) {
      row = {
        key: rowKey,
        orgCompId: swim.org_comp_id,
        orgDisciplineId: swim.org_discipline_id,
        heat: swim.heat,
        startAt: swim.heat_start_at,
        distance: swim.distance,
        styleName: swim.style_name,
        gender: swim.gender,
        ageBand: swim.age_band,
        isRelay: swim.is_relay,
        mine: false,
        entries: [],
      };
      rows.set(rowKey, row);
    }
    row.mine = row.mine || mine;

    if (!swim.is_relay) {
      row.entries.push({
        id: swim.id,
        name: swim.swimmer_name,
        lane: swim.lane,
        mine,
        isRelay: false,
        status: swim.status,
        seedTime: swim.seed_time,
        members: [],
      });
      continue;
    }

    const teamKey = `${rowKey}:${swim.lane}:${swim.club_id}`;
    const team = teams.get(teamKey);
    if (team) {
      team.entry.mine = team.entry.mine || mine;
      team.entry.members.push({ id: swim.id, name: swim.swimmer_name, mine });
      continue;
    }

    const entry: PlanRowEntry = {
      id: swim.id,
      name: swim.club_name,
      lane: swim.lane,
      mine,
      isRelay: true,
      status: swim.status,
      seedTime: swim.seed_time,
      // Имя ноги, а не команды: в `name` у эстафеты стоит клуб, состав живёт отдельно.
      members: [{ id: swim.id, name: swim.swimmer_name, mine }],
    };
    teams.set(teamKey, { entry });
    row.entries.push(entry);
  }

  // Состав команды — по id заявки: источник отдаёт ноги в произвольном порядке (сортировка
  // API идёт по времени/событию/дорожке, а внутри дорожки ноги равны). Порядок протокола
  // отражает только порядок затягивания, то есть id; он же даёт устойчивую выдачу.
  for (const { entry } of teams.values()) {
    entry.members.sort((a, b) => a.id - b.id);
  }

  // Внутри строки: сперва «мои», потом по дорожке — свой ребёнок должен читаться первым.
  for (const row of rows.values()) {
    row.entries.sort((a, b) => Number(b.mine) - Number(a.mine) || a.lane - b.lane);
  }
  return [...rows.values()];
}

/**
 * Состав плана строкой для адреса: «s10,s42,c506» (шаг Т10).
 *
 * Зачем свой формат, а не JSON в query: ссылку пересылают в родительский чат и она должна
 * читаться человеком и переживать копирование из мессенджера — экранированный JSON в
 * адресной строке этого не переживает.
 */
export function serializePlanParam(plan: StartListPlan): string {
  return [
    ...plan.swimmer_ids.map((id) => `s${id}`),
    ...plan.club_ids.map((id) => `c${id}`),
  ].join(',');
}

/**
 * Разбор того же параметра. Мусор и неизвестные префиксы молча отбрасываем: ссылка живёт
 * дольше, чем состав (пловца могли снять со старта), и падать на этом она не должна.
 * null — параметра нет или в нём не осталось ничего осмысленного.
 */
export function parsePlanParam(raw: string | null | undefined): StartListPlan | null {
  if (!raw) return null;
  const swimmer_ids: number[] = [];
  const club_ids: number[] = [];

  for (const token of raw.split(',')) {
    const value = Number(token.slice(1));
    if (!Number.isFinite(value) || value <= 0) continue;
    if (token[0] === 's') swimmer_ids.push(value);
    else if (token[0] === 'c') club_ids.push(value);
  }

  if (swimmer_ids.length === 0 && club_ids.length === 0) return null;
  // Галочки в ссылку не едут: «I'm coming» и «Notify me» — это про ОТПРАВИТЕЛЯ, а не про
  // того, кто открыл ссылку.
  return { swimmer_ids, club_ids, im_coming: false, notify_me: false };
}
