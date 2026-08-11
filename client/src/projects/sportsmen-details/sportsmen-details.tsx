import React, { useState, useMemo, useEffect } from 'react';
import './sportsmen-details.css';
import { useAppSelector } from '../../store/store';
import { useFavoritesContext } from '../../hooks/favorites-context';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useAthleteCareer, AthleteCareer } from '../../hooks/useAthleteCareer';
import { useUserMedia, useMyMediaPublications, UserMediaDto } from '../../hooks/useUserMedia';
import { useLogligStatus } from '../../hooks/useLogligStatus';
import Helper from '../../utils/helpers/data-helper'
import { HelperMedia } from '../../utils/helpers';
import { routes } from '../../utils/routes';
import UI_ClubIcon from '../components/mix/club-icon/club-icon';
import UI_NormativeLevelIcon from '../components/mix/normative-level-icon/normative-level-icon';
import UI_MedalIcon from '../components/mix/medal-icon/medal-icon';
import UI_PositionBadge from '../components/mix/position-badge/position-badge';
import ResultRowDateInfo from '../results-table/components/result-row-date-info';
import UI_PoolIcon from '../components/mix/pool-icon/pool-icon';
import UI_SwimmerTimeCell from '../components/mix/swimmer-time-cell/swimmer-time-cell';
import { swimFlaggedRowProps } from '../components/mix/swim-time/swim-time';
import UI_FlagEmoji from '../components/mix/flag-icon/flag-icon';
import UI_RecordCount from '../components/mix/record-count/record-count';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../utils/interfaces/results';
import { applyCombinedPositions } from '../../utils/helpers/recalculate-positions';

/** "15/02/2026" → "Feb 2026" (для подписи сегмента-переключателя). */
const formatMonthYear = (date?: string): string => {
  if (!date) return '';
  const parts = date.split('/');
  if (parts.length !== 3) return date;
  const dt = new Date(Number(parts[2]), Number(parts[1]) - 1, 1);
  if (isNaN(dt.getTime())) return date;
  return `${dt.toLocaleString('en', { month: 'short' })} ${parts[2]}`;
};

/** Медаль с тёмным поповером (как у гейджа уровня): список заплывов, за которые она дана. */
const MedalWithTooltip: React.FC<{ place: '1' | '2' | '3'; count: number; notes: string[] }> = ({ place, count, notes }) => (
  <div className="group relative">
    <UI_MedalIcon place={place} styleType="icon-place" styleSize="medal-24" placeReplace={String(count)} />
    {notes.length > 0 && (
      <div className="pointer-events-none absolute bottom-full left-1/2 z-20 mb-1.5 hidden -translate-x-1/2 flex-col items-center gap-0.5 whitespace-nowrap rounded-lg bg-[#1f2733] px-2.5 py-1.5 shadow-lg group-hover:flex">
        {notes.map((n, i) => (
          <span key={i} className="text-[11px] font-semibold text-[#c4cbd6]">
            {n}
          </span>
        ))}
      </div>
    )}
  </div>
);

type Scope = 'competition' | 'alltime';

function SportsmenDetails() {
 const filters = useAppSelector((state) => state.filterSelected);
  const selectedSource = useAppSelector((state) => state.dataSourceSelected);
  const isMastersSource = !!selectedSource?.is_masters;
  const isAwardSource = !!selectedSource?.is_award;
  const { isAuthenticated, primarySwimmerId, favoriteSwimmerIds, setMeBySwimmer, toggleFavoriteSwimmer } = useFavoritesContext();
  const { openLoginModal } = useLoginModal();

  // Режим карточки: данные этого соревнования / карьерные (all-time).
  // При смене спортсмена всегда сбрасываемся на «соревнование» (TASK.md).
  const [scope, setScope] = useState<Scope>('competition');
  useEffect(() => {
    setScope('competition');
  }, [filters.selected_name]);
  const career = useAthleteCareer(filters.selected_name);

  if (!selectedSource || !selectedSource.results?.length) {
    return <div className="text-[var(--theme-mode-text-muted)] italic">No data source selected.</div>;
  }

  // Применяем recalculation если включён флаг
  const isRecalculated = !!filters.is_recalculated;
  const effectiveResults = useMemo(() => {
    const raw = selectedSource.results ?? [];
    if (!isRecalculated) return raw;
    // Места объединённого зачёта — с сервера (combined_place), фоллбек — локальный пересчёт.
    return applyCombinedPositions(raw);
  }, [selectedSource, isRecalculated]);

  const sortedBestResults = Helper.getBestResultsByStyle(effectiveResults, filters.selected_name, isMastersSource);
  // Все заплывы на этом соревновании (контент режима «соревнование»)
  const allSwimmerResults = Helper.getAllResultsByName(effectiveResults, filters.selected_name, isMastersSource);
  const pointsSum = Helper.getInternationalPointsSumByName(effectiveResults, filters.selected_name);
  const medalCounts = Helper.getMedalCountsByName(effectiveResults, filters.selected_name, isAwardSource);
  const firstResult = sortedBestResults[0];
  //console.log('firstResult sportsmen:', firstResult);

  const swimmerId = (firstResult as any)?.swimmer_id as number | undefined;
  const isMe = isAuthenticated && swimmerId != null && swimmerId === primarySwimmerId;
  const isFav = isAuthenticated && swimmerId != null && favoriteSwimmerIds.has(swimmerId);
  const canMark = isAuthenticated && swimmerId != null;
  // Гость (фаза 4.5): вместо звезды/сердечка и «Моих ссылок» — компактный CTA «войти».
  const showGuestCta = !isAuthenticated && swimmerId != null;

  // Медиа пловца (2A) — один хук на карточку, используется и в MyMediaSection,
  // и для иконки «есть видео» на строках результатов (только owner-view).
  const userMedia = useUserMedia(swimmerId);
  // Loglig ID (docs/loglig-id-plan.md, шаг 6 — хвост): статус привязки + краудсорс-предложение.
  const logligStatus = useLogligStatus(swimmerId);
  const mediaResultIds = useMemo(
    () => new Set(userMedia.media.filter((m) => typeof m.result_id === 'number').map((m) => m.result_id as number)),
    [userMedia.media]
  );

  // Просмотр уже добавленного медиа на строке (иконка play → onOpenMedia). Добавление
  // медиа с публичных страниц убрано (docs/tasks/public-pages-media-cleanup-sonnet.md) —
  // единственная точка входа для добавления теперь client/media.html.
  const [openMediaResultId, setOpenMediaResultId] = useState<number | null>(null);
  useEffect(() => {
    setOpenMediaResultId(null);
  }, [filters.selected_name]);

  const base = import.meta.env.BASE_URL;
  const gender = firstResult.event_style_gender || 'female';
  // alpha-3 из данных (ISR…) конвертирует сам UI_FlagEmoji; дефолт — Израиль.
  const countryCode = firstResult.country || 'il';

  return (
    <div className="sportsmen-details">
      <div
        className='section-top flex flex-col w-full max-h-[90vh] rounded-[22px] overflow-hidden border border-[var(--theme-mode-modal-border)]'
        style={{ background: 'var(--theme-mode-surface)', boxShadow: 'var(--theme-mode-modal-shadow)' }}
      >
          {/* Identity-бар (design_handoff_athlete_card §1): фото 76px + флаг,
              справа RTL — имя, возраст, чип клуба */}
          <div className='relative px-[18px] pt-4 pb-3.5 flex gap-3.5 items-center' style={{ background: 'var(--theme-mode-hero-grad)' }}>
            <div className='flex flex-col items-center gap-2 shrink-0'>
              <div className='relative'>
                <img
                  src={`${base}images/swimmers/default-${gender}.png`}
                  alt={filters.selected_name}
                  className='block h-[76px] w-[76px] rounded-full object-cover'
                  style={{ border: '3px solid var(--theme-primary)', background: '#fff' }}
                  onError={(e) => {
                    e.currentTarget.src = `${base}images/swimmers/default-female.png`;
                  }}
                />
                <div className='absolute -bottom-1.5 -right-1' style={{ filter: 'drop-shadow(0 2px 6px rgba(0,0,0,0.5))' }}>
                  <UI_FlagEmoji countryCode={countryCode} size={'28x21'} className='rounded' />
                </div>
              </div>
              {/* избранное / «это я» — компактно под фото (фича favorites, вне хендоффа) */}
              {canMark && (
                <div className='flex items-center gap-1.5'>
                  <button
                    type="button"
                    onClick={() => toggleFavoriteSwimmer(swimmerId!)}
                    title={isFav ? 'Remove from favorites' : 'Add to favorites'}
                    aria-pressed={isFav}
                    className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
                    style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
                  >
                    <svg width="17" height="17" viewBox="0 0 24 24" fill={isFav ? '#e23b5a' : 'none'} stroke={isFav ? '#e23b5a' : '#9aa3af'} strokeWidth="2">
                      <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
                    </svg>
                  </button>
                  <button
                    type="button"
                    onClick={() => setMeBySwimmer(swimmerId!)}
                    title={isMe ? 'This is me — unmark' : 'Mark: this is me'}
                    aria-pressed={isMe}
                    className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
                    style={{ background: isMe ? '#fff6da' : 'rgba(255,255,255,0.75)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
                  >
                    <svg width="17" height="17" viewBox="0 0 24 24" fill={isMe ? '#f5b800' : 'none'} stroke={isMe ? '#d99a00' : '#9aa3af'} strokeWidth="1.8" strokeLinejoin="round">
                      <path d="M12 2.5l2.9 5.9 6.5.95-4.7 4.6 1.1 6.45L12 21.3l-5.8 3.05 1.1-6.45-4.7-4.6 6.5-.95z" />
                    </svg>
                  </button>
                </div>
              )}
              {showGuestCta && (
                <button
                  type="button"
                  onClick={openLoginModal}
                  title="Sign in to save favorites"
                  className="w-8 h-8 rounded-full inline-flex items-center justify-center leading-none hover:scale-110 transition-transform"
                  style={{ background: 'rgba(255,255,255,0.9)', boxShadow: '0 1px 3px rgba(0,0,0,0.12)' }}
                >
                  <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#9aa3af" strokeWidth="2">
                    <path d="M12 21s-7.5-4.6-10-9.3C.4 8.3 2 5 5.2 5c2 0 3.3 1.1 4.1 2.3C10.1 6.1 11.4 5 13.4 5 16.6 5 18.2 8.3 16.6 11.7 14.1 16.4 12 21 12 21z" />
                  </svg>
                </button>
              )}
              {swimmerId != null && (
                <LogligSuggestBadge
                  status={logligStatus.status}
                  profileUrl={logligStatus.profileUrl}
                  isAuthenticated={isAuthenticated}
                  suggest={logligStatus.suggest}
                />
              )}
            </div>
            <div className='flex-1 min-w-0 flex flex-col gap-1.5 items-end'>
              <div dir='rtl' className='text-xl font-extrabold leading-tight truncate max-w-full' style={{ color: 'var(--theme-mode-text)' }}>
                {filters.selected_name}
              </div>
              <div className='flex items-center gap-2 text-[11px] font-semibold' style={{ color: 'var(--theme-mode-text-muted)' }}>
                <UI_RecordCount swimmerName={filters.selected_name} />
                <span>{firstResult.event_style_age} year ({firstResult.birth_year})</span>
              </div>
              {/* Диплинк на самостоятельную страницу пловца (swimmer.html?swimmer=<id>). */}
              {swimmerId != null && (
                <a
                  href={routes.swimmer(swimmerId)}
                  className='text-[11px] font-bold hover:underline'
                  style={{ color: 'var(--theme-primary)' }}
                >
                  Open full profile →
                </a>
              )}
              <div
                dir='rtl'
                className='flex items-center gap-2 rounded-full py-1 pl-3.5 pr-[5px] max-w-full'
                style={{ background: 'var(--theme-mode-input-bg)' }}
              >
                <span
                  className='flex h-8 w-8 shrink-0 items-center justify-center rounded-full p-[2px]'
                  style={{ background: '#ffffff', boxShadow: '0 0 0 2px color-mix(in srgb, var(--theme-primary) 40%, transparent)' }}
                >
                  <UI_ClubIcon clubName={firstResult.club} iconWidth='full' styleType="icon-notext" />
                </span>
                <span className='truncate text-xs font-bold' style={{ color: 'var(--theme-mode-text-secondary)' }}>
                  {firstResult.club}
                </span>
              </div>
            </div>
          </div>

          {/* Три плитки статистики: Points / Medals / Level — карьерные данные (§2) */}
          <div className='grid grid-cols-3 gap-[7px] px-3.5 pt-2'>
            <div className='flex flex-col items-center justify-center gap-1 rounded-xl px-2 py-2.5' style={{ background: 'var(--theme-mode-input-bg)' }}>
              <div className='text-xl font-extrabold leading-none' style={{ color: 'var(--theme-primary)' }}>{career.totalPoints}</div>
              <div className='text-[9px] font-bold uppercase tracking-[0.05em]' style={{ color: 'var(--theme-mode-text-muted)' }}>Points</div>
            </div>
            <div className='flex flex-col items-center justify-center gap-1 rounded-xl px-2 py-2.5' style={{ background: 'var(--theme-mode-input-bg)' }}>
              <div className='flex justify-center gap-1'>
                <MedalWithTooltip place="1" count={career.gold} notes={career.medals.filter((m) => m.position === 1).map((m) => m.note)} />
                <MedalWithTooltip place="2" count={career.silver} notes={career.medals.filter((m) => m.position === 2).map((m) => m.note)} />
                <MedalWithTooltip place="3" count={career.bronze} notes={career.medals.filter((m) => m.position === 3).map((m) => m.note)} />
              </div>
              <div className='text-[9px] font-bold uppercase tracking-[0.05em]' style={{ color: 'var(--theme-mode-text-muted)' }}>Medals</div>
            </div>
            <div className='flex flex-col items-center justify-center gap-0.5 rounded-xl px-2 py-2.5' style={{ background: 'var(--theme-mode-input-bg)' }}>
              <UI_NormativeLevelIcon
                levelName={firstResult.levelInfo.currentLevel}
                styleType="gauge"
                styleSize="size-2"
                styleName={firstResult.event_style_name}
                styleLen={firstResult.event_style_len}
                poolType={firstResult.pool_type}
                isMasters={Helper.isResultMasters(isMastersSource, firstResult.event_style_age)}
                normativeAgeGroup={firstResult.levelInfo.normativeAgeGroup}
                progressPercent={firstResult.levelInfo.progressToNextLevel}
                nextTime={firstResult.levelInfo.nextTime}
              />
              <div className='text-[9px] font-bold uppercase tracking-[0.05em]' style={{ color: 'var(--theme-mode-text-muted)' }}>Level</div>
            </div>
          </div>

          {/* scope switcher: это соревнование / all-time (TASK.md) */}
          <div className="px-4 pt-3.5">
            <div
              className="grid grid-cols-2 gap-1 rounded-xl p-1"
              style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border-drawer)' }}
            >
              {([
                {
                  key: 'competition' as Scope,
                  label: selectedSource.title || firstResult.competition || 'This competition',
                  sub: formatMonthYear(firstResult.date),
                },
                { key: 'alltime' as Scope, label: 'All-time', sub: 'career stats' },
              ]).map((seg) => {
                const active = scope === seg.key;
                return (
                  <button
                    key={seg.key}
                    type="button"
                    className="text-center px-1.5 py-[7px] rounded-[9px] cursor-pointer transition-colors min-w-0"
                    style={active ? { background: 'var(--theme-primary)' } : undefined}
                    onClick={() => setScope(seg.key)}
                  >
                    <div
                      className="text-[13px] font-extrabold truncate"
                      style={{ color: active ? '#fff' : 'var(--theme-mode-text)' }}
                    >
                      {seg.label}
                    </div>
                    <div
                      className="text-[10px] font-semibold mt-px truncate"
                      style={{ color: active ? 'rgba(255,255,255,0.8)' : 'var(--theme-mode-text-muted)' }}
                    >
                      {seg.sub}
                    </div>
                  </button>
                );
              })}
            </div>
          </div>

          {/* scope banner — есть в обоих режимах, чтобы попап не прыгал по высоте */}
          <div
            className="mx-4 mt-3 rounded-[14px] px-4 py-[13px] flex justify-between items-center"
            style={{ background: 'linear-gradient(135deg, var(--theme-primary), var(--theme-bg-active))' }}
          >
            {scope === 'competition' ? (
              <>
                <div>
                  <div className="text-2xl font-extrabold text-white leading-none">
                    {pointsSum}<sup className="text-xs font-normal">pt</sup>
                  </div>
                  <div className="text-[10px] font-semibold text-white/80 mt-1">
                    this competition · {formatMonthYear(firstResult.date)}
                  </div>
                </div>
                <div className="flex gap-1.5">
                  <MedalWithTooltip place="1" count={medalCounts.first.length} notes={medalCounts.first.map((r) => (r as any).note)} />
                  <MedalWithTooltip place="2" count={medalCounts.second.length} notes={medalCounts.second.map((r) => (r as any).note)} />
                  <MedalWithTooltip place="3" count={medalCounts.third.length} notes={medalCounts.third.map((r) => (r as any).note)} />
                </div>
              </>
            ) : (
              <>
                <div>
                  <div className="text-2xl font-extrabold text-white leading-none">{career.competitions}</div>
                  <div className="text-[10px] font-semibold text-white/80 mt-1">competitions</div>
                </div>
                <div className="text-right">
                  <div className="text-2xl font-extrabold text-white leading-none">{career.races}</div>
                  <div className="text-[10px] font-semibold text-white/80 mt-1">races</div>
                </div>
              </>
            )}
          </div>

          {/* results section — ВСЕ заплывы пловца на этом соревновании.
              flex-1 min-h-0 + overflow-y-auto: скроллится только этот блок, карточка
              целиком ограничена max-h-[90vh] на section-top и не "прыгает" по высоте. */}
          {scope === 'competition' && allSwimmerResults.length > 0 && (
            <div className="p-4 flex-1 min-h-0 overflow-y-auto">
              <h2 className="text-[15px] font-extrabold mb-3" style={{ color: 'var(--theme-primary)' }}>All results</h2>
              <TopResultsTabs
                sortedBestResults={allSwimmerResults}
                isMastersSource={isMastersSource}
                isAwardSource={isAwardSource}
                mediaResultIds={mediaResultIds}
                onOpenMedia={setOpenMediaResultId}
              />
            </div>
          )}

          {/* all-time section: best times by style, в том же виде, что и результаты соревнования */}
          {scope === 'alltime' && <AllTimeBests career={career} />}

          {/* «Мои ссылки» — личное owner-only медиа (2A), видно только залогиненному. */}
          {isAuthenticated && swimmerId != null && (
            <MyMediaSection
              swimmerId={swimmerId}
              userMedia={userMedia}
              allSwimmerResults={allSwimmerResults}
              openResultId={openMediaResultId}
              onOpenHandled={() => setOpenMediaResultId(null)}
            />
          )}

          {/* Гость (фаза 4.5): вместо звёзд/«Моих ссылок» — один компактный CTA. */}
          {showGuestCta && <GuestFavoritesCta onSignIn={openLoginModal} />}
      </div>
    </div>
  );
}

// Loglig ID (docs/loglig-id-plan.md, шаг 6 — хвост): бейдж статуса привязки либо кнопка
// краудсорс-предложения с инлайн-полем. Гость без статуса/при Rejected не видит ничего —
// не плодим CTA логина рядом.
function LogligSuggestBadge({
  status,
  profileUrl,
  isAuthenticated,
  suggest,
}: {
  status: string | null;
  profileUrl: string | null;
  isAuthenticated: boolean;
  suggest: (input: string) => Promise<{ ok: boolean; error?: string }>;
}) {
  const [open, setOpen] = useState(false);
  const [input, setInput] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  if (status === 'Verified') {
    // Есть подтверждённый ID → бейдж ведёт на публичную карточку игрока loglig.com.
    const badgeClass = 'inline-flex items-center rounded-full px-2 py-1 text-[10px] font-bold';
    const badgeStyle = { background: 'rgba(255,255,255,0.9)', color: 'var(--theme-primary)' };
    if (profileUrl != null) {
      return (
        <a
          href={profileUrl}
          target="_blank"
          rel="noopener noreferrer"
          title="Open verified loglig.com profile"
          className={`${badgeClass} hover:underline`}
          style={badgeStyle}
        >
          loglig ✓
        </a>
      );
    }
    return (
      <span title="Verified loglig.com profile" className={badgeClass} style={badgeStyle}>
        loglig ✓
      </span>
    );
  }

  if (status === 'Suggested') {
    return (
      <span
        className="inline-flex items-center rounded-full px-2 py-1 text-[10px] font-bold"
        style={{ background: 'rgba(255,255,255,0.75)', color: '#6b7280' }}
      >
        loglig: pending review
      </span>
    );
  }

  if (!isAuthenticated) return null;

  const handleSubmit = async () => {
    setError(null);
    setSubmitting(true);
    const res = await suggest(input);
    setSubmitting(false);
    if (res.ok) {
      setOpen(false);
      setInput('');
    } else {
      setError(res.error ?? 'Failed to submit suggestion');
    }
  };

  if (!open) {
    return (
      <button
        type="button"
        onClick={() => setOpen(true)}
        className="inline-flex items-center rounded-full px-2 py-1 text-[10px] font-bold hover:opacity-90"
        style={{ background: 'rgba(255,255,255,0.9)', color: 'var(--theme-mode-text-secondary)' }}
      >
        Suggest loglig profile
      </button>
    );
  }

  return (
    <div className="flex flex-col gap-1 rounded-lg p-1.5" style={{ background: 'rgba(255,255,255,0.9)' }}>
      <div className="flex gap-1">
        <input
          type="text"
          value={input}
          autoFocus
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') handleSubmit(); }}
          placeholder="loglig.com player card link"
          className="w-[150px] min-w-0 rounded px-1.5 py-1 text-[10px]"
          style={{ border: '1px solid var(--theme-mode-border)' }}
        />
        <button
          type="button"
          onClick={handleSubmit}
          disabled={submitting || !input.trim()}
          className="shrink-0 rounded px-1.5 py-1 text-[10px] font-bold text-white disabled:opacity-50"
          style={{ background: 'var(--theme-primary)' }}
        >
          Submit
        </button>
        <button
          type="button"
          onClick={() => { setOpen(false); setInput(''); setError(null); }}
          className="shrink-0 rounded px-1.5 py-1 text-[10px] font-bold"
          style={{ background: 'var(--theme-mode-surface-alt)', color: 'var(--theme-mode-text-secondary)' }}
        >
          Cancel
        </button>
      </div>
      {error && <div className="text-[9px]" style={{ color: '#e23b5a' }}>{error}</div>}
    </div>
  );
}

// Гость (фаза 4.5): компактный CTA вместо звезды/сердечка и «Моих ссылок» —
// стиль карточек попапа + кнопка в стиле кнопок LoginModal.
function GuestFavoritesCta({ onSignIn }: { onSignIn: () => void }) {
  return (
    <div className="p-4 border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
      <div
        className="flex items-center justify-between gap-3 rounded-xl px-4 py-3"
        style={{ background: 'var(--theme-mode-input-bg)' }}
      >
        <span className="text-[13px] font-semibold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
          Sign in to save favorites and links
        </span>
        <button
          type="button"
          onClick={onSignIn}
          className="shrink-0 rounded-[11px] bg-[linear-gradient(140deg,#38bdf8,#0369a1)] px-3.5 py-[9px] text-[13px] font-black text-[#06263f] transition-opacity hover:opacity-90"
        >
          Sign in
        </button>
      </div>
    </div>
  );
}

// All-time: лучшие времена по стилям — те же карточки-строки, что и в табе «это соревнование»
function AllTimeBests({ career }: { career: AthleteCareer }) {
  const rows = useMemo(() => career.bestByStyle.map((b) => {
    const isMaster = Helper.isResultMasters(b.isMasters, b.eventStyleAge);
    const resolvedGender = Helper.resolveGender(b.gender);
    const levelInfo = Helper.getNormativeLevelInfo({
      gender: resolvedGender === 'none' ? 'male' : resolvedGender,
      poolType: Helper.resolvePoolType(b.pool),
      styleName: b.stroke,
      distance: `${b.distance}m`,
      time: Helper.parseTimeToSeconds(b.time),
      isMaster,
      ageGroup: b.ageGroup,
      event_style_age: b.eventStyleAge,
    });

    return {
      position: b.position,
      event_style_age: b.eventStyleAge,
      event_style_name: b.stroke,
      event_style_len: b.distance,
      time: b.time,
      // Инвариант И11: строка со временем несёт и качество. Без этого all-time-карточка
      // молча показывала спорный заплыв как обычный — API признак отдаёт (CareerBest.suspect_reason),
      // терялся он ровно здесь, в мэппинге.
      suspect_reason: b.suspect_reason ?? null,
      international_points: b.points,
      date: b.date,
      competition: b.competition,
      pool_type: b.pool,
      // Каждая строка all-time — из СВОЕГО соревнования, поэтому award-eligibility
      // берём по-строчно (b.isAward), а не общим флагом на весь список.
      is_award: b.isAward,
      levelInfo,
    };
  }), [career.bestByStyle]);

  const isMastersOverall = career.bestByStyle.some((b) => b.isMasters);

  return (
    <div className="p-4 flex-1 min-h-0 overflow-y-auto">
      <h2 className="text-[15px] font-extrabold mb-3" style={{ color: 'var(--theme-primary)' }}>Best times by style</h2>
      {rows.length > 0 ? (
        <ResultsTable results={rows} isMastersSource={isMastersOverall} isAwardSource={false} showCompetition />
      ) : (
        <div className="text-[var(--theme-mode-text-muted)] italic">—</div>
      )}
    </div>
  );
}

// «Мои ссылки» (2A): личное owner-only медиа пловца. Видно только залогиненному владельцу
// (проверка isAuthenticated делается в родителе). Рендер: youtube/vimeo → лайтбокс
// UI_SwimmerGallery (никогда сырой URL в iframe), other → внешняя ссылка noopener/noreferrer.
// Хук useUserMedia поднят в SportsmenDetails (один запрос на карточку, нужен и для иконок
// на строках результатов) — сюда приходит уже готовый объект хука.
function MyMediaSection({
  swimmerId,
  userMedia,
  allSwimmerResults,
  openResultId,
  onOpenHandled,
}: {
  swimmerId: number;
  userMedia: ReturnType<typeof useUserMedia>;
  allSwimmerResults: any[];
  openResultId: number | null;
  onOpenHandled: () => void;
}) {
  const { media, loading, remove } = userMedia;
  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);

  // Публикации в группы (этап 3): статусы заявок + панель «поделиться» для одного медиа.
  const { publications, submit: submitPublication, withdraw: withdrawPublication } = useMyMediaPublications();
  const [publishMediaId, setPublishMediaId] = useState<number | null>(null);
  const [pubGroupId, setPubGroupId] = useState<number | ''>('');
  const [pubLevel, setPubLevel] = useState<'members' | 'public'>('members');
  const [pubSubmitting, setPubSubmitting] = useState(false);
  const [pubError, setPubError] = useState<string | null>(null);
  // Куда МОЖНО подать выбранное медиа — сервер отдаёт только группы, где я член/админ
  // и пловец медиа в ростере (не предлагаем группы, где подача всё равно откажет).
  const [publishTargets, setPublishTargets] = useState<{ id: number; name: string }[] | null>(null);
  useEffect(() => {
    if (publishMediaId == null) { setPublishTargets(null); return; }
    let alive = true;
    setPublishTargets(null);
    fetch(`/api/me/media/${publishMediaId}/publish-targets`, { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((list: { id: number; name: string }[]) => { if (alive) setPublishTargets(list); })
      .catch(() => { if (alive) setPublishTargets([]); });
    return () => { alive = false; };
  }, [publishMediaId]);

  const handlePublish = async () => {
    if (publishMediaId == null || pubGroupId === '') return;
    setPubError(null);
    setPubSubmitting(true);
    const res = await submitPublication(publishMediaId, pubGroupId, pubLevel);
    setPubSubmitting(false);
    if (res.ok) {
      setPublishMediaId(null);
      setPubGroupId('');
    } else {
      setPubError(res.error ?? 'Could not submit the request');
    }
  };

  const pubStatusLabel: Record<string, string> = {
    pending: 'pending review',
    approved: 'published',
    rejected: 'rejected',
  };

  // Только youtube/vimeo идут в лайтбокс — эти элементы и их порядок в gallery
  // соответствуют lightboxIndex, которым мы кликаем.
  const lightboxItems = useMemo<GalleryItem[]>(
    () => media
      .filter((m) => m.source_type === 'youtube' || m.source_type === 'vimeo')
      .map((m) => ({ type: 'video', sourceType: m.source_type, url: m.url })),
    [media]
  );

  const handleDelete = async (id: number) => {
    await remove(id);
    setConfirmDeleteId(null);
  };

  const openLightboxFor = (item: UserMediaDto) => {
    const idx = lightboxItems.findIndex((g) => g.url === item.url);
    if (idx >= 0) setLightboxIndex(idx);
  };

  // Клик по иконке «есть видео» на строке результата (см. onOpenMedia в ResultsTable) —
  // отдельный лайтбокс ТОЛЬКО из медиа этого заплыва (не всего списка пловца).
  const [rowGallery, setRowGallery] = useState<GalleryItem[] | null>(null);
  useEffect(() => {
    if (openResultId == null) return;
    const items = media
      .filter((m) => m.result_id === openResultId && (m.source_type === 'youtube' || m.source_type === 'vimeo'))
      .map((m): GalleryItem => ({ type: 'video', sourceType: m.source_type, url: m.url }));
    if (items.length > 0) setRowGallery(items);
    onOpenHandled();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [openResultId, media]);

  // Чип у привязанного к заплыву элемента в списке — по совпадению result_id со строкой карточки.
  const resultChipLabel = (item: UserMediaDto): string | null => {
    if (item.result_id == null) return null;
    const row = allSwimmerResults.find((r) => r.id === item.result_id);
    return row ? `${row.event_style_len}m ${row.event_style_name}` : 'swim';
  };

  return (
    <div className="border-t" style={{ borderColor: 'var(--theme-mode-border)' }}>
      {/* Свёрнуто по умолчанию — попап пловца не растёт в высоту (запрос Влада);
          добавление медиа убрано с публичных страниц (docs/tasks/public-pages-media-cleanup-sonnet.md) —
          единственная точка входа теперь client/media.html, здесь только просмотр/удаление/публикация. */}
      <details className="p-4">
      <summary
        className="cursor-pointer select-none list-none text-[15px] font-extrabold"
        style={{ color: 'var(--theme-primary)' }}
      >
        My links{!loading && media.length > 0 ? ` (${media.length})` : ''}
      </summary>
      <div className="mt-1 mb-2">
        <a
          href={routes.myMedia()}
          className="text-xs font-semibold underline"
          style={{ color: 'var(--theme-mode-text-secondary)' }}
        >
          Manage in My media →
        </a>
      </div>
      <div className="mt-3">

      {/* Список */}
      {loading ? (
        <div className="text-xs italic" style={{ color: 'var(--theme-mode-text-muted)' }}>Loading…</div>
      ) : media.length === 0 ? (
        <div className="text-xs italic" style={{ color: 'var(--theme-mode-text-muted)' }}>No links yet</div>
      ) : (
        <ul className="grid grid-cols-3 gap-2">
          {media.map((item) => {
            const thumb = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);
            const isEmbeddable = item.source_type === 'youtube' || item.source_type === 'vimeo';
            return (
              <li key={item.id} className="relative rounded-lg overflow-hidden group" style={{ background: 'var(--theme-mode-input-bg)' }}>
                <button
                  type="button"
                  onClick={() => setConfirmDeleteId(item.id)}
                  title="Delete"
                  className="absolute top-1 right-1 z-10 w-5 h-5 rounded-full inline-flex items-center justify-center text-xs font-bold text-white"
                  style={{ background: 'rgba(0,0,0,0.55)' }}
                >
                  ×
                </button>

                {/* Подача в группу (этап 3): открывает панель публикации под сеткой. */}
                <button
                  type="button"
                  onClick={() => {
                    setPublishMediaId(publishMediaId === item.id ? null : item.id);
                    setPubError(null);
                  }}
                  title="Share with a group"
                  className="absolute top-1 left-1 z-10 w-5 h-5 rounded-full inline-flex items-center justify-center text-[10px] font-bold text-white"
                  style={{ background: publishMediaId === item.id ? 'var(--theme-primary)' : 'rgba(0,0,0,0.55)' }}
                >
                  ↗
                </button>

                {isEmbeddable ? (
                  <div className="cursor-pointer aspect-video flex items-center justify-center" onClick={() => openLightboxFor(item)}>
                    {thumb ? (
                      <img src={thumb} alt="" className="w-full h-full object-cover" />
                    ) : (
                      <span className="text-xs" style={{ color: 'var(--theme-mode-text-muted)' }}>video</span>
                    )}
                  </div>
                ) : (
                  <a
                    href={item.url}
                    target="_blank"
                    rel="noopener noreferrer nofollow"
                    className="aspect-video flex items-center justify-center"
                  >
                    {thumb ? (
                      <img src={thumb} alt="" className="w-full h-full object-cover" />
                    ) : (
                      <span className="text-xs underline" style={{ color: 'var(--theme-mode-text-muted)' }}>link</span>
                    )}
                  </a>
                )}

                {resultChipLabel(item) && (
                  <div
                    className="absolute bottom-1 left-1 z-10 max-w-[calc(100%-8px)] truncate rounded px-1.5 py-0.5 text-[9px] font-bold text-white"
                    style={{ background: 'rgba(0,0,0,0.55)' }}
                  >
                    {resultChipLabel(item)}
                  </div>
                )}

                {confirmDeleteId === item.id && (
                  <div className="absolute inset-0 z-20 flex flex-col items-center justify-center gap-1 text-center p-1" style={{ background: 'rgba(0,0,0,0.75)' }}>
                    <span className="text-[10px] text-white">Delete?</span>
                    <div className="flex gap-1">
                      <button
                        type="button"
                        onClick={() => handleDelete(item.id)}
                        className="text-[10px] font-bold px-2 py-0.5 rounded"
                        style={{ background: '#e23b5a', color: '#fff' }}
                      >
                        Да
                      </button>
                      <button
                        type="button"
                        onClick={() => setConfirmDeleteId(null)}
                        className="text-[10px] font-bold px-2 py-0.5 rounded"
                        style={{ background: 'rgba(255,255,255,0.2)', color: '#fff' }}
                      >
                        Нет
                      </button>
                    </div>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      {/* Панель публикации выбранного медиа + статусы её заявок. */}
      {publishMediaId != null && (
        <div className="mt-3 flex flex-col gap-2 rounded-[10px] p-2.5" style={{ background: 'var(--theme-mode-surface-alt)' }}>
          <div className="text-xs font-bold" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            Share with a group
          </div>
          {publishTargets != null && publishTargets.length === 0 && (
            <div className="text-[10px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
              No eligible groups — the swimmer must be in the group's roster and you must be a member.
            </div>
          )}
          <div className="flex flex-wrap gap-2">
            <select
              value={pubGroupId}
              onChange={(e) => setPubGroupId(e.target.value === '' ? '' : Number(e.target.value))}
              className="rounded-lg px-2 py-1.5 text-xs"
              style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text)', border: '1px solid var(--theme-mode-border)' }}
            >
              <option value="">— group —</option>
              {(publishTargets ?? []).map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
            </select>
            <select
              value={pubLevel}
              onChange={(e) => setPubLevel(e.target.value as 'members' | 'public')}
              className="rounded-lg px-2 py-1.5 text-xs"
              style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text)', border: '1px solid var(--theme-mode-border)' }}
            >
              <option value="members">group members</option>
              <option value="public">public (visible to everyone)</option>
            </select>
            <button
              type="button"
              onClick={handlePublish}
              disabled={pubSubmitting || pubGroupId === ''}
              className="rounded-lg px-2.5 py-1.5 text-xs font-bold text-white disabled:opacity-50"
              style={{ background: 'var(--theme-primary)' }}
            >
              Подать
            </button>
          </div>
          {pubError && <div className="text-[10px]" style={{ color: '#e23b5a' }}>{pubError}</div>}

          {/* Заявки этого медиа */}
          {publications.filter((p) => p.user_media_id === publishMediaId).length > 0 && (
            <div className="flex flex-wrap gap-1.5">
              {publications.filter((p) => p.user_media_id === publishMediaId).map((p) => (
                <span
                  key={p.id}
                  className="inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-[10px] font-bold"
                  style={{
                    background: 'var(--theme-mode-input-bg)',
                    color: p.status === 'approved' ? 'var(--theme-primary)'
                      : p.status === 'rejected' ? '#e23b5a' : 'var(--theme-mode-text-secondary)',
                    border: '1px solid var(--theme-mode-border)',
                  }}
                >
                  {p.hub_group_name} · {pubStatusLabel[p.status] ?? p.status}{p.status === 'approved' && p.level === 'public' ? ' (everyone)' : ''}
                  <button
                    type="button"
                    title="Withdraw"
                    onClick={() => withdrawPublication(p.user_media_id, p.hub_group_id)}
                    className="leading-none opacity-60 hover:opacity-100"
                  >
                    ×
                  </button>
                </span>
              ))}
            </div>
          )}
        </div>
      )}
      </div>
      </details>

      {/* Лайтбокс списка «Мои ссылки» — вне <details>, работает и при свёрнутой секции. */}
      <UI_SwimmerGallery
        gallery={lightboxItems}
        openIndex={lightboxIndex}
        onClose={() => setLightboxIndex(null)}
      />

      {/* Лайтбокс иконки строки — только видео конкретного заплыва. */}
      {rowGallery && (
        <UI_SwimmerGallery
          gallery={rowGallery}
          openIndex={0}
          onClose={() => setRowGallery(null)}
        />
      )}
    </div>
  );
}

// Компонент табов для Training/Competition
function TopResultsTabs({
  sortedBestResults,
  isMastersSource,
  isAwardSource,
  mediaResultIds,
  onOpenMedia,
}: {
  sortedBestResults: any[];
  isMastersSource: boolean;
  isAwardSource: boolean;
  mediaResultIds?: Set<number>;
  onOpenMedia?: (resultId: number) => void;
}) {
  // Разделяем результаты на training и competition
  const trainingResults = useMemo(() => {
    return sortedBestResults.filter(r => !!r.training?.trainingId);
  }, [sortedBestResults]);

  const competitionResults = useMemo(() => {
    return sortedBestResults.filter(r => !r.training?.trainingId);
  }, [sortedBestResults]);

  // Определяем начальный таб: competition, если training пустой
  const initialTab = trainingResults.length === 0 && competitionResults.length > 0 ? 'competition' : 'training';
  const [activeTab, setActiveTab] = useState<'training' | 'competition'>(initialTab);

  // Проверяем, есть ли хотя бы один результат с is_masters
  const hasMasters = isMastersSource;

  // Если нет masters — показываем все результаты без табов
  if (!hasMasters) {
    return (
      <ResultsTable
        results={sortedBestResults}
        isMastersSource={isMastersSource}
        isAwardSource={isAwardSource}
        mediaResultIds={mediaResultIds}
        onOpenMedia={onOpenMedia}
      />
    );
  }

  const currentResults = activeTab === 'training' ? trainingResults : competitionResults;

  return (
    <div>
      {/* Табы — pill-группа */}
      <div className="flex gap-1 w-fit rounded-[10px] p-[3px] mb-3" style={{ background: 'var(--theme-mode-surface-alt)' }}>
        {(['training', 'competition'] as const).map((tab) => {
          const active = activeTab === tab;
          const count = tab === 'training' ? trainingResults.length : competitionResults.length;
          return (
            <button
              key={tab}
              className="text-xs font-bold px-3 py-[5px] rounded-lg capitalize transition-colors"
              style={active
                ? { background: 'var(--theme-primary)', color: '#fff' }
                : { background: 'transparent', color: 'var(--theme-mode-text-secondary)' }}
              onClick={() => setActiveTab(tab)}
            >
              {tab} ({count})
            </button>
          );
        })}
      </div>

      {/* Карточки результатов */}
      {currentResults.length > 0 ? (
        <ResultsTable
          results={currentResults}
          isMastersSource={isMastersSource}
          isAwardSource={isAwardSource}
          mediaResultIds={mediaResultIds}
          onOpenMedia={onOpenMedia}
        />
      ) : (
        <div className="text-[var(--theme-mode-text-muted)] italic p-4">No {activeTab} results</div>
      )}
    </div>
  );
}

/** "16/02/2026" → "16 Feb 2026" (дата заплыва в строке результата). */
const formatDayMonthYear = (date?: string): string => {
  if (!date) return '';
  const parts = date.split('/');
  if (parts.length !== 3) return date;
  const dt = new Date(Number(parts[2]), Number(parts[1]) - 1, 1);
  if (isNaN(dt.getTime())) return date;
  return `${Number(parts[0])} ${dt.toLocaleString('en', { month: 'short' })} ${parts[2]}`;
};

// Строка результата в 2 линии (design_handoff_athlete_card §5):
// линия 1 — место/возраст, картинка стиля с дистанцией, время;
// линия 2 — сплиты/очки/дата слева, дуга уровня справа.
function ResultsTable({
  results,
  isMastersSource,
  isAwardSource,
  mediaResultIds,
  onOpenMedia,
  showCompetition,
}: {
  results: any[];
  isMastersSource: boolean;
  isAwardSource: boolean;
  mediaResultIds?: Set<number>;
  onOpenMedia?: (resultId: number) => void;
  /** Название соревнования рядом с датой — только там, где строки реально смешивают разные соревнования (all-time) */
  showCompetition?: boolean;
}) {
  const base = import.meta.env.BASE_URL;

  return (
    <ul className="flex flex-col gap-2.5">
      {results.map((res, index) => {
        const levelInfo = res.levelInfo;
        const hasPoints =
          res.international_points !== undefined &&
          res.international_points !== null &&
          !isNaN(Number(res.international_points));
        // По-строчный флаг (all-time может смешивать разные соревнования) с фолбэком
        // на общий isAwardSource (одно соревнование — таб «это соревнование»).
        const rowIsAward = res.is_award ?? isAwardSource ?? false;

        // Иконка «есть видео» — только для строк с DB id заплыва (статические источники
        // его не имеют, см. Footguns задания). Добавление медиа с этой страницы убрано —
        // остаётся только просмотр уже привязанного медиа.
        const rowId = typeof res.id === 'number' ? res.id : null;
        const hasMedia = rowId != null && !!mediaResultIds?.has(rowId);

        // Спорное время (гибрид 15d): та же обвязка, что у строки таблицы результатов —
        // лента слева + полный текст в title/aria-label; чип рисует UI_SwimTime.
        const quality = res.suspect_reason ? { kind: 'protocol' as const, reason: res.suspect_reason } : null;
        const flagProps = swimFlaggedRowProps(quality);

        return (
          <li
            key={index}
            {...flagProps}
            className={`flex flex-col gap-2 rounded-[14px] py-3 pr-3 ${
              quality ? `${flagProps.className} swim-flagged-row--rounded pl-5` : 'pl-3'
            }`}
            style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text)' }}
          >
            {/* Линия 1: место+возраст · стиль с дистанцией · время */}
            <div className="flex items-center gap-3">
              <div className="flex shrink-0 flex-col items-center gap-1">
                <UI_MedalIcon place={String(res.position ?? index + 1)} styleType={rowIsAward ? 'icon-place' : 'icon-noplace'} styleSize="medal-40" />
                {res.event_style_age && (
                  <div className="text-[10px]" style={{ color: 'var(--theme-mode-text-muted)' }}>
                    age: <strong style={{ color: 'var(--theme-mode-text)' }}>{res.event_style_age}</strong>
                  </div>
                )}
              </div>
              <div className="relative w-[86px] shrink-0">
                {/* Белая подложка иконки стиля — статусная, не зависит от темы (TASK.md §Темы) */}
                <img
                  src={`${base}images/swimm-style-icon/${String(res.event_style_name || '').replaceAll(' ', '-')}.png`}
                  alt={res.event_style_name}
                  className="block w-[86px] rounded-[7px] p-1"
                  style={{ background: '#ffffff' }}
                  onError={(e) => {
                    e.currentTarget.src = `${base}images/swimm-style-icon/no-swim.png`;
                  }}
                />
                <span
                  className="absolute -top-[7px] -right-[5px] text-[15px] font-black"
                  style={{ color: '#e53935', textShadow: '0 1px 2px rgba(255,255,255,0.7)' }}
                >
                  {res.event_style_len}
                </span>
                <UI_PoolIcon styleType="icon-text-center" label={res.pool_type} labelClassName="text-xs" className="mt-1" />
              </div>
              <div className="flex-1" />
              <UI_SwimmerTimeCell
                time={res.time}
                quality={quality}
                qualityMarker="chip"
                time_split={res.time_split}
                time_fail={res.time_fail}
                time_fail_note={res.time_fail_note}
                firstLineClassName="text-xl font-extrabold tabular-nums"
                secondLineClassName="text-[10px]"
                className="text-right"
              />
            </div>

            {/* Линия 2: сплиты (если есть) / очки / дата — слева; дуга уровня — справа */}
            <div
              className="flex items-center gap-2.5 pl-[46px] pt-2"
              style={{ borderTop: '1px solid var(--theme-mode-border)' }}
            >
              <div className="min-w-0 flex-1">
                {hasPoints && (
                  <div className="mt-px text-[11px]" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                    Points: <strong style={{ color: 'var(--theme-mode-text)' }}>{res.international_points}</strong>
                  </div>
                )}
                <div className="mt-px" style={{ color: 'var(--theme-mode-text-secondary)' }}>
                  <ResultRowDateInfo
                    date={res.date}
                    competition={res.competition}
                    showCompetition={showCompetition}
                    className="justify-start"
                  />
                </div>
              </div>

              {/* Медиа заплыва (2A): иконка «есть видео» — только просмотр уже загруженного
                  медиа (owner-view). Добавление медиа с этой страницы убрано (см. задание). */}
              {hasMedia && (
                <button
                  type="button"
                  onClick={() => onOpenMedia?.(rowId!)}
                  title="Open video"
                  className="shrink-0 inline-flex items-center justify-center w-7 h-7 rounded-full transition-opacity hover:opacity-80"
                  style={{ background: 'var(--theme-primary)' }}
                >
                  <svg width="13" height="13" viewBox="0 0 24 24" fill="#fff">
                    <path d="M8 5v14l11-7z" />
                  </svg>
                </button>
              )}

              {levelInfo?.currentLevel ? (
                <UI_NormativeLevelIcon
                  levelName={levelInfo.currentLevel}
                  styleType="gauge"
                  styleSize="size-2"
                  className="shrink-0"
                  styleName={res.event_style_name}
                  styleLen={res.event_style_len}
                  poolType={res.pool_type}
                  isMasters={Helper.isResultMasters(isMastersSource, res.event_style_age)}
                  normativeAgeGroup={levelInfo.normativeAgeGroup}
                  progressPercent={levelInfo.progressToNextLevel}
                  nextTime={levelInfo.nextTime}
                />
              ) : (
                <span style={{ color: 'var(--theme-mode-text-muted)' }}>—</span>
              )}
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export default SportsmenDetails;
