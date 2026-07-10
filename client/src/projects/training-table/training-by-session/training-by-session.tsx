import React, { useMemo } from 'react';
import './training-by-session.css';
import Helper from '../../../utils/helpers/data-helper';
import { Result } from '../../../utils/interfaces/results';
import UI_IntensityIcon from '../../components/mix/intensity-icon/intensity-icon';
import UI_ExpectedTimeDiff from '../../components/mix/expected-time-diff/expected-time-diff';
import UI_PaddlesIcon from '../../components/mix/paddles-icon/paddles-icon';
import UI_PullBuoyIcon from '../../components/mix/pull-buoy-icon/pull-buoy-icon';
import UI_NormativeLevelIcon from '../../components/mix/normative-level-icon/normative-level-icon';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem, TrainingMediaItem } from '../../../utils/interfaces/results';
import { HelperMedia } from '../../../utils/helpers';

interface Props {
  results: Result[];
  selectedSource: { title?: string; is_masters?: boolean } & Record<string, any>;
  filters: Record<string, any>;
  updateFilter: (newFilter: Record<string, any>) => void;
}

// dd/MM/yyyy → сортируемый ключ yyyyMMdd
const dateKey = (d?: string) => {
  const [dd, mm, yy] = (d ?? '').split('/');
  return `${yy ?? ''}${mm ?? ''}${dd ?? ''}`;
};

// «28/10/2025» → «28 Oct 2025»
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const prettyDate = (d?: string) => {
  const [dd, mm, yy] = (d ?? '').split('/');
  if (!dd) return d ?? '';
  return `${dd} ${MONTHS[(Number(mm) || 1) - 1]} ${yy ?? ''}`.trim();
};

function MediaStripTile({ item, onClick }: { item: TrainingMediaItem; onClick?: () => void }) {
  if (item.media_type === 'album') {
    return (
      <a href={item.url} target="_blank" rel="noopener noreferrer" className="tbs-media-tile tbs-media-album">
        <span>📂 {item.caption || 'Album'} ↗</span>
      </a>
    );
  }

  const thumbUrl = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);

  return (
    <div className="tbs-media-tile" onClick={onClick}>
      {thumbUrl ? <img loading="lazy" src={thumbUrl} alt="" /> : <span className="tbs-media-fallback">🎬</span>}
      {item.media_type === 'video' && <span className="tbs-media-play">▶</span>}
      {item.caption && <span className="tbs-media-caption">{item.caption}</span>}
    </div>
  );
}

/** Медиа-лента тренировки под сводкой сессии: тайлы + ОДИН лайтбокс на полосу. */
function TrainingMediaStrip({ media }: { media: TrainingMediaItem[] }) {
  const [openIndex, setOpenIndex] = React.useState<number | null>(null);

  // album в лайтбокс не попадает — массив и карта индексов считаются один раз на media.
  const { lightboxGalleryItems, indexById } = useMemo(() => {
    const lightboxItems = media.filter((m) => m.media_type !== 'album');
    return {
      lightboxGalleryItems: lightboxItems.map((m): GalleryItem => ({
        type: m.media_type === 'video' ? 'video' : 'image',
        sourceType: m.source_type === 'album' ? undefined : (m.source_type as GalleryItem['sourceType']),
        url: m.url,
      })),
      indexById: new Map(lightboxItems.map((m, i) => [m.id, i])),
    };
  }, [media]);

  if (media.length === 0) return null;

  return (
    <div className="tbs-media-strip">
      {media.map((item) => (
        <MediaStripTile
          key={item.id}
          item={item}
          onClick={item.media_type === 'album' ? undefined : () => setOpenIndex(indexById.get(item.id) ?? 0)}
        />
      ))}
      <UI_SwimmerGallery
        gallery={lightboxGalleryItems}
        openIndex={openIndex}
        onClose={() => setOpenIndex(null)}
      />
    </div>
  );
}

const swimmerName = (r: Result) =>
  `${r.first_name ?? ''}${r.last_name ? ' ' + r.last_name : ''}`.trim() ||
  `${r.first_name_en ?? ''} ${r.last_name_en ?? ''}`.trim();

function TrainingBySession({ results, selectedSource, filters, updateFilter }: Props) {
  const showRating = filters?.rating_mode !== 'no';
  const isMastersSource = !!selectedSource?.is_masters;

  const sessions = useMemo(() => {
    const map = new Map<string, Result[]>();
    for (const r of results) {
      const id = String(r?.training?.trainingId ?? 'na');
      if (!map.has(id)) map.set(id, []);
      map.get(id)!.push(r);
    }

    return Array.from(map.entries())
      .map(([id, items]) => {
        const first = items[0];
        const swimmers = new Set(items.map(swimmerName)).size;
        const volume = items.reduce((s, x) => s + (Number(x.event_style_len) || 0), 0);
        const vs = items
          .map((x) => Number(String(x?.training?.intensity ?? '').replace(/\D/g, '')))
          .filter((n) => n > 0);
        const avgV = vs.length ? Math.round(vs.reduce((a, b) => a + b, 0) / vs.length) : 0;
        return {
          id,
          name: first?.training?.trainingName || first?.competition || `Training ${id}`,
          date: first?.date ?? '',
          pool: Helper.resolvePoolType(first?.pool_type),
          reps: items.length,
          swimmers,
          volume,
          avgV,
          sets: Helper.groupTrainingBySet(items),
          media: first?.training?.media ?? [],
        };
      })
      .sort((a, b) => dateKey(a.date).localeCompare(dateKey(b.date)));
  }, [results]);

  if (!sessions.length) {
    return <div className="tbs-empty">No trainings match the current filters.</div>;
  }

  return (
    <div className="tbs">
      {sessions.map((s) => (
        <section key={s.id} className="tbs-card">
          {/* ── Шапка сессии ── */}
          <header className="tbs-head">
            <div className="tbs-title" title={s.name}>{s.name}</div>
            <div className="tbs-head-right">
              {s.media.length > 0 && <span className="tbs-media-count" title="Media">📷 {s.media.length}</span>}
              <span className="tbs-date">{prettyDate(s.date)}</span>
            </div>
          </header>

          {/* ── Сводка сессии ── */}
          <div className="tbs-summary">
            <div className="tbs-stat"><span className="k">Volume</span><span className="v">{s.volume.toLocaleString()}<small> m</small></span></div>
            <div className="tbs-stat"><span className="k">Reps</span><span className="v">{s.reps}</span></div>
            <div className="tbs-stat"><span className="k">Swimmers</span><span className="v">{s.swimmers}</span></div>
            <div className="tbs-stat"><span className="k">Avg effort</span><span className="v">{s.avgV ? `V${s.avgV}` : '—'}</span></div>
            <div className="tbs-stat tbs-stat-pool"><span className="k">Pool</span><span className="v">{s.pool}</span></div>
          </div>

          {/* ── Медиа тренировки ── */}
          <TrainingMediaStrip media={s.media} />

          {/* ── Сеты ── */}
          {s.sets.map((set, si) => {
            const items = set.items;
            const first = items[0];
            const orders = new Set(items.map((x) => x?.training?.order ?? 0)).size;
            const distance = first?.event_style_len ?? '';
            const style = first?.event_style_name ?? '';
            const interval = first?.training?.interval;
            const intensity = first?.training?.intensity;
            const paddles = items.some((x) => x?.training?.isPaddles);
            const buoy = items.some((x) => x?.training?.isBuoy);

            return (
              <div key={si} className="tbs-set">
                <div className="tbs-set-head">
                  <span className="tbs-setno">SET {set.set ?? si + 1}</span>
                  <span className="tbs-recipe">
                    <span className="chip strong">{orders > 1 ? `${orders}×${distance}` : distance} {style}</span>
                    {interval ? <span className="chip">start {Helper.formatSecondsToTimeString(interval)}</span> : null}
                    {intensity ? <span className="chip v"><UI_IntensityIcon intensity={intensity} /></span> : null}
                    {paddles ? <span className="chip"><UI_PaddlesIcon className="w-4 h-4" /> paddles</span> : null}
                    {buoy ? <span className="chip"><UI_PullBuoyIcon className="w-4 h-4" /> buoy</span> : null}
                  </span>
                  <span className="tbs-setmeta">{items.length} reps</span>
                </div>

                <ul className="tbs-rows">
                  {items.map((r, i) => {
                    const isFemale = r.event_style_gender === 'female';
                    const levelInfo = showRating
                      ? Helper.getNormativeLevelInfo({
                          gender: isFemale ? 'female' : 'male',
                          poolType: Helper.resolvePoolType(r.pool_type),
                          styleName: r.event_style_name,
                          distance: `${r.event_style_len}m`,
                          time: Helper.parseTimeToSeconds(r.time),
                          isMaster: Helper.isResultMasters(isMastersSource, r.event_style_age),
                          event_style_age: r.event_style_age,
                        })
                      : null;

                    return (
                      <li key={i} className={`tbs-row ${isFemale ? 'f' : 'm'}`}>
                        <span className="tbs-order">{set.set ?? '·'}.{r?.training?.order ?? '·'}</span>
                        <button
                          type="button"
                          className="tbs-who"
                          dir="rtl"
                          title={`Filter by ${swimmerName(r)}`}
                          onClick={() => updateFilter({ selected_name: swimmerName(r) })}
                        >
                          {swimmerName(r)}
                        </button>
                        <span className="tbs-gear">
                          {r?.training?.isPaddles && <UI_PaddlesIcon className="w-4 h-4" />}
                          {r?.training?.isBuoy && <UI_PullBuoyIcon className="w-4 h-4" />}
                        </span>
                        <span className="tbs-time">
                          <span className="t">{r.time}</span>
                          <UI_ExpectedTimeDiff time={r.time} expected_time={r?.training?.expected_time} />
                        </span>
                        <span className="tbs-v"><UI_IntensityIcon intensity={r?.training?.intensity} /></span>
                        {showRating && levelInfo && (
                          <span className="tbs-lvl">
                            <UI_NormativeLevelIcon
                              levelName={levelInfo.currentLevel}
                              styleType="style-1"
                              styleSize="size-2"
                              styleName={r.event_style_name}
                              styleLen={r.event_style_len}
                              poolType={r.pool_type}
                              isMasters={Helper.isResultMasters(isMastersSource, r.event_style_age)}
                              normativeAgeGroup={levelInfo.normativeAgeGroup}
                            />
                          </span>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </div>
            );
          })}
        </section>
      ))}
    </div>
  );
}

export default TrainingBySession;
