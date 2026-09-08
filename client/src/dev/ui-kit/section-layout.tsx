/**
 * Секция «Layout»: страничный контейнер PAGE_CONTAINER и лесенка колонок content-box-*.
 *
 * Показываем каждую ширину дважды. Схема (полоса в масштабе) отвечает на «сколько это»,
 * живая проба — на «сколько это ЗДЕСЬ»: у боксов нет медиазапросов, на узком окне max-width
 * просто не срабатывает и все пять дают 100%. Именно это чаще всего и удивляет.
 */
import React, { useEffect, useRef, useState } from 'react';
import { PAGE_CONTAINER } from '../../utils/layout';

interface BoxSpec {
  size: string;
  max: number;
  use: string;
}

/** Держать в синхроне с лесенкой в src/index.css (там же — почему она такая). */
const BOXES: BoxSpec[] = [
  { size: 'xs', max: 780, use: 'узкая лента: строки, где полезных пикселей мало' },
  { size: 'sm', max: 850, use: 'текстовая колонка, формы' },
  { size: 'md', max: 920, use: 'списки и карточки средней плотности' },
  { size: 'lg', max: 1050, use: 'таблицы с несколькими колонками' },
  { size: 'xl', max: 1180, use: 'широкие таблицы; ширина страниц клуба и пловца' },
];

const SCALE_MAX = 1180;

/** Фактическая ширина узла — чтобы показать, сработал max-width или окно уже него. */
function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null);
  const [width, setWidth] = useState(0);
  useEffect(() => {
    const node = ref.current;
    if (!node) return;
    const ro = new ResizeObserver(() => setWidth(Math.round(node.getBoundingClientRect().width)));
    ro.observe(node);
    return () => ro.disconnect();
  }, []);
  return { ref, width };
}

const LiveProbe: React.FC = () => {
  const [size, setSize] = useState('md');
  const box = BOXES.find((b) => b.size === size)!;
  const { ref, width } = useWidth<HTMLDivElement>();
  const clipped = width > 0 && width < box.max;

  return (
    <div className="uk-probe">
      <div className="uk-toolbar">
        <span className="uk-toolbar__label">живая проба</span>
        {BOXES.map((b) => (
          <button
            key={b.size}
            type="button"
            className={`uk-chip${size === b.size ? ' is-active' : ''}`}
            onClick={() => setSize(b.size)}
          >
            {b.size}
          </button>
        ))}
      </div>
      <div className="uk-probe__page">
        <div className={PAGE_CONTAINER}>
          <div ref={ref} className={`content-box-${size} uk-probe__box`}>
            .content-box-{size}
          </div>
        </div>
      </div>
      <p className="uk-note">
        max-width {box.max}px → фактически <b>{width || '—'}px</b>.{' '}
        {clipped
          ? 'Окно уже бокса: max-width не сработал, ширину диктует окно — ровно так все пять ведут себя на телефоне.'
          : 'Бокс упёрся в свой max-width.'}
      </p>
    </div>
  );
};

const SectionLayout: React.FC = () => (
  <>
    <section id="page-container" className="uk-section">
      <div className="uk-section__head">
        <h2>PAGE_CONTAINER</h2>
        <a className="uk-anchor" href="#page-container">
          #page-container
        </a>
      </div>
      <p className="uk-section__lead">
        Контейнер СТРАНИЦЫ: ширина 1440 плюс боковые паддинги. Один на страницу, живёт в{' '}
        <code>src/utils/layout.ts</code>. Полосы-фоны (топбар, hero, табы) идут край-в-край, а их
        содержимое и контент страницы ограничивает он. Колонки <code>content-box-*</code> — другое:
        они вкладываются ВНУТРЬ него и своих паддингов не имеют.
      </p>
      <pre className="uk-code">{PAGE_CONTAINER}</pre>
    </section>

    <section id="content-box" className="uk-section">
      <div className="uk-section__head">
        <h2>content-box-*</h2>
        <a className="uk-anchor" href="#content-box">
          #content-box
        </a>
      </div>
      <p className="uk-section__lead">
        Лесенка колонок контента. Не всякому экрану нужны 1440: у ленты стартового протокола
        полезных пикселей в строке ~230 из 1180, и на широком мониторе это читается как сломанная
        вёрстка. Обычные классы, а не <code>@utility</code> — имя, собранное в рантайме
        (<code>content-box-$&#123;size&#125;</code>), не сработало бы. Медиазапросов нет и не нужно.
      </p>

      <div className="uk-ladder">
        {BOXES.map((b) => (
          <div key={b.size} id={`content-box-${b.size}`} className="uk-ladder__row">
            <div className="uk-ladder__name">
              .content-box-{b.size}
              <a className="uk-anchor" href={`#content-box-${b.size}`}>
                #content-box-{b.size}
              </a>
            </div>
            <div className="uk-ladder__bar">
              <span style={{ width: `${(b.max / SCALE_MAX) * 100}%` }}>{b.max}px</span>
            </div>
            <div className="uk-ladder__use">{b.use}</div>
          </div>
        ))}
      </div>

      <LiveProbe />
    </section>
  </>
);

export default SectionLayout;
