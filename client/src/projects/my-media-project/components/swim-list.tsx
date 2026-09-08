import React, { useEffect, useMemo, useState } from 'react';
import { UserMediaPublicationDto } from '../../../hooks/useUserMedia';
import { fetchPublishTargets, PublishTargetDto } from '../use-all-my-media';
import { MySwimDto, SwimMediaDto } from '../use-my-swims';
import { STATUS_COLORS, CardStatus, derivedCardStatus, visibilityLabel, hpCardCls } from './status-styles';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SeasonBestBadge from '../../components/mix/season-best-badge/season-best-badge';
import HelperSwimmer from '../../../utils/helpers/helper-swimmer';
import Helper from '../../../utils/helpers/data-helper';
import UI_RecordBadge, { type RecordKind } from '../../components/mix/record-badge/record-badge';
import CompetitionTile from '../../results-main-project/components/competition-header/competition-tile';
import { competitionTileData } from '../../../utils/helpers/competition-source';

// Список заплывов, сгруппированный по соревнованиям — ядро My media.
// Строка узкая на обеих ширинах, а всё управление медиа (share, withdraw, delete, ❤)
// живёт в ОДНОЙ разворачиваемой панели под строкой — и на десктопе, и на телефоне
// (решение Влада 08.09.2026; своей мобильной шторки действий у строки больше нет).

export interface SwimListCallbacks {
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  onPlay: (media: SwimMediaDto) => void;
  onAddVideo: (swim: MySwimDto) => void;
  onAddCompMedia: (competitionId: number, competitionName: string) => void;
  onSubmitShare: (mediaId: number, hubGroupId: number, level: 'members' | 'public') => Promise<boolean>;
  onWithdraw: (mediaId: number, hubGroupId: number) => void;
  onDelete: (mediaId: number) => void;
  onToggleLike: (media: SwimMediaDto) => void;
  onToggleCheer: (swim: MySwimDto) => void;
}

interface Props extends SwimListCallbacks {
  swims: MySwimDto[];
  competitionMedia: SwimMediaDto[];
  /** Показывать имя пловца в строке (фильтр = All). */
  showSwimmerName: boolean;
  /** id → имя (из response.swimmers), в порядке избранного: primary первым. */
  swimmerNames: Map<number, string>;
  /** Выбранный чипом пловец — его имя и показываем на строках, которые ему принадлежат. */
  preferredSwimmerId?: number | null;
}

/* ── Хелперы ─────────────────────────────────────────────────────────────── */

/**
 * Дата соревнования приходит как dd/MM/yyyy, день заплыва — yyyy-MM-dd. Совпали → колонку
 * DATE не рисуем: она дублирует дату в шапке группы. Не совпали (многодневка) — рисуем,
 * иначе день заплыва потерялся бы.
 */
function sameDay(competitionDate: string, swimDate: string): boolean {
  const [d, m, y] = competitionDate.split('/');
  return y != null && `${y}-${m}-${d}` === swimDate;
}

/**
 * Метка достижения строки. Словарь и приоритет — ОБЩИЕ для продукта, здесь ничего своего:
 *  • `SB` — «быстрейший в стране в этом сезоне на своей ступени» (пол × возраст в сезоне ×
 *    стиль × дистанция × бассейн, порог peers>=2). Рисует общий `UI_SeasonBestBadge`;
 *  • `PB` — личный рекорд пловца за всё время на дистанции;
 *  • одна строка носит ОДИН чип, SB замещает PB — тот же приоритет, что у `SwimRowBadge`
 *    в `components/swim-row` («SB сильнее BEST и ЗАМЕЩАЕТ его»).
 * Рекордов (WR/NR/REC·AGE/REC·M, `UI_RecordBadge`) в этом агрегате пока нет; когда появятся —
 * они старше SB, вставлять их надо сюда же, а не рядом.
 */
const PB_CHIP =
  'inline-flex items-center rounded-full border border-[var(--t-accent-dim)] '
  + 'bg-[var(--t-accent-soft)] px-2 py-[1px] text-[10px] font-black leading-[1.4] text-[var(--t-accent)]';

function BestMark({ record = null, pb, sb, stacked = false }: {
  record?: { kind: RecordKind; scope?: string | null } | null;
  pb: boolean;
  sb: boolean;
  stacked?: boolean;
}) {
  const chip = record
    ? <UI_RecordBadge kind={record.kind} scope={record.scope} isNew />
    : sb
      ? <UI_SeasonBestBadge />
      : pb ? <span className={PB_CHIP} title="Personal best">PB</span> : null;
  if (chip == null) return null;
  return stacked ? <span className="mt-[3px] block">{chip}</span> : chip;
}

/**
 * Имя пловца в строке. У ЭСТАФЕТЫ владелец строки — одна нога, и он запросто не из
 * избранного: тогда в строке стоял «?», хотя строка попала в выдачу как раз потому, что
 * избранный плыл другую ногу. Кому принадлежит заплыв — решает канон-хелпер
 * `HelperSwimmer.resultBelongsToSwimmer` (docs/relays.md: свой матчинг по `swimmer_id`
 * заводить нельзя), поэтому имя ищем среди избранных: сперва выбранный чипом пловец,
 * иначе первый подходящий (карта идёт в порядке избранного, primary первым).
 */
function rowSwimmerName(
  swim: MySwimDto, names: Map<number, string>, preferredId: number | null,
): string {
  if (preferredId != null && HelperSwimmer.resultBelongsToSwimmer(swim, preferredId)) {
    return names.get(preferredId) ?? '?';
  }
  for (const [id, name] of names) {
    if (HelperSwimmer.resultBelongsToSwimmer(swim, id)) return name;
  }
  return '?';
}

/**
 * Шеврон кнопки «развернуть медиа». Инлайновый SVG, а не `UI_*`: готовой иконки-стрелки в
 * реестре нет (docs/ui-components.md §6, тот же случай, что у кнопки «наверх» в стартовом
 * протоколе). Раньше тут стоял глиф ▾ в 11px — его было почти не видно.
 */
function Chevron({ open }: { open: boolean }) {
  return (
    <svg
      width="14" height="14" viewBox="0 0 24 24" fill="none" aria-hidden="true"
      style={{ transform: open ? 'rotate(180deg)' : undefined, transition: 'transform 120ms' }}
    >
      <path d="M6 9l6 6 6-6" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

function medal(place: number | null): string | null {
  return place === 1 ? '🥇' : place === 2 ? '🥈' : place === 3 ? '🥉' : null;
}

/** Style.Name из БД сырой (freestyle / individual_medley) — короткие лейблы дизайна. */
const STYLE_LABELS: Record<string, string> = {
  freestyle: 'free', backstroke: 'back', breaststroke: 'breast',
  butterfly: 'fly', individual_medley: 'IM', medley: 'IM',
};
export function styleLabel(style: string): string {
  return STYLE_LABELS[style.toLowerCase()] ?? style.replace(/_/g, ' ');
}

function mediaStatus(m: SwimMediaDto, pubs: UserMediaPublicationDto[]): { status: CardStatus; isPublic: boolean } {
  const status = derivedCardStatus(pubs);
  const isPublic = pubs.some((p) => p.status === 'approved' && p.level === 'public');
  return { status, isPublic };
}

function pillTitle(pubs: UserMediaPublicationDto[]): string {
  if (pubs.length === 0) return 'Private — only you can see this';
  return pubs
    .map((p) => `${p.hub_group_name}: ${p.status} · ${p.level === 'public' ? 'everyone' : 'members'}`)
    .join('\n');
}

/**
 * Видимость всего заплыва на свёрнутой строке: две строки вместо одной длинной —
 * колонка MEDIA всего 330px, а «members of דולפין נתניה מסטרס» в одну строку её съедает.
 * Считается по ВСЕМ медиа заплыва (у видео и фото публикации могут различаться),
 * подробности по каждой группе — в title.
 */
function RowVisibility({ swim, publicationsByMedia }: {
  swim: MySwimDto;
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  /** Мобильная строка: одна строка с обрезкой — там колонка узкая, а высота дороже ширины. */
}) {
  const pubs = swim.media.flatMap((m) => publicationsByMedia.get(m.id) ?? []);
  const status = derivedCardStatus(pubs);
  const isPublic = pubs.some((p) => p.status === 'approved' && p.level === 'public');
  const groups = Array.from(new Map(pubs.map((p) => [p.hub_group_id, p.hub_group_name])).values());
  const c = STATUS_COLORS[status];

  const head =
    status === 'private' ? 'private'
      : status === 'published' ? (isPublic ? 'everyone in' : 'members of')
        : status === 'pending' ? 'pending in' : 'rejected in';
  const tail = groups.length === 0 ? 'only you' : groups.length === 1 ? groups[0] : `${groups.length} groups`;


  return (
    <span
      title={pillTitle(pubs)}
      className="hp-mono inline-block max-w-[142px] overflow-hidden rounded-[6px] px-[7px] py-[2px] text-[9.5px] font-extrabold leading-[1.3]"
      style={{ color: c.text, border: `${isPublic ? '1.5px' : '1px'} solid ${c.border}`, background: c.bg }}
    >
      <span className="block">{head}{isPublic ? ' 🌐' : ''}</span>
      <span dir="auto" className="block truncate opacity-80">{tail}</span>
    </span>
  );
}

function StatusPill({ status, isPublic, pubs }: { status: CardStatus; isPublic: boolean; pubs: UserMediaPublicationDto[] }) {
  const c = STATUS_COLORS[status];
  return (
    <span
      title={pillTitle(pubs)}
      className="hp-mono inline-flex items-center gap-1 rounded-[6px] px-[8px] py-[2px] text-[10.5px] font-extrabold"
      style={{ color: c.text, border: `${isPublic ? '1.5px' : '1px'} solid ${c.border}`, background: c.bg }}
    >
      {visibilityLabel(status, isPublic)}{isPublic ? ' 🌐' : ''}
    </span>
  );
}

function SourceChip({ m, onClick }: { m: SwimMediaDto; onClick: () => void }) {
  const label = m.media_type === 'image' ? '🖼 PHOTO' : `▶ ${m.source_type.toUpperCase()}`;
  return (
    <button
      type="button"
      onClick={onClick}
      className="hp-mono w-[120px] shrink-0 rounded-[7px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-2 py-[4px] text-left text-[10.5px] font-extrabold text-[var(--t-accent)]"
      title={m.media_type === 'image' ? 'Open photo' : 'Play'}
    >
      {label}
    </button>
  );
}

function LikeChip({ m, onToggle }: { m: SwimMediaDto; onToggle: () => void }) {
  const liked = m.my_like;
  return (
    <button
      type="button"
      onClick={onToggle}
      className="hp-mono rounded-[7px] px-2 py-[3px] text-[10.5px] font-extrabold"
      style={{
        border: `1px solid ${liked ? 'var(--t-like)' : 'var(--t-border)'}`,
        background: liked ? 'var(--t-like-soft)' : 'transparent',
        color: liked ? 'var(--t-like)' : m.likes_count > 0 ? 'var(--t-text-2)' : 'var(--t-text-3)',
      }}
      title={liked ? 'Remove like' : 'Like'}
    >
      ❤ {m.likes_count}
    </button>
  );
}

function CheerChip({ swim, emphasized, onToggle }: {
  swim: MySwimDto; emphasized: boolean; onToggle: () => void;
}) {
  const on = swim.my_cheer;
  return (
    <button
      type="button"
      onClick={onToggle}
      className="hp-mono whitespace-nowrap rounded-[7px] px-2 py-[3px] text-[10.5px] font-extrabold"
      style={{
        border: `1px solid ${on ? 'var(--t-warn)' : emphasized ? 'var(--t-warn-border)' : 'var(--t-border)'}`,
        background: on ? 'var(--t-warn-soft)' : 'transparent',
        color: on ? 'var(--t-warn)' : emphasized ? 'var(--t-warn)' : swim.congrats_count > 0 ? 'var(--t-text-2)' : 'var(--t-text-3)',
      }}
      title={on ? 'Remove congrats' : 'Congratulate'}
    >
      🎉 {swim.congrats_count}
    </button>
  );
}

/* ── Media line (развёрнутая панель): source · status · inline share · actions ── */

function MediaLine({
  m, pubs, cb,
}: {
  m: SwimMediaDto;
  pubs: UserMediaPublicationDto[];
  cb: SwimListCallbacks;
}) {
  const { status, isPublic } = mediaStatus(m, pubs);
  // Действующая публикация (первая из pending/approved) — она же начальное значение селектов.
  const active = pubs.find((p) => p.status === 'pending' || p.status === 'approved') ?? null;
  const [targets, setTargets] = useState<PublishTargetDto[] | null>(null);
  const [group, setGroup] = useState<number | ''>(active ? active.hub_group_id : '');
  const [level, setLevel] = useState<'members' | 'public'>(active ? active.level : 'members');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    fetchPublishTargets(m.id).then((t) => { if (alive) setTargets(t); });
    return () => { alive = false; };
  }, [m.id]);

  // Публикации приходят асинхронно и меняются после share/withdraw — возвращаем селекты
  // к фактическому состоянию, но только когда оно реально сменилось (иначе затрём выбор юзера).
  const activeKey = active ? `${active.hub_group_id}:${active.level}` : '';
  useEffect(() => {
    setGroup(active ? active.hub_group_id : '');
    setLevel(active ? active.level : 'members');
  }, [activeKey]); // eslint-disable-line react-hooks/exhaustive-deps

  // Группа могла выпасть из publish-targets (пловца убрали из ростера), а публикация осталась —
  // без этого селект показал бы пустоту вместо своей же группы.
  const options = useMemo(() => {
    const list = targets ?? [];
    if (active && !list.some((t) => t.id === active.hub_group_id)) {
      return [...list, { id: active.hub_group_id, name: active.hub_group_name }];
    }
    return list;
  }, [targets, active]);

  // Сервер отвергает повторную подачу в ту же группу («publication already exists»),
  // смена уровня идёт через withdraw+резаявку в onSubmitShare — здесь только гасим кнопку.
  const current = group === '' ? null : pubs.find((p) => p.hub_group_id === group && (p.status === 'pending' || p.status === 'approved')) ?? null;
  const unchanged = current != null && current.level === level;

  const share = async () => {
    if (group === '' || busy || unchanged) return;
    setBusy(true);
    await cb.onSubmitShare(m.id, group, level);
    setBusy(false);
  };

  const withdrawable = pubs.filter((p) => p.status === 'pending' || p.status === 'approved');

  return (
    <div className="flex flex-wrap items-center gap-2 py-[6px]">
      <SourceChip m={m} onClick={() => cb.onPlay(m)} />
      <span className="w-[120px] shrink-0"><StatusPill status={status} isPublic={isPublic} pubs={pubs} /></span>
      <LikeChip m={m} onToggle={() => cb.onToggleLike(m)} />
      <div className="ml-auto flex flex-wrap items-center gap-1.5">
        {targets != null && options.length > 0 && (
          <>
            <select
              value={group}
              onChange={(e) => setGroup(e.target.value === '' ? '' : Number(e.target.value))}
              className="rounded-[7px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-1.5 py-[3px] text-[11px] text-[var(--t-text)]"
            >
              <option value="">Group…</option>
              {options.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
            <select
              value={level}
              onChange={(e) => setLevel(e.target.value as 'members' | 'public')}
              className="rounded-[7px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-1.5 py-[3px] text-[11px] text-[var(--t-text)]"
            >
              <option value="members">Members</option>
              <option value="public">Everyone 🌐</option>
            </select>
            <button
              type="button"
              disabled={group === '' || busy || unchanged}
              onClick={share}
              title={unchanged ? 'Already shared with this group at this level' : undefined}
              className="hp-mono rounded-[7px] border-none px-2.5 py-[4px] text-[10.5px] font-extrabold disabled:opacity-40"
              style={{ background: 'var(--t-accent)', color: 'var(--t-accent-ink)' }}
            >
              {current ? 'Update' : 'Share'}
            </button>
          </>
        )}
        {withdrawable.map((p) => (
          <button
            key={p.hub_group_id}
            type="button"
            onClick={() => cb.onWithdraw(m.id, p.hub_group_id)}
            className="hp-mono rounded-[7px] border border-[var(--t-warn-border)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[var(--t-warn)]"
            title={`Withdraw from ${p.hub_group_name}`}
          >
            Withdraw
          </button>
        ))}
        <button
          type="button"
          onClick={() => cb.onDelete(m.id)}
          className="hp-mono rounded-[7px] border border-[var(--t-danger-border)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[var(--t-danger)]"
        >
          Delete
        </button>
      </div>
    </div>
  );
}

/* ── Строка заплыва My media ──────────────────────────────────────────────────── */

/**
 * Это НЕ общая строка заплыва `SwimRow` (`components/swim-row/`). Раньше она звалась
 * так же и читалась как шестая копия той же строки — поэтому переименована.
 *
 * Почему не сведена в общую: общая строка — двухлинейная КАРТОЧКА результата, а здесь —
 * ПЛОТНАЯ ТАБЛИЦА управления медиа: сетка `48 28 66 1fr 72 110 120` под своей шапкой
 * (PLACE / SWIM / TIME / MEDIA) и разворачиваемая панель медиа под строкой. Карточка втрое
 * выше и ломает выравнивание по колонкам, а чтобы вместить медиа-кнопки, RELAY, метку PB и
 * тап-по-строке, в общий компонент пришлось бы добавить слот на каждый угол — ровно то, от
 * чего план общей строки отказался (§3.1 `docs/plans/swim-row-shared-component-plan.md`).
 *
 * В строке — только короткое и главное (решение Влада 07.09.2026): бейдж «кому это видно»,
 * поздравления соседей и управление публикациями живут в раскрывающейся панели, а на
 * мобильной — в нижней шторке действий. Сетка задана в `my-media.css` (`.mms-row`,
 * `.mms-mrow`), и шапка колонок берёт её же — разъехаться они не могут.
 *
 * Общее берётся ячейками: `UI_SwimmStyleIcon`, `UI_SwimTime` вместе с
 * `swimFlaggedRowProps` (носитель спорного времени) и `UI_DateIcon` (формат даты один на продукт).
 */
function MySwimRow({ swim, showSwimmerName, showDate, showCheers, swimmerName, cb }: {
  swim: MySwimDto;
  showSwimmerName: boolean;
  /** Колонка DATE — только у многодневок (см. `sameDay`). */
  showDate: boolean;
  /** Колонка 🎉 — только если в карточке кого-то уже поздравили (см. `CompetitionGroup`). */
  showCheers: boolean;
  swimmerName: string;
  cb: SwimListCallbacks;
}) {
  const [expanded, setExpanded] = useState(false);
  const videos = swim.media.filter((m) => m.media_type === 'video');
  const photos = swim.media.filter((m) => m.media_type === 'image');
  const hasMedia = swim.media.length > 0;
  const noVideo = videos.length === 0;
  // ❤ в строке — сводка: самое залайканное медиа заплыва. Ноль не показываем: пустое
  // сердечко в каждой строке читается как «никому не понравилось», а не как «ещё нет оценок».
  const topLiked = swim.media.reduce<SwimMediaDto | null>(
    (best, m) => (m.likes_count > 0 && (!best || m.likes_count > best.likes_count) ? m : best),
    null,
  );

  // Спорное время (И11): чип рисует `UI_SwimTime`, а НОСИТЕЛЬ — сама строка:
  // caution-лента слева плюс полный текст в title/aria-label. До этого строка My media
  // обвязки не несла — ровно тот случай, ради которого хелпер и заводили.
  const quality = swim.suspect_reason ? { kind: 'protocol' as const, reason: swim.suspect_reason } : null;
  const flagged = swimFlaggedRowProps(quality);

  // Бейдж рекорда — тем же способом, что в протоколе: справочник рекордов + ось возраста из
  // админ-настройки (`Helper.recordStepAge`), своего поиска по справочнику тут нет.
  // Эстафеты, DSQ и помеченные ошибки протокола рекордов не носят — как в таблице результатов.
  const isMastersResult = Helper.isResultMasters(swim.category === 'masters', swim.event_style_age);
  const recordMark = swim.is_relay || swim.time_fail || swim.suspect_reason || !swim.gender
    ? null
    : Helper.recordMarkForTime({
      time: swim.time,
      gender: swim.gender,
      poolType: swim.pool_type,
      styleName: swim.style,
      distance: `${swim.distance}m`,
      age: Helper.recordStepAge({
        date: swim.competition_date,
        birth_year: swim.birth_year,
        event_style_age: swim.event_style_age,
      }),
      isMasters: isMastersResult,
    });

  return (
    <>
      {/* Desktop row — сетка хендоффа `48 28 66 1fr 72 110 120` (Ф6). В строке только
          короткое и главное (решение Влада 07.09.2026): место, медаль, дисциплина, кто,
          метка, время и ОДИН чип медиа. Бейдж «кому это видно» и управление публикациями
          уехали в раскрывающуюся панель — она и есть кнопка «открыть/закрыть». */}
      <div
        {...flagged}
        className={`mms-row${showDate ? ' mms-row--dated' : ''}${showCheers ? ' mms-row--cheers' : ''} hidden px-5 py-[10px] sm:grid${flagged.className ? ` ${flagged.className}` : ''}`}
        style={{ background: noVideo ? 'var(--t-input-bg)' : 'transparent' }}
      >
        <span className="text-center text-[17px] font-black leading-none">
          {swim.place != null ? `#${swim.place}` : '—'}
        </span>
        <span className="text-center text-[15px] leading-none">{medal(swim.place)}</span>
        <UI_SwimmStyleIcon
          styleName={swim.style}
          styleLen={swim.distance}
          styleType="icon-len"
          lenPlacement="below"
          size={64}
          className="src-swim-list rounded-[8px] bg-[var(--t-plate)] px-1 py-0.5"
        />
        {/* Средняя колонка тянется. Показан один пловец — имени в строке нет (оно в шапке
            карточки), и колонку занимает название дисциплины: пустая тянущаяся колонка
            разрывала бы строку пополам. */}
        <span className="flex min-w-0 items-center gap-2 overflow-hidden">
          {showSwimmerName ? (
            // text-left обязателен: dir="auto" у ивритского имени тянет выравнивание вправо,
            // и имена прыгали бы между краями колонки от пловца к пловцу.
            <span dir="auto" className="min-w-0 flex-1 truncate text-left text-[19px] font-black text-[var(--t-text)]">
              {swimmerName}
            </span>
          ) : (
            <span className="min-w-0 flex-1 truncate text-left text-[13.5px] font-extrabold text-[var(--t-text-2)]">
              {swim.distance}m {styleLabel(swim.style)}
            </span>
          )}
          {swim.is_relay && (
            <span className="hp-mono shrink-0 rounded-[5px] border border-[var(--t-accent-border)] px-1.5 py-[1px] text-[9px] font-extrabold text-[var(--t-accent)]">RELAY</span>
          )}
        </span>
        {/* Метка достижения — своей колонкой, а не под медалью: рекорд и PB это про ВРЕМЯ,
            и стоять им положено рядом с ним. */}
        <span className="flex items-center justify-end">
          <BestMark record={recordMark} pb={swim.is_pb} sb={swim.is_sb} />
        </span>
        <span className="hp-mono text-[15px] font-extrabold text-[var(--t-accent)]">
          {swim.time_fail ? 'DSQ' : (
            <UI_SwimTime time={swim.time} quality={quality} />
          )}
        </span>
        {/* Дата — общим `UI_DateIcon`, а не сырой ISO-строкой из API: формат даты живёт
            в одном месте, а «2026-07-30» здесь спорило с «30 JUL 2026» на всех остальных
            экранах. Колонка есть только у многодневок (см. `sameDay`). */}
        {showDate && (
          <span>
            <UI_DateIcon
              styleType="row-style-1"
              date={swim.date}
              fontClassName="hp-mono text-[10.5px] text-[var(--t-text-3)]"
            />
          </span>
        )}
        {/* Поздравления — СВОЯ колонка: 🎉 про заплыв, а не про медиа, и в медиа-ячейке
            читалось как оценка ролика. Колонка есть только там, где кого-то поздравили. */}
        {showCheers && (
          <span className="flex items-center justify-end">
            <CheerChip swim={swim} emphasized={swim.is_pb} onToggle={() => cb.onToggleCheer(swim)} />
          </span>
        )}
        {/* MEDIA — один чип. У заплыва с медиа он же и раскрывает панель, поэтому отдельной
            кнопки «Manage» больше нет: две кнопки об одном занимали треть строки. */}
        <span className="flex items-center justify-end gap-1.5">
          {hasMedia ? (
            <button
              type="button"
              onClick={() => setExpanded((v) => !v)}
              aria-expanded={expanded}
              title={expanded ? 'Hide media panel' : 'Share, withdraw, delete this media'}
              className="hp-mono inline-flex h-[26px] shrink-0 items-center gap-1 rounded-[8px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-2 text-[10.5px] font-extrabold text-[var(--t-accent)]"
            >
              {videos.length > 0 ? `▶ ${videos.length}` : `🖼 ${photos.length}`}
              <Chevron open={expanded} />
            </button>
          ) : (
            <button
              type="button"
              onClick={() => cb.onAddVideo(swim)}
              className="hp-mono shrink-0 rounded-[8px] border border-dashed border-[var(--t-accent-border)] bg-transparent px-2 py-[4px] text-[10.5px] font-extrabold text-[var(--t-accent)]"
            >
              + Add video
            </button>
          )}
          {topLiked && <LikeChip m={topLiked} onToggle={() => cb.onToggleLike(topLiked)} />}
        </span>
      </div>

      {/* Mobile row — сетка хендоффа `34 58 1fr auto` (Ф6). Тап по строке открывает
          нижнюю шторку действий: бейдж «кому видно», поздравления и управление медиа живут
          там, а в строке остаётся только короткое. */}
      <div
        {...flagged}
        className={`mms-mrow grid px-4 py-[10px] sm:hidden${flagged.className ? ` ${flagged.className}` : ''}`}
        style={{ background: noVideo ? 'var(--t-input-bg)' : 'transparent' }}
      >
        {/* Место, медаль и метка — ОДНИМ столбиком: на узком экране трёх колонок под них нет. */}
        <span className="text-center leading-tight">
          <span className="block text-[13.5px] font-black leading-none">
            {swim.place != null ? `#${swim.place}` : '—'}
          </span>
          {medal(swim.place) && <span className="mt-[2px] block text-[13px]">{medal(swim.place)}</span>}
          <BestMark record={recordMark} pb={swim.is_pb} sb={swim.is_sb} stacked />
        </span>
        <UI_SwimmStyleIcon
          styleName={swim.style}
          styleLen={swim.distance}
          styleType="icon-len"
          lenPlacement="below"
          size={64}
          className="src-swim-list rounded-[8px] bg-[var(--t-plate)] px-1 py-0.5"
        />
        <span className="min-w-0">
          <span className="hp-mono block text-[13.5px] font-extrabold text-[var(--t-accent)]">
            {swim.time_fail ? 'DSQ' : (
              <UI_SwimTime time={swim.time} quality={quality} />
            )}
          </span>
          {/* Имя целиком, без многоточия: ивритское имя, укороченное посередине, читается
              как чужое. Показан один пловец — вместо имени дисциплина. */}
          <span
            dir={showSwimmerName ? 'auto' : undefined}
            className={`mt-0.5 block break-words text-left leading-tight ${
              showSwimmerName
                ? 'text-[15px] font-black text-[var(--t-text)]'
                : 'text-[12px] font-extrabold text-[var(--t-text-2)]'
            }`}
          >
            {showSwimmerName ? swimmerName : `${swim.distance}m ${styleLabel(swim.style)}`}
          </span>
          <span className="mt-1 flex items-center gap-1.5">
            {swim.is_relay && (
              <span className="hp-mono inline-block rounded-[5px] border border-[var(--t-accent-border)] px-1 py-[1px] text-[8.5px] font-extrabold text-[var(--t-accent)]">RELAY</span>
            )}
            {/* Колонок на телефоне нет, поэтому 🎉 стоит у времени — рядом с заплывом,
                к которому относится, а не у медиа. */}
            {swim.congrats_count > 0 && (
              <CheerChip swim={swim} emphasized={swim.is_pb} onToggle={() => cb.onToggleCheer(swim)} />
            )}
          </span>
        </span>
        {/* Цель нажатия 44px, поздравления — ПОД кнопкой, а не сбоку: справа их выдавливало
            имя. Кнопка раскрывает ТУ ЖЕ панель, что на десктопе (решение Влада 08.09.2026):
            своей мобильной шторки действий у строки больше нет. */}
        <span className="flex flex-col items-end gap-1.5">
          {hasMedia ? (
            <button
              type="button"
              onClick={() => setExpanded((v) => !v)}
              aria-expanded={expanded}
              aria-label="Open media"
              className="hp-mono inline-flex h-[44px] items-center gap-1 rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] px-3 text-[11px] font-extrabold text-[var(--t-accent)]"
            >
              {videos.length > 0 ? `▶ ${videos.length}` : `🖼 ${photos.length}`}
              <Chevron open={expanded} />
            </button>
          ) : (
            <button
              type="button"
              onClick={() => cb.onAddVideo(swim)}
              className="hp-mono inline-flex h-[44px] items-center rounded-[10px] border border-dashed border-[var(--t-accent-border)] bg-transparent px-3 text-[11px] font-extrabold text-[var(--t-accent)]"
            >
              + Video
            </button>
          )}
          {topLiked && <LikeChip m={topLiked} onToggle={() => cb.onToggleLike(topLiked)} />}
        </span>
      </div>

      {/* Раскрытая панель медиа — ОДНА на обе ширины: на узком экране она просто идёт
          во всю ширину строки, без отступа под колонки. */}
      {expanded && hasMedia && (
        <div className="bg-[var(--t-input-bg)] px-4 py-2 sm:px-5 sm:pl-[116px]">
          {/* «Кому это видно» — первым: раньше бейдж стоял в строке и занимал 124px у каждой,
              хотя отвечает на вопрос, который задают, только открыв панель. */}
          <div className="mb-1">
            <RowVisibility swim={swim} publicationsByMedia={cb.publicationsByMedia} />
          </div>
          {[...videos, ...photos].map((m) => (
            <MediaLine key={m.id} m={m} pubs={cb.publicationsByMedia.get(m.id) ?? []} cb={cb} />
          ))}
          <button
            type="button"
            onClick={() => cb.onAddVideo(swim)}
            className="hp-mono my-1.5 rounded-[7px] border border-dashed border-[var(--t-accent-border)] bg-transparent px-2.5 py-[4px] text-[10.5px] font-extrabold text-[var(--t-accent-dim)]"
          >
            + Add media
          </button>
        </div>
      )}
    </>
  );
}

/* ── Competition group ───────────────────────────────────────────────────── */

function CompetitionGroup({ swims, compMedia, showSwimmerName, swimmerNames, preferredSwimmerId, cb }: {
  swims: MySwimDto[];
  compMedia: SwimMediaDto[];
  showSwimmerName: boolean;
  swimmerNames: Map<number, string>;
  preferredSwimmerId: number | null;
  cb: SwimListCallbacks;
}) {
  const [mediaOpen, setMediaOpen] = useState(false);
  const first = swims[0];
  const videoCount = swims.reduce((n, s) => n + s.media.filter((m) => m.media_type === 'video').length, 0);
  const anyPodium = swims.some((s) => s.place != null && s.place <= 3);
  const anyPb = swims.some((s) => s.is_pb);
  const anySb = swims.some((s) => s.is_sb);
  // Дата в шапке группы одна на всех — колонку DATE держим только там, где дни разные.
  const showDate = swims.some((s) => !sameDay(first.competition_date, s.date));
  const selectedName = preferredSwimmerId != null ? swimmerNames.get(preferredSwimmerId) ?? null : null;
  // Подписи колонок — ровно по сетке строки (`.mms-row`), поэтому ширины здесь больше нет:
  // и шапка, и строка тянут её из одного grid-шаблона в `my-media.css`.
  // Колонка 🎉 появляется, только если в этой карточке кого-то уже поздравили: пустой
  // столбец нулей в каждой строке — шум, а не информация.
  const showCheers = swims.some((s) => s.congrats_count > 0);
  const columns: { label: string; align?: 'center' | 'right' }[] = [
    { label: 'PLACE', align: 'center' },
    { label: '' },
    { label: 'SWIM', align: 'center' },
    { label: '' },
    { label: '' },
    { label: 'TIME' },
    ...(showDate ? [{ label: 'DATE' as const }] : []),
    ...(showCheers ? [{ label: '🎉', align: 'right' as const }] : []),
    { label: 'MEDIA', align: 'right' as const },
  ];

  return (
    <div className={`${hpCardCls} overflow-hidden`}>
      <div className="flex flex-wrap items-center gap-2.5 border-b border-[var(--t-accent-soft)] px-4 py-3 sm:px-5">
        {/* Плитка соревнования — общая CompetitionTile (сезон/кубок · буква категории ·
            возрастная лента). Данные считает общий competitionTileData, своей эвристики по
            названию тут нет: категория и флаг чемпионата приходят с сервера. */}
        {/* Плитка, имя пловца и название — ОДНОЙ строкой: на мобильной этот блок занимает
            всю ширину, поэтому дата, бассейн, метки и кнопки переносятся на вторую строку.
            Ширина блока обязательна: без неё название сжималось флексом почти в ноль и
            ломалось по одной букве в строку. */}
        <div className="flex w-full min-w-0 items-center gap-2.5 sm:w-auto">
          <CompetitionTile
            {...competitionTileData({
              name: first.competition_name,
              date: first.competition_date,
              category: first.category,
              is_championship: first.is_championship,
            })}
            size="sm"
          />
          {/* Имя выбранного пловца — крупно и первым: карточка соревнования должна сама
              отвечать «чьи это заплывы». В режиме All имени нет: в карточке лежат заплывы
              разных избранных, и одно имя над ними было бы враньём. */}
          {selectedName && (
            <span dir="auto" className="shrink-0 text-[18px] font-black leading-tight text-[var(--t-accent)]">
              {selectedName}
            </span>
          )}
          <span dir="auto" className="min-w-0 flex-1 text-[15px] font-black leading-tight text-[var(--t-text)] sm:flex-none sm:overflow-hidden sm:text-ellipsis sm:whitespace-nowrap">
            {first.competition_name}
          </span>
        </div>
        <span className="hp-mono text-[11px] font-extrabold text-[var(--t-accent)]">{first.competition_date}</span>
        <span className="text-[11.5px] text-[var(--t-text-3)]">{first.pool_type}</span>
        {anyPodium && <span title="Podium finish">🏅</span>}
        <BestMark pb={anyPb} sb={anySb} />
        <span className="ml-auto flex items-center gap-2">
          <span className="hidden text-[11px] font-bold text-[var(--t-text-3)] sm:inline">
            {swims.length} {swims.length === 1 ? 'swim' : 'swims'} · {videoCount} {videoCount === 1 ? 'video' : 'videos'}
          </span>
          {compMedia.length > 0 && (
            <button
              type="button"
              onClick={() => setMediaOpen((v) => !v)}
              className="hp-mono rounded-[7px] border border-[var(--t-accent-border)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[var(--t-accent)]"
            >
              📎 {compMedia.length} {mediaOpen ? '▴' : '▾'}
            </button>
          )}
          <button
            type="button"
            onClick={() => cb.onAddCompMedia(first.competition_id, first.competition_name)}
            className="hp-mono rounded-[7px] border border-dashed border-[var(--t-accent-border)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[var(--t-accent)]"
          >
            + Photo/Video
          </button>
        </span>
      </div>

      {mediaOpen && compMedia.length > 0 && (
        <div className="border-b border-[var(--t-accent-soft)] bg-[var(--t-input-bg)] px-4 py-2 sm:px-5">
          <p className="hp-mono m-0 mb-1 text-[9px] font-extrabold uppercase tracking-[0.14em] text-[var(--t-accent-border)]">
            Competition media · not tied to a swim
          </p>
          {compMedia.map((m) => (
            <MediaLine key={m.id} m={m} pubs={cb.publicationsByMedia.get(m.id) ?? []} cb={cb} />
          ))}
        </div>
      )}

      {/* Column header (desktop) — та же сетка, что у строки: подписи не могут разъехаться
          со столбцами, потому что ширины у них общие (`.mms-row` в my-media.css). */}
      <div className={`mms-row${showDate ? ' mms-row--dated' : ''}${showCheers ? ' mms-row--cheers' : ''} hidden px-5 py-1.5 sm:grid`}>
        {columns.map((c, i) => (
          <span
            key={i}
            className={`hp-mono text-[9px] font-extrabold uppercase tracking-[0.14em] text-[var(--t-accent-border)]${
              c.align === 'center' ? ' text-center' : c.align === 'right' ? ' text-right' : ''
            }`}
          >
            {c.label}
          </span>
        ))}
      </div>

      <div className="divide-y divide-[var(--t-accent-soft)]">
        {swims.map((s) => (
          <MySwimRow
            key={s.result_id}
            swim={s}
            showSwimmerName={showSwimmerName}
            showDate={showDate}
            showCheers={showCheers}
            swimmerName={rowSwimmerName(s, swimmerNames, preferredSwimmerId)}
            cb={cb}
          />
        ))}
      </div>
    </div>
  );
}

/* ── Root ────────────────────────────────────────────────────────────────── */

function SwimList({ swims, competitionMedia, showSwimmerName, swimmerNames, preferredSwimmerId = null, ...cb }: Props) {
  const groups = useMemo(() => {
    const byComp = new Map<number, MySwimDto[]>();
    for (const s of swims) {
      const list = byComp.get(s.competition_id) ?? [];
      list.push(s);
      byComp.set(s.competition_id, list);
    }
    // swims приходят отсортированными по дате DESC — порядок групп наследуем.
    return Array.from(byComp.values());
  }, [swims]);

  const compMediaByComp = useMemo(() => {
    const map = new Map<number, SwimMediaDto[]>();
    for (const m of competitionMedia) {
      if (m.competition_id == null) continue;
      const list = map.get(m.competition_id) ?? [];
      list.push(m);
      map.set(m.competition_id, list);
    }
    return map;
  }, [competitionMedia]);

  return (
    <div className="flex flex-col gap-4">
      {groups.map((g) => (
        <CompetitionGroup
          key={g[0].competition_id}
          swims={g}
          compMedia={compMediaByComp.get(g[0].competition_id) ?? []}
          showSwimmerName={showSwimmerName}
          swimmerNames={swimmerNames}
          preferredSwimmerId={preferredSwimmerId}
          cb={cb}
        />
      ))}
    </div>
  );
}

export default SwimList;
