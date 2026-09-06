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

// Список заплывов, сгруппированный по соревнованиям — ядро My media v3
// (README design_handoff_my_swims_v3,1 §7). Desktop: строки с фикс. колонками
// + разворачиваемые media-панели с inline share; mobile: компактные строки,
// действия — в bottom sheet родителя (onOpenActions).

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
  /** Mobile: тап по строке → actions bottom sheet у родителя. */
  onOpenActions: (swim: MySwimDto) => void;
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
  'inline-flex items-center rounded-full border border-[rgba(125,211,252,0.55)] '
  + 'bg-[rgba(125,211,252,0.14)] px-2 py-[1px] text-[10px] font-black leading-[1.4] text-[#7dd3fc]';

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
function RowVisibility({ swim, publicationsByMedia, compact }: {
  swim: MySwimDto;
  publicationsByMedia: Map<number, UserMediaPublicationDto[]>;
  /** Мобильная строка: одна строка с обрезкой — там колонка узкая, а высота дороже ширины. */
  compact?: boolean;
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

  if (compact) {
    return (
      <span
        title={pillTitle(pubs)}
        className="hp-mono inline-block max-w-full truncate rounded-[5px] px-[5px] py-[1px] text-[8.5px] font-extrabold"
        style={{ color: c.text, border: `1px solid ${c.border}`, background: c.bg }}
      >
        {status === 'private' ? 'private' : <>{head}{isPublic ? ' 🌐' : ''} <span dir="auto">{tail}</span></>}
      </span>
    );
  }

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
      className="hp-mono w-[120px] shrink-0 rounded-[7px] border border-[rgba(125,211,252,0.4)] bg-[rgba(125,211,252,0.08)] px-2 py-[4px] text-left text-[10.5px] font-extrabold text-[#7dd3fc]"
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
        border: `1px solid ${liked ? 'rgba(255,125,156,0.55)' : 'rgba(125,211,252,0.25)'}`,
        background: liked ? 'rgba(255,125,156,0.1)' : 'transparent',
        color: liked ? '#ff7d9c' : m.likes_count > 0 ? 'rgba(203,224,240,0.7)' : 'rgba(203,224,240,0.35)',
      }}
      title={liked ? 'Remove like' : 'Like'}
    >
      ❤ {m.likes_count}
    </button>
  );
}

function CheerChip({ swim, emphasized, onToggle, stop }: {
  swim: MySwimDto; emphasized: boolean; onToggle: () => void; stop?: boolean;
}) {
  const on = swim.my_cheer;
  return (
    <button
      type="button"
      onClick={(e) => { if (stop) e.stopPropagation(); onToggle(); }}
      className="hp-mono whitespace-nowrap rounded-[7px] px-2 py-[3px] text-[10.5px] font-extrabold"
      style={{
        border: `1px solid ${on ? 'rgba(255,202,122,0.55)' : emphasized ? 'rgba(255,202,122,0.35)' : 'rgba(125,211,252,0.25)'}`,
        background: on ? 'rgba(255,202,122,0.1)' : 'transparent',
        color: on ? '#ffca7a' : emphasized ? 'rgba(255,202,122,0.8)' : swim.congrats_count > 0 ? 'rgba(203,224,240,0.7)' : 'rgba(203,224,240,0.35)',
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
              className="rounded-[7px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-1.5 py-[3px] text-[11px] text-[#f3f8fd]"
            >
              <option value="">Group…</option>
              {options.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
            <select
              value={level}
              onChange={(e) => setLevel(e.target.value as 'members' | 'public')}
              className="rounded-[7px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-1.5 py-[3px] text-[11px] text-[#f3f8fd]"
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
              style={{ background: '#38ef8f', color: '#04101f' }}
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
            className="hp-mono rounded-[7px] border border-[rgba(255,202,122,0.45)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[#ffca7a]"
            title={`Withdraw from ${p.hub_group_name}`}
          >
            Withdraw
          </button>
        ))}
        <button
          type="button"
          onClick={() => cb.onDelete(m.id)}
          className="hp-mono rounded-[7px] border border-[rgba(239,83,80,0.45)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[#ef5350]"
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
 * Почему не сведена в общую: общая строка — двухлинейная КАРТОЧКА результата, а
 * здесь — ПЛОТНАЯ ТАБЛИЦА управления медиа: фиксированные колонки под своей шапкой
 * (PLACE / SWIM / TIME / DATE / congrats / MEDIA), зона действий на 330px и разворачиваемая
 * панель медиа под строкой. Карточка втрое выше и ломает выравнивание по колонкам, а
 * чтобы вместить медиа-кнопки, RELAY, метку PB и тап-по-строке, в общий компонент
 * пришлось бы добавить слот на каждый угол — ровно то, от чего план общей строки
 * отказался (§3.1 `docs/plans/swim-row-shared-component-plan.md`).
 *
 * Общее берётся ячейками: `UI_SwimmStyleIcon`, `UI_SwimTime` вместе с
 * `swimFlaggedRowProps` (носитель спорного времени) и `UI_DateIcon` (формат даты один на продукт).
 */
function MySwimRow({ swim, showSwimmerName, showDate, swimmerName, cb }: {
  swim: MySwimDto;
  showSwimmerName: boolean;
  /** Колонка DATE — только у многодневок (см. `sameDay`). */
  showDate: boolean;
  swimmerName: string;
  cb: SwimListCallbacks;
}) {
  const [expanded, setExpanded] = useState(false);
  const videos = swim.media.filter((m) => m.media_type === 'video');
  const photos = swim.media.filter((m) => m.media_type === 'image');
  const hasMedia = swim.media.length > 0;
  const noVideo = videos.length === 0;

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
      {/* Desktop row */}
      <div
        {...flagged}
        className={`hidden items-center gap-3 px-5 py-[10px] sm:flex${flagged.className ? ` ${flagged.className}` : ''}`}
        style={{ background: noVideo ? 'rgba(2,10,24,0.25)' : 'transparent' }}
      >
        {/* Порядок: место — первым, следом столбик «медаль + метки». Место по центру своей
            колонки и одного кегля с медалью: после снятия строки очков оно оставалось
            прижатым влево и съезжало относительно медали. */}
        <span className="w-[46px] shrink-0 self-center text-center text-[17px] font-black leading-none">
          {swim.place != null ? `#${swim.place}` : '—'}
        </span>
        <span className="w-[46px] shrink-0 text-center text-[14px] leading-tight">
          {medal(swim.place)}
          <BestMark record={recordMark} pb={swim.is_pb} sb={swim.is_sb} stacked />
        </span>
        <span className="flex min-w-[120px] items-center justify-center gap-2 overflow-hidden text-[13.5px] font-extrabold" style={{ color: noVideo ? 'rgba(226,240,252,0.55)' : '#f3f8fd' }}>
          <UI_SwimmStyleIcon
            styleName={swim.style}
            styleLen={swim.distance}
            styleType="icon-len"
            lenPlacement="below"
            size={64}
            className="src-swim-list shrink-0 rounded-[8px] bg-[rgba(226,240,252,0.92)] px-1 py-0.5"
          />
          {showSwimmerName && <span className="truncate text-[11px] font-bold text-[rgba(203,224,240,0.5)]">{swimmerName}</span>}
          {swim.is_relay && (
            <span className="hp-mono ml-1 rounded-[5px] border border-[rgba(125,211,252,0.4)] px-1.5 py-[1px] text-[9px] font-extrabold text-[#7dd3fc]">RELAY</span>
          )}
        </span>
        <span className="hp-mono w-[84px] shrink-0 text-[13.5px] font-extrabold text-[#7dd3fc]">
          {swim.time_fail ? 'DSQ' : (
            <UI_SwimTime time={swim.time} quality={quality} />
          )}
        </span>
        {/* Дата — общим `UI_DateIcon`, а не сырой ISO-строкой из API: формат даты живёт
            в одном месте, а «2026-07-30» здесь спорило с «30 JUL 2026» на всех остальных экранах. */}
        {showDate && (
          <span className="w-[92px] shrink-0">
            <UI_DateIcon
              styleType="row-style-1"
              date={swim.date}
              fontClassName="hp-mono text-[10.5px] text-[rgba(203,224,240,0.45)]"
            />
          </span>
        )}
        <span className="w-[52px] shrink-0">
          <CheerChip swim={swim} emphasized={swim.is_pb} onToggle={() => cb.onToggleCheer(swim)} />
        </span>
        <span className="flex w-[330px] shrink-0 items-center gap-2">
          {videos.length > 0 && (
            <button type="button" onClick={() => setExpanded((v) => !v)} className="hp-mono rounded-[7px] border border-[rgba(125,211,252,0.45)] bg-[rgba(125,211,252,0.1)] px-2 py-[3px] text-[10.5px] font-extrabold text-[#7dd3fc]">
              ▶ {videos.length}
            </button>
          )}
          {photos.length > 0 && (
            <button type="button" onClick={() => setExpanded((v) => !v)} className="hp-mono rounded-[7px] border border-[rgba(125,211,252,0.25)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[rgba(125,211,252,0.6)]">
              🖼 {photos.length}
            </button>
          )}
          {noVideo && (
            <button
              type="button"
              onClick={() => cb.onAddVideo(swim)}
              className="hp-mono rounded-[7px] border border-dashed border-[rgba(56,239,143,0.5)] bg-transparent px-2.5 py-[4px] text-[10.5px] font-extrabold text-[#38ef8f]"
            >
              + Add video
            </button>
          )}
          {hasMedia && <RowVisibility swim={swim} publicationsByMedia={cb.publicationsByMedia} />}
          {hasMedia && (
            <button type="button" onClick={() => setExpanded((v) => !v)} className="ml-auto border-none bg-transparent text-[11px] text-[rgba(125,211,252,0.6)]">
              {expanded ? '▴' : '▾'}
            </button>
          )}
        </span>
      </div>

      {/* Mobile row */}
      <div
        {...flagged}
        className={`flex cursor-pointer items-center gap-2.5 px-4 py-[10px] sm:hidden${flagged.className ? ` ${flagged.className}` : ''}`}
        style={{ background: noVideo ? 'rgba(2,10,24,0.25)' : 'transparent' }}
        onClick={() => hasMedia && cb.onOpenActions(swim)}
      >
        <span className="w-[34px] shrink-0 text-center text-[13px] leading-tight">
          {medal(swim.place)}
          <BestMark record={recordMark} pb={swim.is_pb} sb={swim.is_sb} stacked />
        </span>
        <UI_SwimmStyleIcon
          styleName={swim.style}
          styleLen={swim.distance}
          styleType="icon-len"
          lenPlacement="below"
          size={64}
          className="src-swim-list shrink-0 rounded-[8px] bg-[rgba(226,240,252,0.92)] px-1 py-0.5"
        />
        <span className="min-w-0 flex-1">
          {swim.is_relay && (
            <span className="block">
              <span className="hp-mono rounded-[5px] border border-[rgba(125,211,252,0.4)] px-1 py-[1px] text-[8.5px] font-extrabold text-[#7dd3fc]">RELAY</span>
            </span>
          )}
          <span className="mt-0.5 flex items-center gap-2">
            <span className="hp-mono text-[12px] font-extrabold text-[#7dd3fc]">
              {swim.time_fail ? 'DSQ' : (
                <UI_SwimTime time={swim.time} quality={quality} />
              )}
            </span>
            <span className="text-[10px] text-[rgba(203,224,240,0.45)]">
              {swim.place != null ? `#${swim.place}` : ''}
            </span>
            <CheerChip swim={swim} emphasized={swim.is_pb} onToggle={() => cb.onToggleCheer(swim)} stop />
          </span>
          {hasMedia && (
            <span className="mt-[3px] block">
              <RowVisibility swim={swim} publicationsByMedia={cb.publicationsByMedia} compact />
            </span>
          )}
        </span>
        <span className="flex shrink-0 items-center gap-1.5">
          {photos.length > 0 && <span className="text-[11px] text-[rgba(125,211,252,0.55)]">🖼</span>}
          {videos.length > 0 ? (
            <span className="hp-mono rounded-[7px] border border-[rgba(125,211,252,0.45)] bg-[rgba(125,211,252,0.1)] px-1.5 py-[2px] text-[10px] font-extrabold text-[#7dd3fc]">▶ {videos.length}</span>
          ) : (
            <button
              type="button"
              onClick={(e) => { e.stopPropagation(); cb.onAddVideo(swim); }}
              className="hp-mono rounded-[7px] border border-dashed border-[rgba(56,239,143,0.5)] bg-transparent px-2 py-[3px] text-[10px] font-extrabold text-[#38ef8f]"
            >
              + Video
            </button>
          )}
        </span>
      </div>

      {/* Expanded media panel (desktop) */}
      {expanded && hasMedia && (
        <div className="hidden bg-[rgba(2,10,24,0.4)] px-5 py-2 pl-[116px] sm:block">
          {[...videos, ...photos].map((m) => (
            <MediaLine key={m.id} m={m} pubs={cb.publicationsByMedia.get(m.id) ?? []} cb={cb} />
          ))}
          <button
            type="button"
            onClick={() => cb.onAddVideo(swim)}
            className="hp-mono my-1.5 rounded-[7px] border border-dashed border-[rgba(56,239,143,0.4)] bg-transparent px-2.5 py-[4px] text-[10.5px] font-extrabold text-[rgba(56,239,143,0.8)]"
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
  const columns: { label: string; width?: number; center?: boolean }[] = [
    { label: 'PLACE', width: 46 },
    { label: '', width: 46 },
    { label: 'SWIM', center: true },
    { label: 'TIME', width: 84 },
    ...(showDate ? [{ label: 'DATE', width: 92 }] : []),
    { label: '🎉', width: 52 },
    { label: 'MEDIA', width: 330 },
  ];

  return (
    <div className={`${hpCardCls} overflow-hidden`}>
      <div className="flex flex-wrap items-center gap-2.5 border-b border-[rgba(125,211,252,0.15)] px-4 py-3 sm:px-5">
        {/* Плитка соревнования — общая CompetitionTile (сезон/кубок · буква категории ·
            возрастная лента). Данные считает общий competitionTileData, своей эвристики по
            названию тут нет: категория и флаг чемпионата приходят с сервера. */}
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
          <span dir="auto" className="text-[18px] font-black leading-tight text-[#7dd3fc]">
            {selectedName}
          </span>
        )}
        <span dir="auto" className="max-w-full overflow-hidden text-ellipsis whitespace-nowrap text-[15px] font-black text-[#f3f8fd]">
          {first.competition_name}
        </span>
        <span className="hp-mono text-[11px] font-extrabold text-[#7dd3fc]">{first.competition_date}</span>
        <span className="text-[11.5px] text-[rgba(203,224,240,0.5)]">{first.pool_type}</span>
        {anyPodium && <span title="Podium finish">🏅</span>}
        <BestMark pb={anyPb} sb={anySb} />
        <span className="ml-auto flex items-center gap-2">
          <span className="hidden text-[11px] font-bold text-[rgba(203,224,240,0.45)] sm:inline">
            {swims.length} {swims.length === 1 ? 'swim' : 'swims'} · {videoCount} {videoCount === 1 ? 'video' : 'videos'}
          </span>
          {compMedia.length > 0 && (
            <button
              type="button"
              onClick={() => setMediaOpen((v) => !v)}
              className="hp-mono rounded-[7px] border border-[rgba(125,211,252,0.35)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[#7dd3fc]"
            >
              📎 {compMedia.length} {mediaOpen ? '▴' : '▾'}
            </button>
          )}
          <button
            type="button"
            onClick={() => cb.onAddCompMedia(first.competition_id, first.competition_name)}
            className="hp-mono rounded-[7px] border border-dashed border-[rgba(56,239,143,0.5)] bg-transparent px-2 py-[3px] text-[10.5px] font-extrabold text-[#38ef8f]"
          >
            + Photo/Video
          </button>
        </span>
      </div>

      {mediaOpen && compMedia.length > 0 && (
        <div className="border-b border-[rgba(125,211,252,0.12)] bg-[rgba(2,10,24,0.35)] px-4 py-2 sm:px-5">
          <p className="hp-mono m-0 mb-1 text-[9px] font-extrabold uppercase tracking-[0.14em] text-[rgba(125,211,252,0.45)]">
            Competition media · not tied to a swim
          </p>
          {compMedia.map((m) => (
            <MediaLine key={m.id} m={m} pubs={cb.publicationsByMedia.get(m.id) ?? []} cb={cb} />
          ))}
        </div>
      )}

      {/* Column header (desktop) */}
      <div className="hidden items-center gap-3 px-5 py-1.5 sm:flex">
        {columns.map((c, i) => (
          <span
            key={i}
            className={`hp-mono text-[9px] font-extrabold uppercase tracking-[0.14em] text-[rgba(125,211,252,0.45)]${c.center ? ' text-center' : ''}`}
            style={{ width: c.width, flex: c.width === undefined ? 1 : undefined, flexShrink: 0 }}
          >
            {c.label}
          </span>
        ))}
      </div>

      <div className="divide-y divide-[rgba(125,211,252,0.08)]">
        {swims.map((s) => (
          <MySwimRow
            key={s.result_id}
            swim={s}
            showSwimmerName={showSwimmerName}
            showDate={showDate}
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
