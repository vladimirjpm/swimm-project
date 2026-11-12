import React, { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react';
import { UserMediaPublicationDto } from '../../../hooks/useUserMedia';
import { fetchPublishTargets, PublishTargetDto } from '../use-all-my-media';
import {
  canShareWithEveryone, EVERYONE_TRUSTED_ONLY, parseTargetKey, targetKey, type PublishTargetRef,
} from '../../../hooks/useUserMedia';
import { MySwimDto, SwimMediaDto } from '../use-my-swims';
import { STATUS_COLORS, CardStatus, derivedCardStatus, visibilityLabel, hpCardCls } from './status-styles';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import UI_PrelimLabel from '../../components/mix/prelim-label/prelim-label';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';
import UI_SeasonBestBadge from '../../components/mix/season-best-badge/season-best-badge';
import HelperSwimmer from '../../../utils/helpers/helper-swimmer';
import HelperResults from '../../../utils/helpers/helper-results';
import HelperMedia from '../../../utils/helpers/helper-media';
import UI_RecordBadge, { type RecordKind } from '../../components/mix/record-badge/record-badge';
import CompetitionTile from '../../results-main-project/components/competition-header/competition-tile';
import { competitionTileData, hasTileContent } from '../../../utils/helpers/competition-source';
import UI_CompetitionName from '../../components/mix/competition-name/competition-name';
import { routes } from '../../../utils/routes';
import UI_MedalIcon from '../../components/mix/medal-icon/medal-icon';
import UI_ClubLogo from '../../components/mix/club-logo/club-logo';
import MobileFiltersDrawer from '../../components/filter-section/mobile-filters-drawer';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { useDeepThemeClass } from '../../components/deep/use-deep-theme-class';
import { isMedal, styleLabel, swimMarks } from '../swim-marks';
import { CoverButton, CoverDialogHost } from '../cover/cover-entry';
import { PosterButton, PosterDialogHost, canMakePoster } from '../poster/poster-entry';

// Список заплывов, сгруппированный по соревнованиям — ядро My media. Карточка соревнования —
// вариант 3A хендоффа `!design_handoff/my-media-handoff-nov2026` (12.11.2026): слева список
// результатов, всё управление медиа (кому видно, share, withdraw, delete, ❤) — в ПАНЕЛИ МЕДИА
// открытого результата. Панель одна, мест у неё три, и выбирает их ширина КАРТОЧКИ: рядом со
// списком (широкая карточка), выезжает справа (десктоп, карточка уже), нижняя шторка (телефон).
// План и решения — `docs/plans/my-media-card-3a-plan.md`.

export interface SwimListCallbacks {
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  onPlay: (media: SwimMediaDto) => void;
  onAddVideo: (swim: MySwimDto) => void;
  onAddCompMedia: (competitionId: number, competitionName: string) => void;
  onSubmitShare: (mediaId: number, target: PublishTargetRef, level: 'members' | 'public') => Promise<boolean>;
  onWithdraw: (mediaId: number, target: PublishTargetRef) => void;
  onDelete: (mediaId: number) => void;
  onToggleLike: (media: SwimMediaDto) => void;
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
 *  • рекорд (`UI_RecordBadge`) старше обоих и замещает их.
 * Метка — свойство ЗАПЛЫВА, поэтому живёт только в строке; сводной метки «PB есть хоть где-то»
 * в шапке карточки больше нет (решение Влада 10.11.2026): она не говорила, какой заплыв.
 * Помеченной строке (`SuspectReason`, Р76) метки не рисуются — это наш вывод из времени.
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
function rowSwimmer(
  swim: MySwimDto, names: Map<number, string>, preferredId: number | null,
): { id: number | null; name: string } {
  if (preferredId != null && HelperSwimmer.resultBelongsToSwimmer(swim, preferredId)) {
    return { id: preferredId, name: names.get(preferredId) ?? '?' };
  }
  for (const [id, name] of names) {
    if (HelperSwimmer.resultBelongsToSwimmer(swim, id)) return { id, name };
  }
  return { id: null, name: '?' };
}

/**
 * Ссылки карточки (просьба Влада 10.11.2026): имя → страница пловца, название → соревнование,
 * дисциплина заплыва → ЭТОТ заплыв в протоколе. Цвет у ссылки — текста вокруг, подчёркивание
 * только под курсором: иначе карточка превратилась бы в синий частокол.
 */
const LINK_CLS = 'no-underline [color:inherit] hover:underline hover:underline-offset-2';

/**
 * Заплыв в протоколе: таб Swims, отфильтрованный на пловца, строка подсвечена (`?swim=`).
 * Адрес — ДНЯ, даже у многодневки: заплыв лежит в конкретном дне (`routes.competitionSwims`).
 */
function swimHref(swim: MySwimDto, swimmerId: number | null): string {
  return routes.competitionSwims(swim.competition_id, { swimmerId, resultId: swim.result_id });
}

/* ── Линейные иконки карточки ─────────────────────────────────────────────── */

/**
 * Инлайновые SVG с `currentColor`, а не `UI_*`: готовых линейных иконок (видео, фото, плюс,
 * стрелка, крестик) в реестре нет (docs/ui-components.md §6), а отдельных вариантов под тему
 * им не нужно — цвет приходит от текста. Рисунки — из хендоффа карточки 3A.
 */
const IconVideo = ({ size = 8 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 12 12" aria-hidden="true">
    <path d="M3 1.5v9l7.5-4.5z" fill="currentColor" />
  </svg>
);
const IconPhoto = ({ size = 12 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
    <rect x="1.5" y="2.5" width="13" height="11" rx="2" />
    <path d="M2 11l3.5-3.5 3 3 2-2 3.5 3.5" />
  </svg>
);
const IconPlus = ({ size = 16 }: { size?: number }) => (
  <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
    <path d="M8 3v10M3 8h10" />
  </svg>
);
/** Стрелка кнопки «открыть медиа»: вправо — панель рядом или выезжает справа, вверх — шторка. */
const IconChevron = ({ dir }: { dir: 'right' | 'up' }) => (
  <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
    <path d={dir === 'right' ? 'M6 4l4 4-4 4' : 'M4 10l4-4 4 4'} />
  </svg>
);
const IconClose = () => (
  <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
    <path d="M4 4l8 8M12 4l-8 8" />
  </svg>
);

/**
 * Счётчик видео или фото. Есть — число на акценте; нет — серый значок без числа (ноль в
 * каждой строке читался бы как оценка, а не как «ещё не добавили»).
 */
function Counter({ kind, count, large = false }: { kind: 'video' | 'photo'; count: number; large?: boolean }) {
  const noun = kind === 'video' ? 'video' : 'photo';
  const label = count === 0 ? `No ${noun}s` : `${count} ${noun}${count === 1 ? '' : 's'}`;
  return (
    <span
      role="img"
      aria-label={label}
      title={label}
      className={`mmc-count${count > 0 ? ' mmc-count--on' : ''}${large ? ' mmc-count--lg' : ''}`}
    >
      {kind === 'video' ? <IconVideo size={large ? 10 : 8} /> : <IconPhoto size={large ? 13 : count > 0 ? 11 : 12} />}
      {count > 0 && count}
    </span>
  );
}

/**
 * Место — кружок `UI_MedalIcon`. Медальная заливка только там, где медаль вручали
 * (`isMedal`); иначе `icon-noplace` — номер без кружка: место 1 в предварительном или в
 * «כללי» золотым кружком показало бы медаль, которой не было. Места нет (DNS / DQ, снятые) —
 * прочерк.
 */
function PlaceMark({ swim, size }: { swim: MySwimDto; size: 'medal-32' | 'medal-40' }) {
  if (swim.place == null) {
    return <span className="text-[15px] font-black leading-none text-[var(--t-text-3)]" title="No place">—</span>;
  }
  const medalPlace = isMedal(swim);
  return (
    <UI_MedalIcon
      place={String(swim.place)}
      styleType={medalPlace ? 'icon-place' : 'icon-noplace'}
      styleSize={size}
      title={medalPlace ? `Place ${swim.place} · medal` : `Place ${swim.place}`}
    />
  );
}

/** Иконка стиля с дистанцией на плите 52px (картинка 46 + поля) — одна на строку и панель. */
function StylePlate({ swim }: { swim: MySwimDto }) {
  return (
    <UI_SwimmStyleIcon
      styleName={swim.style}
      styleLen={swim.distance}
      styleType="icon-len"
      lenPlacement="below"
      iconSize={46}
      lenSize={11}
      className="src-swim-list mmc-plate"
    />
  );
}

/** Время заплыва: `UI_SwimTime` (единственный вывод времени) или пометка протокола DNS / DQ. */
function SwimTimeText({ swim, quality }: { swim: MySwimDto; quality: ReturnType<typeof swimMarks>['quality'] }) {
  return swim.time_fail
    ? <>{HelperResults.failLabel(swim)}</>
    : <UI_SwimTime time={swim.time} quality={quality} />;
}

/** RELAY — признак самого заплыва, стоит под временем (решение Влада 08.09.2026). */
const RELAY_CHIP = (
  <span className="hp-mono rounded-[5px] border border-[var(--t-text-2)] px-[5px] text-[9px] font-extrabold leading-[1.5] tracking-[0.08em] text-[var(--t-text-2)]">
    RELAY
  </span>
);

function mediaStatus(m: SwimMediaDto, pubs: UserMediaPublicationDto[]): { status: CardStatus; isPublic: boolean } {
  const status = derivedCardStatus(pubs);
  const isPublic = pubs.some((p) => p.status === 'approved' && p.level === 'public');
  return { status, isPublic };
}

function pillTitle(pubs: UserMediaPublicationDto[]): string {
  if (pubs.length === 0) return 'Private — only you can see this';
  return pubs
    .map((p) => `${p.target_name}: ${p.status} · ${p.level === 'public' ? 'everyone' : 'members'}`)
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
  const groups = Array.from(new Map(pubs.map((p) => [p.target_id, p.target_name])).values());
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

/**
 * Плашка источника: «▶ YOUTUBE» и ❤ ВНУТРИ неё (решение Влада 08.09.2026). Сердечко про
 * это же медиа, отдельной плашкой оно занимало ещё одну позицию в строке и выдавливало
 * «Delete» на второй ряд.
 *
 * Корпус — `span`, а не `button`: внутри ДВЕ разные кнопки (играть и лайкнуть), а кнопка
 * в кнопке невалидна и отдаёт клик по сердечку в воспроизведение.
 */
function SourceChip({ m, onPlay, onToggleLike }: { m: SwimMediaDto; onPlay: () => void; onToggleLike: () => void }) {
  const isAlbum = HelperMedia.isAlbumUrl(m.url);
  const label = isAlbum ? '📁 ALBUM' : m.media_type === 'image' ? '🖼 PHOTO' : `▶ ${m.source_type.toUpperCase()}`;
  const like = likeVisual(m);
  return (
    <span className="hp-mono inline-flex w-[142px] shrink-0 items-stretch overflow-hidden rounded-[7px] border border-[var(--t-accent-border)] bg-[var(--t-accent-soft)] text-[10.5px] font-extrabold text-[var(--t-accent)]">
      <button
        type="button"
        onClick={onPlay}
        className="min-w-0 flex-1 truncate px-2 py-[4px] text-left"
        title={isAlbum ? 'Open album' : m.media_type === 'image' ? 'Open photo' : 'Play'}
      >
        {label}
      </button>
      <button
        type="button"
        onClick={onToggleLike}
        className="flex shrink-0 items-center border-l border-[var(--t-accent-border)] px-[7px]"
        style={{ color: like.color, background: like.background }}
        title={like.liked ? 'Remove like' : 'Like'}
      >
        {like.label}
      </button>
    </span>
  );
}

/** Вид сердечка — один на плашку источника и на чип строки, чтобы они не разошлись. */
function likeVisual(m: SwimMediaDto) {
  const liked = m.my_like;
  return {
    liked,
    label: `❤ ${m.likes_count}`,
    color: liked ? 'var(--t-like)' : m.likes_count > 0 ? 'var(--t-text-2)' : 'var(--t-text-3)',
    border: liked ? 'var(--t-like)' : 'var(--t-border)',
    background: liked ? 'var(--t-like-soft)' : 'transparent',
  };
}

/**
 * ❤ — сколько людей отметило ЭТО медиа. В строке заплыва он только ПОКАЗЫВАЕТ, что
 * популярно (решение Влада 08.09.2026), поэтому там идёт без `onToggle` и рисуется
 * span'ом: нажимать нечего, и вид кнопки обещал бы обратное. Нажимаемым он остаётся в
 * раскрытой панели, где вообще живут действия над медиа.
 */
function LikeChip({ m, onToggle }: { m: SwimMediaDto; onToggle?: () => void }) {
  const v = likeVisual(m);
  const className = 'hp-mono rounded-[7px] px-2 py-[3px] text-[10.5px] font-extrabold';
  const style = { border: `1px solid ${v.border}`, background: v.background, color: v.color };

  if (!onToggle) {
    return <span className={className} style={style} title={`${m.likes_count} liked this`}>{v.label}</span>;
  }
  return (
    <button type="button" onClick={onToggle} className={className} style={style} title={v.liked ? 'Remove like' : 'Like'}>
      {v.label}
    </button>
  );
}

/**
 * 🎉 — сколько людей поздравило с ЭТИМ заплывом. Тоже только показ: строка отвечает на
 * вопрос «что тут популярно», а не предлагает поздравить самого себя — эта страница про
 * своих пловцов.
 */
function CheerChip({ swim, emphasized }: { swim: MySwimDto; emphasized: boolean }) {
  const on = swim.my_cheer;
  return (
    <span
      className="hp-mono whitespace-nowrap rounded-[7px] px-2 py-[3px] text-[10.5px] font-extrabold"
      style={{
        border: `1px solid ${on ? 'var(--t-warn)' : emphasized ? 'var(--t-warn-border)' : 'var(--t-border)'}`,
        background: on ? 'var(--t-warn-soft)' : 'transparent',
        color: on ? 'var(--t-warn)' : emphasized ? 'var(--t-warn)' : swim.congrats_count > 0 ? 'var(--t-text-2)' : 'var(--t-text-3)',
      }}
      title={`${swim.congrats_count} congratulated this swim`}
    >
      🎉 {swim.congrats_count}
    </span>
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
  // Ключ цели строкой («group:17» / «club:438»): цель это пара (тип, id), числом её не выразить.
  const [group, setGroup] = useState<string>(active ? targetKey({ type: active.target_type, id: active.target_id }) : '');
  const [level, setLevel] = useState<'members' | 'public'>(active ? active.level : 'members');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    fetchPublishTargets(m.id).then((t) => { if (alive) setTargets(t); });
    return () => { alive = false; };
  }, [m.id]);

  // Публикации приходят асинхронно и меняются после share/withdraw — возвращаем селекты
  // к фактическому состоянию, но только когда оно реально сменилось (иначе затрём выбор юзера).
  const activeKey = active ? `${active.target_type}:${active.target_id}:${active.level}` : '';
  useEffect(() => {
    setGroup(active ? targetKey({ type: active.target_type, id: active.target_id }) : '');
    setLevel(active ? active.level : 'members');
  }, [activeKey]); // eslint-disable-line react-hooks/exhaustive-deps

  // Группа могла выпасть из publish-targets (пловца убрали из ростера), а публикация осталась —
  // без этого селект показал бы пустоту вместо своей же группы.
  const options = useMemo(() => {
    const list = targets ?? [];
    if (active && !list.some((t) => t.type === active.target_type && t.id === active.target_id)) {
      return [...list, { type: active.target_type, id: active.target_id, name: active.target_name }];
    }
    return list;
  }, [targets, active]);

  // Сервер отвергает повторную подачу в ту же группу («publication already exists»),
  // смена уровня идёт через withdraw+резаявку в onSubmitShare — здесь только гасим кнопку.
  const chosen = parseTargetKey(group);
  const current = chosen == null ? null : pubs.find(
    (p) => p.target_type === chosen.type && p.target_id === chosen.id
           && (p.status === 'pending' || p.status === 'approved')) ?? null;
  const unchanged = current != null && current.level === level;
  // «Everyone 🌐» — только у доверенной группы (Р65); у клуба — всегда.
  const everyoneAllowed = canShareWithEveryone(
    chosen == null ? null : options.find((t) => t.type === chosen.type && t.id === chosen.id));

  const share = async () => {
    if (chosen == null || busy || unchanged) return;
    setBusy(true);
    await cb.onSubmitShare(m.id, chosen, level);
    setBusy(false);
  };

  const withdrawable = pubs.filter((p) => p.status === 'pending' || p.status === 'approved');

  return (
    // Перенос оставлен: три контрола публикации требуют ~430px, и на узкой панели нести
    // их в одну строку значило бы обрезать. Без них строка «источник · статус · Delete»
    // помещается целиком — ровно этого и не хватало, пока ❤ стояло отдельной плашкой.
    <div className="flex flex-wrap items-center gap-2 py-[6px]">
      <SourceChip m={m} onPlay={() => cb.onPlay(m)} onToggleLike={() => cb.onToggleLike(m)} />
      <span className="min-w-0"><StatusPill status={status} isPublic={isPublic} pubs={pubs} /></span>
      {m.moderation_state && (
        // Жалобы (Р62): владелец видит, что медиа спрятано, и почему поделиться нельзя.
        <span
          className="hp-mono rounded-[6px] border border-[var(--t-warn-border)] bg-[var(--t-warn-soft)] px-[8px] py-[2px] text-[10.5px] font-extrabold text-[var(--t-warn)]"
          title={m.moderation_state === 'removed'
            ? 'The site admin removed this after reports. It is visible only to you and can’t be shared again.'
            : 'Hidden from everyone after reports, until the site admin reviews it. You still see it here.'}
        >
          {m.moderation_state === 'removed' ? '⚑ Removed by admin' : '⚑ Hidden — under review'}
        </span>
      )}
      <div className="ml-auto flex flex-wrap items-center gap-1.5">
        {targets != null && options.length > 0 && !m.moderation_state && (
          <>
            <select
              value={group}
              onChange={(e) => {
                setGroup(e.target.value);
                const next = parseTargetKey(e.target.value);
                // У клуба нет аккаунтов-участников, значит и уровня members.
                if (next?.type === 'club') setLevel('public');
                // У недоверенной группы нет Everyone (Р65) — переключаем на Members.
                else if (!canShareWithEveryone(next && options.find((t) => t.type === next.type && t.id === next.id)))
                  setLevel('members');
              }}
              className="rounded-[7px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-1.5 py-[3px] text-[11px] text-[var(--t-text)]"
            >
              <option value="">Group or club…</option>
              {options.map((t) => (
                <option key={targetKey(t)} value={targetKey(t)}>
                  {t.type === 'club' ? `${t.name} (club)` : t.name}
                </option>
              ))}
            </select>
            <select
              value={level}
              onChange={(e) => setLevel(e.target.value as 'members' | 'public')}
              // Почему Members гаснет на клубе — сказать вслух: у погашенного пункта нет
              // способа объясниться, и это читается как поломка (спрошено 09.09.2026).
              title={chosen?.type === 'club' ? 'Clubs have no member accounts — publishing to a club is always public'
                : !everyoneAllowed ? EVERYONE_TRUSTED_ONLY : undefined}
              className="rounded-[7px] border border-[var(--t-border)] bg-[var(--t-input-bg)] px-1.5 py-[3px] text-[11px] text-[var(--t-text)]"
            >
              <option value="members" disabled={chosen?.type === 'club'}>Members</option>
              <option value="public" disabled={!everyoneAllowed}>Everyone 🌐{everyoneAllowed ? '' : ' — Trusted groups only'}</option>
            </select>
            <button
              type="button"
              disabled={chosen == null || busy || unchanged || (level === 'public' && !everyoneAllowed)}
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
            key={p.target_id}
            type="button"
            onClick={() => cb.onWithdraw(m.id, { type: p.target_type, id: p.target_id })}
            className="hp-mono rounded-[7px] border border-[var(--t-warn-border)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[var(--t-warn)]"
            title={`Withdraw from ${p.target_name}`}
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

/* ── Ссылки строки ───────────────────────────────────────────────────────────── */

/**
 * Иконка дисциплины — ссылка на заплыв в протоколе. Ссылка именно она, а не время: у
 * помеченного времени `UI_SwimTime` рисует КНОПКУ-объяснялку, а кнопка внутри `<a>`
 * невалидна и отдавала бы клик по «⚠» в переход.
 */
function SwimLink({ href, discipline, children }: { href: string; discipline: string; children: React.ReactNode }) {
  const label = `${discipline} — open this swim in the results`;
  return (
    <a
      href={href}
      title={label}
      aria-label={label}
      className="block w-fit rounded-[8px] transition-shadow hover:shadow-[0_0_0_2px_var(--t-accent-border)] focus-visible:shadow-[0_0_0_2px_var(--t-accent)] focus-visible:outline-none"
    >
      {children}
    </a>
  );
}

/** Имя пловца — ссылка на его страницу; без id (не нашли среди избранных) — просто текст. */
function SwimmerLink({ id, children }: { id: number | null; children: React.ReactNode }) {
  if (id == null) return <>{children}</>;
  return <a href={routes.swimmer(id)} className={LINK_CLS}>{children}</a>;
}

/* ── Строка результата (карточка 3A) ──────────────────────────────────────────── */

/**
 * Это НЕ общая строка заплыва `SwimRow` (`components/swim-row/`): та — двухлинейная КАРТОЧКА
 * результата, а здесь — плотная строка управления медиа (вариант 3A хендоффа
 * `!design_handoff/my-media-handoff-nov2026`, 12.11.2026).
 *
 * Слева направо: место (`UI_MedalIcon`) → иконка стиля с дистанцией (ссылка на заплыв в
 * протоколе) → время и одна метка → счётчики видео и фото → кнопка. Текстовой подписи
 * «50m breast» в строке нет: стиль и дистанцию показывает иконка (подпись осталась в панели).
 *
 * Всё управление медиа (кому видно, Share / Withdraw / Delete, ❤) живёт в панели медиа
 * открытого результата. У заплыва без медиа открывать нечего — на месте кнопки стоит «+»,
 * который сразу зовёт Add video: добавить ролик к свежему заплыву — главный сценарий
 * страницы, и лишний шаг через пустую панель ему ни к чему.
 *
 * Сетка — в `my-media.css` (`.mmc-row`), от ШИРИНЫ КАРТОЧКИ (container query): на
 * телефонной счётчики встают столбиком, цели касания 44px.
 */
function MySwimRow({ swim, first, showSwimmerName, showDate, swimmerName, swimmerId, selected, sheet, onOpen, cb }: {
  swim: MySwimDto;
  /** Первая строка списка или дня — без верхнего разделителя. */
  first: boolean;
  showSwimmerName: boolean;
  /** День заплыва в строке — только у однодневной карточки, где он разошёлся с шапкой (`sameDay`). */
  showDate: boolean;
  swimmerName: string;
  /** Чей это заплыв среди избранных (у эстафеты — нога из избранного); null — не нашли. */
  swimmerId: number | null;
  /** Панель медиа открыта на этом результате. */
  selected: boolean;
  /** Панель открывается нижней шторкой (телефон) — стрелка кнопки смотрит вверх. */
  sheet: boolean;
  onOpen: () => void;
  cb: SwimListCallbacks;
}) {
  const href = swimHref(swim, swimmerId);
  const discipline = `${swim.distance}m ${styleLabel(swim.style)}`;
  const videos = swim.media.filter((m) => m.media_type === 'video').length;
  const photos = swim.media.length - videos;
  // ❤ в строке — сводка: самое залайканное медиа заплыва. Ноль не показываем: пустое
  // сердечко в каждой строке читается как «никому не понравилось», а не как «ещё нет оценок».
  const topLiked = swim.media.reduce<SwimMediaDto | null>(
    (best, m) => (m.likes_count > 0 && (!best || m.likes_count > best.likes_count) ? m : best),
    null,
  );
  const { quality, record, pb, sb } = swimMarks(swim);
  const flagged = swimFlaggedRowProps(quality);

  return (
    <div
      {...flagged}
      className={`mmc-row${first ? ' mmc-row--first' : ''}${selected ? ' mmc-row--on' : ''}${flagged.className ? ` ${flagged.className}` : ''}`}
    >
      <span className="mmc-row__place">
        <PlaceMark swim={swim} size="medal-32" />
        {/* Место предварительного заплыва — не медальное, и молчать об этом нельзя:
            иначе «1» утром и «1» вечером читаются как два золота. */}
        <UI_PrelimLabel heatType={swim.heat_type} className="text-[8px] leading-none" />
      </span>
      <SwimLink href={href} discipline={discipline}>
        <StylePlate swim={swim} />
      </SwimLink>
      <div className="flex min-w-0 flex-col items-start gap-[2px]">
        {/* Режим All: имя — первой строкой ячейки времени, целиком, без многоточия (ивритское
            имя, укороченное посередине, читается как чужое). `w-fit` + text-left: dir="auto"
            в блоке на всю ширину увёл бы имя вправо. */}
        {showSwimmerName && (
          <span dir="auto" className="w-fit max-w-full break-words text-left text-[14px] font-black leading-tight text-[var(--t-text)]">
            <SwimmerLink id={swimmerId}>{swimmerName}</SwimmerLink>
          </span>
        )}
        <span className="mmc-time"><SwimTimeText swim={swim} quality={quality} /></span>
        {/* Под временем — одна метка достижения (рекорд > SB > PB) либо RELAY, и то, что есть
            не у каждой строки: день заплыва, 🎉 и ❤. `empty:hidden` — пустой контейнер не
            должен раздвигать строку. */}
        <span className="flex flex-wrap items-center gap-1 empty:hidden">
          <BestMark record={record} pb={pb} sb={sb} />
          {swim.is_relay && RELAY_CHIP}
          {/* Дата — общим `UI_DateIcon`: формат даты живёт в одном месте. */}
          {showDate && (
            <UI_DateIcon
              styleType="row-style-1"
              date={swim.date}
              fontClassName="hp-mono text-[10px] text-[var(--t-text-3)]"
            />
          )}
          {/* 🎉 про заплыв, ❤ про его медиа — оба только ПОКАЗЫВАЮТ (решение Влада 08.09.2026). */}
          {swim.congrats_count > 0 && <CheerChip swim={swim} emphasized={swim.is_pb} />}
          {topLiked && <LikeChip m={topLiked} />}
        </span>
      </div>
      <div className="mmc-row__counts">
        <Counter kind="video" count={videos} />
        <Counter kind="photo" count={photos} />
      </div>
      {swim.media.length > 0 ? (
        <button
          type="button"
          onClick={onOpen}
          aria-pressed={selected}
          aria-label={selected ? 'Media of this result is open' : 'Open photos and videos of this result'}
          title="Photos and videos: play, share, withdraw, delete"
          className={`mmc-btn${selected ? ' mmc-btn--on' : ''}`}
        >
          <IconChevron dir={sheet ? 'up' : 'right'} />
        </button>
      ) : (
        <button
          type="button"
          onClick={() => cb.onAddVideo(swim)}
          aria-label="Add video to this result"
          title="+ Add video"
          className="mmc-btn mmc-btn--add"
        >
          <IconPlus />
        </button>
      )}
    </div>
  );
}

/**
 * Строка «медиа ко всему соревнованию» — первая в списке, только когда такое медиа есть.
 * В макете 3A его нет вовсе (хендофф §1.7): счётчики в шапке считают всё медиа карточки, а
 * управлять роликом «не к заплыву» было бы негде. Строка открывает ту же панель, что и
 * результат.
 */
function CompMediaRow({ media, selected, sheet, onOpen }: {
  media: SwimMediaDto[];
  selected: boolean;
  sheet: boolean;
  onOpen: () => void;
}) {
  const videos = media.filter((m) => m.media_type === 'video').length;
  return (
    <div className={`mmc-row mmc-row--first${selected ? ' mmc-row--on' : ''}`}>
      <span className="mmc-row__place text-[17px] leading-none" aria-hidden="true">📎</span>
      <span className="col-span-2 min-w-0">
        <span className="block text-[13px] font-black leading-tight text-[var(--t-text)]">Competition media</span>
        <span className="block text-[11px] font-bold leading-tight text-[var(--t-text-2)]">not tied to a swim</span>
      </span>
      <div className="mmc-row__counts">
        <Counter kind="video" count={videos} />
        <Counter kind="photo" count={media.length - videos} />
      </div>
      <button
        type="button"
        onClick={onOpen}
        aria-pressed={selected}
        aria-label={selected ? 'Competition media is open' : 'Open competition photos and videos'}
        className={`mmc-btn${selected ? ' mmc-btn--on' : ''}`}
      >
        <IconChevron dir={sheet ? 'up' : 'right'} />
      </button>
    </div>
  );
}

/* ── Панель медиа открытого результата ────────────────────────────────────────── */

/** Крупное превью выбранного медиа: кадр ролика с кнопкой Play, фото, значок альбома. */
function MediaStage({ m, onPlay }: { m: SwimMediaDto; onPlay: () => void }) {
  const isAlbum = HelperMedia.isAlbumUrl(m.url);
  const thumb = HelperMedia.resolveThumbUrl(m.media_type, m.source_type, m.url);
  const label = isAlbum ? 'Open album' : m.media_type === 'image' ? 'Open photo' : 'Play video';
  return (
    <button type="button" className="mmc-stage" onClick={onPlay} aria-label={label} title={label}>
      {/* `no-referrer` — правило для картинок по ссылке (Drive отвечает 429 с Referer). */}
      {thumb && <img src={thumb} alt="" referrerPolicy="no-referrer" loading="lazy" />}
      {m.media_type === 'video'
        ? <span className="mmc-stage__play"><IconVideo size={20} /></span>
        : !thumb && <span className="text-[13px] font-extrabold">{isAlbum ? '📁 Album' : 'Photo'}</span>}
    </button>
  );
}

/**
 * Панель медиа — ОДНА вёрстка на три места: рядом со списком (широкая карточка), выезжающая
 * справа и нижняя шторка. Сверху заголовок (что открыто), крупное превью выбранного медиа,
 * лента превью всех видео и фото с «добавить», под ней — управление ВЫБРАННЫМ медиа: тот же
 * `MediaLine`, что был в раскрывающейся панели под строкой (источник и ❤, статус публикации,
 * пометка модерации, Share / Update, Withdraw, Delete).
 *
 * `actions` — кнопки результата под управлением медиа: `YouTube cover` (задача 2 хендоффа).
 * `Poster` и `Share` из макета здесь нет: плакат — про все заплывы пловца, его кнопка стоит в
 * шапке карточки (решение Влада 12.11.2026), а Share живёт в
 * `MediaLine`.
 */
function MediaPane({ head, media, addLabel, onAdd, onClose, actions, cb }: {
  head: React.ReactNode;
  /** Видео первыми, затем фото. */
  media: SwimMediaDto[];
  addLabel: string;
  onAdd: () => void;
  /** Есть только у выезжающей панели и шторки; рядом со списком закрывать нечего. */
  onClose?: () => void;
  /** Кнопки открытого результата (обложка YouTube); у медиа соревнования их нет. */
  actions?: React.ReactNode;
  cb: SwimListCallbacks;
}) {
  const [activeId, setActiveId] = useState<number | null>(null);
  // Выбранное могли удалить — тогда снова первое.
  const active = media.find((m) => m.id === activeId) ?? media[0] ?? null;

  return (
    <div className="mmc-media">
      <div className="flex items-start gap-2">
        <div className="min-w-0 flex-1">{head}</div>
        {onClose && (
          <button type="button" onClick={onClose} aria-label="Close" className="mmc-btn mmc-btn--lg text-[var(--t-text-2)]">
            <IconClose />
          </button>
        )}
      </div>
      {active && <MediaStage m={active} onPlay={() => cb.onPlay(active)} />}
      <div className="mmc-thumbs">
        {media.map((m, i) => {
          const thumb = HelperMedia.resolveThumbUrl(m.media_type, m.source_type, m.url);
          const isVideo = m.media_type === 'video';
          const on = m.id === active?.id;
          return (
            <button
              key={m.id}
              type="button"
              onClick={() => setActiveId(m.id)}
              aria-pressed={on}
              aria-label={`${isVideo ? 'Video' : 'Photo'} ${i + 1}${on ? ', selected' : ''}`}
              className={`mmc-thumb${isVideo ? ' mmc-thumb--video' : ''}${on ? ' mmc-thumb--on' : ''}`}
            >
              {thumb
                ? <img src={thumb} alt="" referrerPolicy="no-referrer" loading="lazy" />
                : isVideo ? <IconVideo size={14} /> : <IconPhoto size={18} />}
            </button>
          );
        })}
        <button type="button" onClick={onAdd} aria-label={addLabel} title={`+ ${addLabel}`} className="mmc-thumb mmc-thumb--add">
          <IconPlus />
        </button>
      </div>
      {/* `key` — у `MediaLine` своё состояние селектов публикации: при смене медиа оно обязано
          начаться заново, а не унаследовать выбор соседа. */}
      {active && <MediaLine key={active.id} m={active} pubs={cb.publicationsByMedia.get(active.id) ?? []} cb={cb} />}
      {actions && <div className="flex flex-wrap items-center gap-2 border-t border-[var(--t-border-2)] pt-2.5">{actions}</div>}
    </div>
  );
}

/** Заголовок панели результата: место, иконка, время, метка, «кому видно»; ниже — подписи. */
function SwimPaneHead({ swim, showSwimmerName, swimmerName, swimmerId, cb }: {
  swim: MySwimDto;
  showSwimmerName: boolean;
  swimmerName: string;
  swimmerId: number | null;
  cb: SwimListCallbacks;
}) {
  const href = swimHref(swim, swimmerId);
  const discipline = `${swim.distance}m ${styleLabel(swim.style)}`;
  const { quality, record, pb, sb } = swimMarks(swim);
  return (
    <div className="flex flex-col gap-2">
      {/* Режим All: имя стоит заголовком панели — в шапке карточки его нет. */}
      {showSwimmerName && (
        <span dir="auto" className="w-fit max-w-full text-left text-[16px] font-black leading-tight text-[var(--t-accent)]">
          <SwimmerLink id={swimmerId}>{swimmerName}</SwimmerLink>
        </span>
      )}
      <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1.5">
        <PlaceMark swim={swim} size="medal-40" />
        <SwimLink href={href} discipline={discipline}>
          <StylePlate swim={swim} />
        </SwimLink>
        <span className="mmc-media__time"><SwimTimeText swim={swim} quality={quality} /></span>
        <BestMark record={record} pb={pb} sb={sb} />
        {/* «Кому это видно» — по ВСЕМ медиа заплыва; подробности по каждому — ниже, в строке
            выбранного медиа. */}
        <span className="ml-auto"><RowVisibility swim={swim} publicationsByMedia={cb.publicationsByMedia} /></span>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        {/* Текстовая подпись дисциплины — здесь, а не в строке: в строке её заменила иконка. */}
        <a href={href} title="Open this swim in the results" className={`text-[12.5px] font-extrabold text-[var(--t-text-2)] ${LINK_CLS}`}>
          {discipline}
        </a>
        {swim.is_relay && RELAY_CHIP}
        <UI_PrelimLabel heatType={swim.heat_type} className="text-[9px]" />
        {swim.congrats_count > 0 && <CheerChip swim={swim} emphasized={swim.is_pb} />}
      </div>
    </div>
  );
}

/* ── Competition group ───────────────────────────────────────────────────── */

/** Заплывы карточки по дням (`date`, yyyy-MM-dd) в порядке появления — сортировку списка наследуем. */
function swimsByDay(swims: MySwimDto[]): { date: string; swims: MySwimDto[] }[] {
  const byDay = new Map<string, MySwimDto[]>();
  for (const s of swims) {
    const list = byDay.get(s.date) ?? [];
    list.push(s);
    byDay.set(s.date, list);
  }
  return Array.from(byDay, ([date, list]) => ({ date, swims: list }));
}

/** yyyy-MM-dd → dd/MM/yyyy — формат даты в шапке карточки (`competition_date`). */
function isoToDmy(iso: string): string {
  const [y, m, d] = iso.split('-');
  return d ? `${d}/${m}/${y}` : iso;
}

/** Полоса дня внутри карточки многодневки: день недели · дата · сколько заплывов. */
function DayDivider({ date, count }: { date: string; count: number }) {
  const [y, m, d] = date.split('-').map(Number);
  const weekday = new Date(y, m - 1, d).toLocaleDateString('en-US', { weekday: 'short' });
  return (
    <div className="mmc-day">
      <span className="hp-mono text-[10.5px] font-extrabold uppercase tracking-[0.06em] text-[var(--t-text-2)]">
        {weekday}
      </span>
      <UI_DateIcon
        styleType="row-style-1"
        date={date}
        fontClassName="hp-mono text-[10.5px] font-extrabold text-[var(--t-text)]"
      />
      <span className="hp-mono text-[10.5px] font-extrabold text-[var(--t-text-2)]">
        · {count} {count === 1 ? 'swim' : 'swims'}
      </span>
    </div>
  );
}

/** Что открыто в панели медиа: заплыв (`result_id`) или медиа ко всему соревнованию. */
type PaneSel = number | 'comp';

/**
 * Включена ли раскладка «две панели». Решает CSS — container query на карточке
 * (`@container mmc (min-width: …)` в `my-media.css`), а не число в коде: здесь только
 * ЧИТАЕМ, показан ли слот панели. Так порог живёт в одном месте, и вёрстка с логикой
 * («панель рядом» против «панель в портале») разойтись не могут.
 */
function useTwoPane(
  cardRef: React.RefObject<HTMLElement>, paneRef: React.RefObject<HTMLElement>, hasMedia: boolean,
): boolean {
  const [wide, setWide] = useState(false);
  useLayoutEffect(() => {
    const card = cardRef.current;
    const pane = paneRef.current;
    if (!card || !pane) return undefined;
    const check = () => setWide(getComputedStyle(pane).display !== 'none');
    check();
    const observer = new ResizeObserver(check);
    observer.observe(card);
    return () => observer.disconnect();
    // `hasMedia` меняет класс карточки, а с ним и видимость слота — перечитываем.
  }, [cardRef, paneRef, hasMedia]);
  return wide;
}

function CompetitionGroup({ swims, compMedia, showSwimmerName, swimmerNames, preferredSwimmerId, isDesktop, deep, cb }: {
  swims: MySwimDto[];
  compMedia: SwimMediaDto[];
  showSwimmerName: boolean;
  swimmerNames: Map<number, string>;
  preferredSwimmerId: number | null;
  /** Окно от 1024px: панель медиа выезжает справа; уже — нижняя шторка. */
  isDesktop: boolean;
  /** Класс темы deep — порталу панели он нужен свой. */
  deep: string;
  cb: SwimListCallbacks;
}) {
  const cardRef = useRef<HTMLDivElement>(null);
  const paneRef = useRef<HTMLDivElement>(null);
  const first = swims[0];
  const videoCount = [...swims.flatMap((s) => s.media), ...compMedia].filter((m) => m.media_type === 'video').length;
  const photoCount = swims.reduce((n, s) => n + s.media.length, 0) + compMedia.length - videoCount;
  const hasMedia = videoCount + photoCount > 0;
  const anyPodium = swims.some((s) => s.place != null && s.place <= 3);
  // Дни карточки (многодневка — одна карточка на событие): разные дни — полосы-разделители.
  const days = useMemo(() => swimsByDay(swims), [swims]);
  const multiDay = days.length > 1;
  // Разделители уже несут дату; день в строке — только у однодневной карточки, где день
  // заплыва всё же разошёлся с датой в шапке.
  const showDate = !multiDay && swims.some((s) => !sameDay(first.competition_date, s.date));
  // Самый ранний день карточки: к нему «+» цепляет медиа события — как кнопка в шапке
  // соревнования (первый день).
  const earliest = days[days.length - 1].swims[0];
  const headerDate = multiDay
    ? `${isoToDmy(days[days.length - 1].date)} – ${isoToDmy(days[0].date)}`
    : first.competition_date;
  const selectedName = preferredSwimmerId != null ? swimmerNames.get(preferredSwimmerId) ?? null : null;
  const tile = competitionTileData({
    name: first.competition_name,
    date: first.competition_date,
    category: first.category,
    is_championship: first.is_championship,
  });
  // Логотип клуба рядом с именем — клуб строки результата (на момент заплыва). Сперва личный
  // заплыв выбранного пловца; эстафета годится запасной — у неё клуб команды, тот же.
  const ownerClub = preferredSwimmerId == null ? null : (
    swims.find((s) => !s.is_relay && HelperSwimmer.resultBelongsToSwimmer(s, preferredSwimmerId))
    ?? swims.find((s) => HelperSwimmer.resultBelongsToSwimmer(s, preferredSwimmerId))
  )?.club_name || null;

  // ── Панель медиа: что открыто и где оно показано ──────────────────────────────
  const wide = useTwoPane(cardRef, paneRef, hasMedia);
  const [picked, setPicked] = useState<PaneSel | null>(null);
  const isOpenable = (sel: PaneSel | null): sel is PaneSel =>
    sel === 'comp' ? compMedia.length > 0 : sel != null && swims.some((s) => s.result_id === sel && s.media.length > 0);
  // Рядом со списком панель не бывает пустой: пока ничего не выбрано — первый результат с
  // медиа (или медиа соревнования). В портале по умолчанию закрыто.
  const defaultSel: PaneSel | null = swims.find((s) => s.media.length > 0)?.result_id ?? (compMedia.length > 0 ? 'comp' : null);
  const sel: PaneSel | null = isOpenable(picked) ? picked : wide ? defaultSel : null;

  // Карточка сузилась (окно, сайдбар) — выбор «рядом» не должен выскочить выезжающей панелью.
  const wasWide = useRef(wide);
  useEffect(() => {
    if (wasWide.current && !wide) setPicked(null);
    wasWide.current = wide;
  }, [wide]);

  const drawerOpen = !wide && sel != null;
  useEffect(() => {
    if (!drawerOpen) return undefined;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setPicked(null); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [drawerOpen]);
  // Пока панель уезжает, в ней остаётся последнее открытое — иначе она схлопывалась бы на ходу.
  const lastSel = useRef<PaneSel | null>(null);
  if (sel != null) lastSel.current = sel;
  const drawerSel = sel ?? (isOpenable(lastSel.current) ? lastSel.current : null);

  // Обложка YouTube: для какого результата открыт диалог.
  const [coverSwim, setCoverSwim] = useState<MySwimDto | null>(null);
  // Плакат — про все заплывы ОДНОГО пловца, поэтому кнопка есть только при выбранном пловце.
  const [posterOpen, setPosterOpen] = useState(false);

  const renderPane = (target: PaneSel, onClose?: () => void) => {
    if (target === 'comp') {
      return (
        <MediaPane
          key="comp"
          head={(
            <div>
              <div className="text-[15px] font-black leading-tight text-[var(--t-text)]">📎 Competition media</div>
              <div className="mt-0.5 text-[11.5px] font-bold text-[var(--t-text-2)]">Not tied to a swim</div>
            </div>
          )}
          media={compMedia}
          addLabel="Add photo or video to the competition"
          onAdd={() => cb.onAddCompMedia(earliest.competition_id, earliest.competition_name)}
          onClose={onClose}
          cb={cb}
        />
      );
    }
    const swim = swims.find((s) => s.result_id === target);
    if (!swim) return null;
    const owner = rowSwimmer(swim, swimmerNames, preferredSwimmerId);
    return (
      <MediaPane
        key={swim.result_id}
        head={<SwimPaneHead swim={swim} showSwimmerName={showSwimmerName} swimmerName={owner.name} swimmerId={owner.id} cb={cb} />}
        media={[...swim.media.filter((m) => m.media_type === 'video'), ...swim.media.filter((m) => m.media_type !== 'video')]}
        addLabel="Add photo or video"
        onAdd={() => cb.onAddVideo(swim)}
        onClose={onClose}
        actions={<CoverButton onClick={() => setCoverSwim(swim)} />}
        cb={cb}
      />
    );
  };

  return (
    <div ref={cardRef} className={`${hpCardCls} mmc${hasMedia ? '' : ' mmc--nomedia'}`}>
      <div className="mmc-head">
        <div className="mmc-head__id">
          {/* Плитка соревнования — общая CompetitionTile (сезон/кубок · буква категории ·
              возрастная лента). Данные считает общий competitionTileData, своей эвристики по
              названию тут нет: категория и флаг чемпионата приходят с сервера. Почти пустую
              плитку (без буквы и без возраста) не рисуем — текст шапки начинается от края. */}
          {hasTileContent(tile) && <CompetitionTile {...tile} size="sm" />}
          {/* Столбик «имя пловца / название / даты». `items-start` обязателен: ивритский
              текст с dir="auto" в блоке на всю ширину уезжает вправо, а имя должно стоять
              слева (хендофф §1.4). */}
          <div className="flex min-w-0 flex-1 flex-col items-start gap-0.5">
            {/* Имя выбранного пловца — крупно и первым: карточка сама отвечает «чьи это
                заплывы». В режиме All имени нет: в карточке заплывы разных избранных, и одно
                имя (и один логотип) над ними было бы враньём. */}
            {selectedName && (
              <span className="flex max-w-full items-center gap-2">
                <span dir="auto" className="min-w-0 text-left text-[18px] font-black leading-tight text-[var(--t-accent)]">
                  <SwimmerLink id={preferredSwimmerId}>{selectedName}</SwimmerLink>
                </span>
                {ownerClub && <UI_ClubLogo clubName={ownerClub} size={isDesktop ? 28 : 26} />}
              </span>
            )}
            <span className="flex max-w-full items-center gap-2">
              {/* Адрес ДНЯ открывает всё событие (И-64), поэтому у многодневки — ранний день. */}
              <a href={routes.competition(earliest.competition_id)} className={`block min-w-0 ${LINK_CLS}`}>
                <UI_CompetitionName
                  as="div"
                  className="text-left text-[15px] font-black leading-tight text-[var(--t-text)]"
                  name={first.competition_name}
                  isChampionship={first.is_championship}
                />
              </a>
              {anyPodium && <span className="flex-none" title="Podium finish" role="img" aria-label="Podium finish">🏅</span>}
            </span>
            <span className="mmc-head__meta">
              {headerDate} · {first.pool_type} · {swims.length} {swims.length === 1 ? 'swim' : 'swims'}
            </span>
          </div>
        </div>
        <div className="mmc-head__actions">
          {/* Счётчики — по ВСЕМУ медиа карточки: заплывов и «ко всему соревнованию». */}
          <Counter kind="video" count={videoCount} large />
          <Counter kind="photo" count={photoCount} large />
          {preferredSwimmerId != null && canMakePoster(swims) && <PosterButton onClick={() => setPosterOpen(true)} />}
          <button
            type="button"
            onClick={() => cb.onAddCompMedia(earliest.competition_id, earliest.competition_name)}
            aria-label="Add photo or video to the competition"
            title="+ Photo or video to the whole competition"
            className="mmc-btn mmc-btn--accent mmc-btn--lg"
          >
            <IconPlus />
          </button>
        </div>
      </div>

      <div className="mmc-body">
        <div className="mmc-list">
          {compMedia.length > 0 && (
            <CompMediaRow media={compMedia} selected={sel === 'comp'} sheet={!isDesktop} onOpen={() => setPicked('comp')} />
          )}
          {days.map((day, dayIdx) => (
            <React.Fragment key={day.date}>
              {multiDay && <DayDivider date={day.date} count={day.swims.length} />}
              {day.swims.map((s, i) => {
                const owner = rowSwimmer(s, swimmerNames, preferredSwimmerId);
                return (
                  <MySwimRow
                    key={s.result_id}
                    swim={s}
                    first={i === 0 && (multiDay || (dayIdx === 0 && compMedia.length === 0))}
                    showSwimmerName={showSwimmerName}
                    showDate={showDate}
                    swimmerName={owner.name}
                    swimmerId={owner.id}
                    selected={sel === s.result_id}
                    sheet={!isDesktop}
                    onOpen={() => setPicked(s.result_id)}
                    cb={cb}
                  />
                );
              })}
            </React.Fragment>
          ))}
        </div>
        {/* Слот панели «рядом со списком». Показан он или нет — решает container query
            (`.mmc-pane` в my-media.css); `useTwoPane` это только читает. */}
        <div ref={paneRef} className="mmc-pane">
          {wide && sel != null && <div className="mmc-pane__stick">{renderPane(sel)}</div>}
        </div>
      </div>

      {/* Карточка уже порога двух панелей: та же панель в общем `MobileFiltersDrawer` —
          выезжает справа на десктопе, нижняя шторка на телефоне. Портал в `body`, поэтому
          класс темы у него свой. */}
      {!wide && hasMedia && (
        <MobileFiltersDrawer
          variant={isDesktop ? 'side' : 'sheet'}
          className={`mmc-drawer ${deep}`}
          open={drawerOpen}
          onClose={() => setPicked(null)}
        >
          {drawerSel != null && renderPane(drawerSel, () => setPicked(null))}
        </MobileFiltersDrawer>
      )}

      {coverSwim && (
        <CoverDialogHost
          swim={coverSwim}
          swimmerName={rowSwimmer(coverSwim, swimmerNames, preferredSwimmerId).name}
          firstDay={days[days.length - 1].date}
          lastDay={days[0].date}
          onClose={() => setCoverSwim(null)}
        />
      )}
      {posterOpen && preferredSwimmerId != null && selectedName && (
        <PosterDialogHost
          swims={swims}
          swimmerId={preferredSwimmerId}
          swimmerName={selectedName}
          clubName={ownerClub}
          firstDay={days[days.length - 1].date}
          onClose={() => setPosterOpen(false)}
        />
      )}
    </div>
  );
}

/* ── Root ────────────────────────────────────────────────────────────────── */

/**
 * Ключ карточки: многодневка — ОДНА карточка на событие (решение Влада 08.11.2026; на сайте
 * событие и так открывается целиком, И-64), однодневное — по дню.
 */
function cardKey(competitionId: number, eventId: number | null): string {
  return eventId != null ? `e${eventId}` : `c${competitionId}`;
}

function SwimList({ swims, competitionMedia, showSwimmerName, swimmerNames, preferredSwimmerId = null, ...cb }: Props) {
  // Граница «телефон / десктоп» — та же, что у сайдбара и шторки фильтров страницы (lg).
  const isDesktop = useMediaQuery('(min-width: 1024px)');
  const deep = useDeepThemeClass();

  const groups = useMemo(() => {
    const byCard = new Map<string, MySwimDto[]>();
    for (const s of swims) {
      const key = cardKey(s.competition_id, s.event_id);
      const list = byCard.get(key) ?? [];
      list.push(s);
      byCard.set(key, list);
    }
    // swims приходят отсортированными по дате DESC — порядок групп наследуем.
    return Array.from(byCard.entries());
  }, [swims]);

  // Медиа «ко всему соревнованию» привязано к ДНЮ (кнопка в шапке соревнования цепляет к
  // первому). Карточка многодневки — на всё событие, поэтому медиа любого её дня — её.
  const compMediaByCard = useMemo(() => {
    const map = new Map<string, SwimMediaDto[]>();
    for (const m of competitionMedia) {
      if (m.competition_id == null) continue;
      const key = cardKey(m.competition_id, m.event_id ?? null);
      const list = map.get(key) ?? [];
      list.push(m);
      map.set(key, list);
    }
    return map;
  }, [competitionMedia]);

  return (
    <div className="flex flex-col gap-4">
      {groups.map(([key, g]) => (
        <CompetitionGroup
          key={key}
          swims={g}
          compMedia={compMediaByCard.get(key) ?? []}
          showSwimmerName={showSwimmerName}
          swimmerNames={swimmerNames}
          preferredSwimmerId={preferredSwimmerId}
          isDesktop={isDesktop}
          deep={deep}
          cb={cb}
        />
      ))}
    </div>
  );
}

export default SwimList;
