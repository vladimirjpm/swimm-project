import React, { useMemo, useRef, useState } from 'react';

/**
 * Полоса сезонов — кольцевая карусель (design_handoff_club_page_tabs/SEASON-CAROUSEL.md,
 * вариант 4c). ОБЩИЙ компонент страниц клуба и спортсмена: хендофф требует её «1:1»,
 * а поведение здесь нетривиальное (проезд ряда без ремоунта) — копия разъедется.
 *
 * Это КАРУСЕЛЬ, а не степпер. Правила, которые нельзя потерять при правках:
 *  • выбранный сезон ВСЕГДА строго по центру и никогда не смещается;
 *  • соседи видны по обе стороны (десктоп ±3, мобайл ±1) и кликабельны — тап подвозит
 *    сезон в центр, весь ряд едет (~200ms ease-out);
 *  • лента КОНЕЧНАЯ (решение Влада): слева упирается в самый старый сезон, справа —
 *    в ∞ («все сезоны»). За краями слоты пустые, но НЕ схлопываются: их ширина держит
 *    центр по центру, иначе на краях диапазона карусель бы съезжала вбок;
 *  • слоты фиксированной ширины + tabular-nums — иначе ряд дёргается на смене цифр;
 *  • cyan-свечение в полосе ровно одно: либо центр карусели, либо All (правило Deep).
 *
 * Сколько соседей видно, решает CSS, а не JS-медиазапрос: рендерим всегда ±3, лишние
 * прячет брейкпоинт.
 */

const NEIGHBOURS = 3;

/** «2025/26» → «25/26»: в слот 52px полная запись не влезает (макет 4c §4). */
function shortLabel(season: number): string {
  const from = String(season % 100).padStart(2, '0');
  const to = String((season + 1) % 100).padStart(2, '0');
  return `${from}/${to}`;
}

/** Минимум, который нужен карусели; страницы передают свои более полные типы. */
export interface DeepSeasonOption {
  /** Год НАЧАЛА сезона (2025 → «2025/26»). */
  season: number;
  label: string;
}

interface Props {
  seasons: DeepSeasonOption[];
  /** null — режим All (сезон не выбран). */
  season: number | null;
  onSeason: (season: number | null) => void;
}

/** Элемент кольца: либо сезон, либо ∞ («все сезоны»). */
type RingItem = { kind: 'season'; option: DeepSeasonOption } | { kind: 'all' };

function DeepSeasonCarousel({ seasons, season, onSeason }: Props) {
  /**
   * Лента = сезоны по возрастанию + ∞ ПОСЛЕ самого свежего сезона, и на этом она
   * КОНЧАЕТСЯ: слева край — самый старый сезон, справа — ∞. «Все сезоны» выбираются
   * тем же шагом карусели, а не только кнопкой слева.
   */
  const ring = useMemo<RingItem[]>(
    () => [
      ...[...seasons]
        .sort((a, b) => a.season - b.season)
        .map((option): RingItem => ({ kind: 'season', option })),
      { kind: 'all' },
    ],
    [seasons],
  );

  // Центр живёт отдельно от выбора: карусель никуда не прыгает, пока по ней не шагнули.
  const [center, setCenter] = useState(() => {
    const i = ring.findIndex((r) => r.kind === 'season' && r.option.season === season);
    return i >= 0 ? i : ring.length - 1; // сезон не выбран → стоим на ∞
  });

  /**
   * Смещение ряда для анимации шага. Приём стандартный для каруселей: контент уже
   * перерисован на новый центр, поэтому ряд СНАЧАЛА мгновенно сдвигается на ширину
   * слота назад (визуально он остаётся там, где был), а следующим кадром едет в 0
   * с transition — получается плавный проезд.
   *
   * Через key+@keyframes так не выходит: смена key РЕМОУНТИТ ряд, и вместо проезда
   * виден рывок (это и был «прыгающий» экран).
   */
  const [shift, setShift] = useState(0);
  const rowRef = useRef<HTMLDivElement>(null);
  const touchX = useRef<number | null>(null);

  if (ring.length <= 1) return null;   // сезонов нет вовсе — одно ∞ показывать незачем

  /** Индекс по ленте; за краями — null (ленту НЕ закольцовываем). */
  const indexAt = (offset: number): number | null => {
    const i = center + offset;
    return i >= 0 && i < ring.length ? i : null;
  };
  const at = (offset: number): RingItem | null => {
    const i = indexAt(offset);
    return i == null ? null : ring[i];
  };

  /** Что показывает слот: «25/26» либо ∞. */
  const textOf = (item: RingItem) => (item.kind === 'all' ? '∞' : shortLabel(item.option.season));
  const labelOf = (item: RingItem) =>
    item.kind === 'all' ? 'All seasons' : `Season ${item.option.label}`;

  /** Выбор элемента кольца: ∞ снимает фильтр, сезон — ставит. */
  const apply = (item: RingItem) => onSeason(item.kind === 'all' ? null : item.option.season);

  const slide = (dir: 1 | -1) => {
    // Шаг ряда = ширина слота + gap; меряем по факту, потому что в мобайле слот
    // уже (44 против 52), а брейкпоинт живёт в CSS, не здесь.
    const slotEl = rowRef.current?.querySelector('.deep-wheel__slot');
    const step = slotEl ? slotEl.getBoundingClientRect().width + 2 : 54;

    // Сдвигаем ПРОТИВ направления шага: контент уехал вперёд — ряд отводим назад,
    // чтобы стартовать с прежней позиции, и следующим кадром отпускаем в 0.
    setShift(dir > 0 ? step : -step);
    const release = () => setShift(0);
    requestAnimationFrame(() => requestAnimationFrame(release));
    // Страховка: в фоновой вкладке (и в скрытом превью) rAF не вызывается вовсе,
    // и без таймера ряд остался бы стоять сдвинутым.
    setTimeout(release, 60);
  };

  /** Шаг карусели: центр переезжает на offset и сразу применяется как фильтр. */
  const pick = (offset: number) => {
    const i = indexAt(offset);
    if (offset === 0 || i == null) return;   // за край ленты не уезжаем
    setCenter(i);
    apply(ring[i]);
    slide(offset > 0 ? 1 : -1);
  };

  const isAll = season == null;
  const offsets = Array.from({ length: NEIGHBOURS }, (_, i) => i + 1);
  // Клампим: список сезонов приходит с сервера и может стать короче (смена клуба),
  // а center пережил бы перерисовку и указал за конец ленты.
  const centerItem = ring[Math.min(center, ring.length - 1)];

  const slot = (offset: number) => {
    const item = at(offset);
    // За краем ленты — пустой слот той же ширины: он держит центр по центру полосы.
    if (!item) {
      return <span key={offset} className="deep-wheel__slot" aria-hidden="true" />;
    }
    return (
      <button
        key={offset}
        type="button"
        onClick={() => pick(offset)}
        aria-label={labelOf(item)}
        className={`deep-wheel__slot deep-wheel__slot--d${Math.abs(offset)}${
          item.kind === 'all' ? ' deep-wheel__slot--all' : ''
        }`}
      >
        {textOf(item)}
      </button>
    );
  };

  return (
    <div className="deep-season-strip mb-4">
      {/* Кнопка All и ∞ в кольце — одно и то же состояние, поэтому кнопка ещё и
          подвозит кольцо к ∞: иначе полоса показывала бы «All» слева и конкретный
          сезон в центре одновременно. */}
      <button
        type="button"
        onClick={() => {
          setCenter(ring.length - 1);
          onSeason(null);
        }}
        className={`deep-pill deep-strip-side ${isAll ? 'deep-pill--active' : ''}`}
      >
        All
      </button>

      <div
        className="deep-wheel"
        onTouchStart={(e) => { touchX.current = e.touches[0].clientX; }}
        onTouchEnd={(e) => {
          // Свайп по полосе = шаг на сезон (макет 4c). Порог 30px, чтобы обычный
          // тап по соседу не считался свайпом.
          const from = touchX.current;
          touchX.current = null;
          if (from == null) return;
          const dx = e.changedTouches[0].clientX - from;
          if (Math.abs(dx) < 30) return;
          pick(dx < 0 ? 1 : -1);
        }}
      >
        {/* Стрелки — СНАРУЖИ ряда соседей, а не вплотную к цифрам (замечание 3). */}
        {/* На краях ленты стрелка гасится: раньше она уводила по кольцу, теперь идти
            некуда, и кнопка обязана это показывать, а не молча ничего не делать. */}
        <button
          type="button"
          onClick={() => pick(-1)}
          disabled={center === 0}
          aria-label="Older season"
          className="deep-wheel-nav"
        >
          ‹
        </button>

        <div
          ref={rowRef}
          // Гашение центра при All больше не нужно: там теперь ∞, и он активен.
          className="deep-wheel__row"
          style={{
            transform: shift ? `translateX(${shift}px)` : undefined,
            // Пока ряд отводят назад — без перехода (иначе он поедет НЕ туда),
            // а возврат в 0 уже с ним.
            transition: shift ? 'none' : 'transform .2s ease-out',
          }}
        >
          {[...offsets].reverse().map((o) => slot(-o))}

          {/* Центр кликабелен: если он ∞, а фильтр стоит на сезоне (или наоборот),
              тап применяет то, что видно в центре — без шага стрелкой туда-обратно. */}
          <button
            type="button"
            onClick={() => apply(centerItem)}
            aria-label={labelOf(centerItem)}
            aria-current="true"
            className="deep-wheel__center"
          >
            <span className="deep-wheel__label">SEASON</span>
            {/* ∞ оптически мельче цифр (низкий глиф), поэтому у него свой кегль */}
            <span
              className={`deep-wheel__value${centerItem.kind === 'all' ? ' deep-wheel__value--inf' : ''}`}
            >
              {textOf(centerItem)}
            </span>
          </button>

          {offsets.map((o) => slot(o))}
        </div>

        <button
          type="button"
          onClick={() => pick(1)}
          disabled={center === ring.length - 1}
          aria-label="Newer season"
          className="deep-wheel-nav"
        >
          ›
        </button>
      </div>

      {/* Спейсер шириной с All: без него центр карусели уезжает влево. */}
      <span className="deep-strip-side" aria-hidden="true" />
    </div>
  );
}

export default DeepSeasonCarousel;
