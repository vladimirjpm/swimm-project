import React, { useEffect, useMemo, useState } from 'react';
import { UserMediaPublicationDto } from '../../../hooks/useUserMedia';
import { fetchPublishTargets, PublishTargetDto } from '../use-all-my-media';
import { MySwimDto, SwimMediaDto } from '../use-my-swims';
import { STATUS_COLORS, CardStatus, derivedCardStatus, hpCardCls } from './status-styles';
import UI_SwimmStyleIcon from '../../components/mix/swimm-style-icon/swimm-style-icon';
import UI_SwimTime, { swimFlaggedRowProps } from '../../components/mix/swim-time/swim-time';
import UI_DateIcon from '../../components/mix/date-icon/date-icon';

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
  /** id → имя (из response.swimmers). */
  swimmerNames: Map<number, string>;
}

/* ── Хелперы ─────────────────────────────────────────────────────────────── */

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

function StatusPill({ status, isPublic }: { status: CardStatus; isPublic: boolean }) {
  const c = STATUS_COLORS[status];
  return (
    <span
      className="hp-mono inline-flex items-center gap-1 rounded-[6px] px-[8px] py-[2px] text-[10.5px] font-extrabold"
      style={{ color: c.text, border: `${isPublic ? '1.5px' : '1px'} solid ${c.border}`, background: c.bg }}
    >
      {status}{isPublic ? ' 🌐' : ''}
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
  const [targets, setTargets] = useState<PublishTargetDto[] | null>(null);
  const [group, setGroup] = useState<number | ''>('');
  const [level, setLevel] = useState<'members' | 'public'>('members');
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    fetchPublishTargets(m.id).then((t) => { if (alive) setTargets(t); });
    return () => { alive = false; };
  }, [m.id]);

  const share = async () => {
    if (group === '' || busy) return;
    setBusy(true);
    await cb.onSubmitShare(m.id, group, level);
    setBusy(false);
    setGroup('');
  };

  const withdrawable = pubs.filter((p) => p.status === 'pending' || p.status === 'approved');

  return (
    <div className="flex flex-wrap items-center gap-2 py-[6px]">
      <SourceChip m={m} onClick={() => cb.onPlay(m)} />
      <span className="w-[120px] shrink-0"><StatusPill status={status} isPublic={isPublic} /></span>
      <LikeChip m={m} onToggle={() => cb.onToggleLike(m)} />
      <div className="ml-auto flex flex-wrap items-center gap-1.5">
        {targets != null && targets.length > 0 && (
          <>
            <select
              value={group}
              onChange={(e) => setGroup(e.target.value === '' ? '' : Number(e.target.value))}
              className="rounded-[7px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-1.5 py-[3px] text-[11px] text-[#f3f8fd]"
            >
              <option value="">Group…</option>
              {targets.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
            <select
              value={level}
              onChange={(e) => setLevel(e.target.value as 'members' | 'public')}
              className="rounded-[7px] border border-[rgba(125,211,252,0.3)] bg-[rgba(2,10,24,0.5)] px-1.5 py-[3px] text-[11px] text-[#f3f8fd]"
            >
              <option value="members">Members</option>
              <option value="public">Public 🌐</option>
            </select>
            <button
              type="button"
              disabled={group === '' || busy}
              onClick={share}
              className="hp-mono rounded-[7px] border-none px-2.5 py-[4px] text-[10.5px] font-extrabold disabled:opacity-40"
              style={{ background: '#38ef8f', color: '#04101f' }}
            >
              Share
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
function MySwimRow({ swim, showSwimmerName, swimmerName, cb }: {
  swim: MySwimDto;
  showSwimmerName: boolean;
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

  return (
    <>
      {/* Desktop row */}
      <div
        {...flagged}
        className={`hidden items-center gap-3 px-5 py-[10px] sm:flex${flagged.className ? ` ${flagged.className}` : ''}`}
        style={{ background: noVideo ? 'rgba(2,10,24,0.25)' : 'transparent' }}
      >
        <span className="w-[26px] shrink-0 text-center text-[14px] leading-tight">
          {medal(swim.place)}
          {swim.is_pb && <span className="block text-[10px] text-[#ffca7a]" title="Personal best">⚡</span>}
        </span>
        <span className="w-[60px] shrink-0">
          <span className="block text-[12.5px] font-extrabold">{swim.place != null ? `#${swim.place}` : '—'}</span>
          <span className="block text-[10.5px] text-[rgba(203,224,240,0.45)]">{swim.points > 0 ? `${swim.points} pts` : ''}</span>
        </span>
        <span
          className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[#2c3d52] text-[9px] font-black text-[#bfe0f5]"
          style={{ opacity: noVideo ? 0.5 : 1 }}
          title={swimmerName}
        >
          {swimmerName.trim().charAt(0).toUpperCase()}
        </span>
        <span className="flex min-w-[120px] items-center gap-2 overflow-hidden text-[13.5px] font-extrabold" style={{ color: noVideo ? 'rgba(226,240,252,0.55)' : '#f3f8fd' }}>
          <UI_SwimmStyleIcon
            styleName={swim.style}
            styleLen={swim.distance}
            styleType="icon-len"
            className="w-[46px] shrink-0 rounded-[8px] bg-[rgba(226,240,252,0.92)] px-1 py-0.5 text-[15px]"
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
        <span className="w-[92px] shrink-0">
          <UI_DateIcon
            styleType="row-style-1"
            date={swim.date}
            fontClassName="hp-mono text-[10.5px] text-[rgba(203,224,240,0.45)]"
          />
        </span>
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
        <span className="w-5 shrink-0 text-center text-[13px] leading-tight">
          {medal(swim.place)}
          {swim.is_pb && <span className="block text-[9px] text-[#ffca7a]">⚡</span>}
        </span>
        <UI_SwimmStyleIcon
          styleName={swim.style}
          styleLen={swim.distance}
          styleType="icon-len"
          className="w-[40px] shrink-0 rounded-[8px] bg-[rgba(226,240,252,0.92)] px-1 py-0.5 text-[14px]"
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
              {swim.place != null ? `#${swim.place}` : ''}{swim.points > 0 ? ` · ${swim.points} pts` : ''}
            </span>
            <CheerChip swim={swim} emphasized={swim.is_pb} onToggle={() => cb.onToggleCheer(swim)} stop />
          </span>
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

function CompetitionGroup({ swims, compMedia, showSwimmerName, swimmerNames, cb }: {
  swims: MySwimDto[];
  compMedia: SwimMediaDto[];
  showSwimmerName: boolean;
  swimmerNames: Map<number, string>;
  cb: SwimListCallbacks;
}) {
  const [mediaOpen, setMediaOpen] = useState(false);
  const first = swims[0];
  const videoCount = swims.reduce((n, s) => n + s.media.filter((m) => m.media_type === 'video').length, 0);
  const anyPodium = swims.some((s) => s.place != null && s.place <= 3);
  const anyRecord = swims.some((s) => s.is_pb);

  return (
    <div className={`${hpCardCls} overflow-hidden`}>
      <div className="flex flex-wrap items-center gap-2.5 border-b border-[rgba(125,211,252,0.15)] px-4 py-3 sm:px-5">
        <span dir="auto" className="max-w-full overflow-hidden text-ellipsis whitespace-nowrap text-[15px] font-black text-[#f3f8fd]">
          {first.competition_name}
        </span>
        <span className="hp-mono text-[11px] font-extrabold text-[#7dd3fc]">{first.competition_date}</span>
        <span className="text-[11.5px] text-[rgba(203,224,240,0.5)]">{first.pool_type}</span>
        {anyPodium && <span title="Podium finish">🏅</span>}
        {anyRecord && (
          <span className="hp-mono rounded-[5px] border border-[rgba(255,202,122,0.5)] bg-[rgba(255,202,122,0.08)] px-1.5 py-[1px] text-[9.5px] font-extrabold text-[#ffca7a]">⚡ REC</span>
        )}
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
        {(['', 'PLACE', '', 'SWIM', 'TIME', 'DATE', '🎉', 'MEDIA'] as const).map((h, i) => (
          <span
            key={i}
            className="hp-mono text-[9px] font-extrabold uppercase tracking-[0.14em] text-[rgba(125,211,252,0.45)]"
            style={{ width: [26, 60, 24, undefined, 84, 92, 52, 330][i], flex: i === 3 ? 1 : undefined, flexShrink: 0 }}
          >
            {h}
          </span>
        ))}
      </div>

      <div className="divide-y divide-[rgba(125,211,252,0.08)]">
        {swims.map((s) => (
          <MySwimRow
            key={s.result_id}
            swim={s}
            showSwimmerName={showSwimmerName}
            swimmerName={swimmerNames.get(s.swimmer_id) ?? '?'}
            cb={cb}
          />
        ))}
      </div>
    </div>
  );
}

/* ── Root ────────────────────────────────────────────────────────────────── */

function SwimList({ swims, competitionMedia, showSwimmerName, swimmerNames, ...cb }: Props) {
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
          cb={cb}
        />
      ))}
    </div>
  );
}

export default SwimList;
