/**
 * Каркас dev-витрины: слева оглавление, справа секции. Тёмный хром страницы намеренно
 * не зависит от темы приложения (свои токены --uk-* в ui-kit.css) — иначе, переключая тему,
 * мы бы перекрашивали и линейку, которой меряем.
 *
 * Оси приложения переключаются по-настоящему, на <html>: data-mode через штатный useMode,
 * data-theme — руками (штатный useTheme завязан на activity_type в сторе, здесь его нет).
 */
import React, { useCallback, useEffect, useRef, useState } from 'react';
import './ui-kit.css';
import useMode from '../../hooks/useMode';
import SectionLayout from './section-layout';
import Playground from './playground';
import { COMPONENTS, defaultValues } from './registry';
import type { PanelState, PanelsState } from './panel-store';
import { defaultPanel, fingerprint, loadPanels, mergePanel, savePanels } from './panel-store';
import {
  lastWrittenHash,
  PROP_PREFIX,
  propsFromUrl,
  readUrlState,
  sameHash,
  writeUrlState,
} from './url-state';

/** Список тем — из :root[data-theme=…] в src/index.css. */
const THEMES = [
  'training',
  'training-dashboard',
  'training-nexaverse',
  'training-ocean',
  'competition',
  'competition-emerald',
  'competition-blue',
  'competition-warm',
  'competition-dark',
];

interface NavItem {
  anchor: string;
  label: string;
  children?: { anchor: string; label: string }[];
}

const NAV: { title: string; items: NavItem[] }[] = [
  {
    title: 'Layout',
    items: [
      { anchor: 'page-container', label: 'PAGE_CONTAINER' },
      {
        anchor: 'content-box',
        label: 'content-box-*',
        children: ['xs', 'sm', 'md', 'lg', 'xl'].map((s) => ({
          anchor: 'content-box-' + s,
          label: s,
        })),
      },
    ],
  },
  {
    title: 'Components',
    items: COMPONENTS.map((c) => ({ anchor: 'c-' + c.id, label: c.name })),
  },
];

const ALL_ANCHORS = NAV.flatMap((g) =>
  g.items.flatMap((i) => [i.anchor, ...(i.children ?? []).map((c) => c.anchor)])
);

const UiKitApp: React.FC = () => {
  const initial = useRef(readUrlState()).current;
  const { mode, setMode } = useMode();
  const [theme, setTheme] = useState(initial.params.theme ?? 'training');
  const [active, setActive] = useState(initial.anchor || ALL_ANCHORS[0]);
  const [values, setValues] = useState<Record<string, Record<string, string>>>(() =>
    Object.fromEntries(
      COMPONENTS.map((c) => [c.id, { ...defaultValues(c), ...propsFromUrl(c.id, initial) }])
    )
  );
  /** Компонент, чьи пропсы едут в адрес: последний, который крутили. */
  const [propComponent, setPropComponent] = useState(
    initial.params.pc ??
      (initial.anchor.startsWith('c-') ? initial.anchor.slice(2) : COMPONENTS[0].id)
  );
  const [copiedLink, setCopiedLink] = useState(false);
  /**
   * Вид всех панелей страницы одним объектом — порядок пропов, раскрытость, autoclose,
   * настройки сцены. Живёт здесь, а не внутри песочниц, потому что кнопка «сохранить»
   * одна на страницу: собирать состояние по кускам с детей было бы нечем.
   */
  const [panels, setPanels] = useState<PanelsState>(() => {
    const stored = loadPanels();
    return Object.fromEntries(
      COMPONENTS.map((c) => [
        c.id,
        mergePanel(defaultPanel(c.props.map((p) => p.name), c.defaultWidth), stored[c.id]),
      ])
    );
  });
  /** Отпечаток последнего сохранения — по нему видно, есть ли несохранённые правки. */
  const [savedPrint, setSavedPrint] = useState<string>(() => '');
  const [saveNote, setSaveNote] = useState('');

  const dirty = fingerprint(panels) !== savedPrint;

  // Первый отпечаток снимаем после инициализации: он должен описывать РОВНО то, что лежит
  // в хранилище, иначе страница открывалась бы уже «изменённой».
  useEffect(() => {
    setSavedPrint(fingerprint(panels));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const savePanelState = () => {
    const ok = savePanels(panels);
    setSavedPrint(ok ? fingerprint(panels) : savedPrint);
    setSaveNote(ok ? 'сохранено' : 'не сохранилось');
    window.setTimeout(() => setSaveNote(''), 1500);
  };

  // Оси приложения на <html>.
  useEffect(() => {
    document.documentElement.dataset.theme = theme;
  }, [theme]);

  useEffect(() => {
    const fromUrl = initial.params.mode;
    if (fromUrl === 'light' || fromUrl === 'dark') setMode(fromUrl);
    // Один раз, на монтировании: дальше режимом рулит переключатель.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Скролл к якорю из адреса — после первого рендера, когда секции уже есть.
  useEffect(() => {
    if (!initial.anchor) return;
    document.getElementById(initial.anchor)?.scrollIntoView({ block: 'start' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Чужая правка адреса — вставили ссылку в ту же вкладку, нажали «назад». Своя запись
  // сюда не попадает (сверяем с lastWrittenHash), иначе витрина затирала бы вставленную
  // ссылку своим текущим состоянием.
  useEffect(() => {
    const onHashChange = () => {
      if (sameHash(window.location.hash, lastWrittenHash())) return;
      const state = readUrlState();
      const urlMode = state.params.mode;
      if (urlMode === 'light' || urlMode === 'dark') setMode(urlMode);
      if (state.params.theme) setTheme(state.params.theme);
      if (state.params.pc) setPropComponent(state.params.pc);
      setValues(
        Object.fromEntries(
          COMPONENTS.map((c) => [c.id, { ...defaultValues(c), ...propsFromUrl(c.id, state) }])
        )
      );
      if (state.anchor) {
        setActive(state.anchor);
        document.getElementById(state.anchor)?.scrollIntoView({ block: 'start' });
      }
    };
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, [setMode]);

  // Скролл-спай: активна последняя секция, чей верх выше линии в 120px.
  useEffect(() => {
    let frame = 0;
    const onScroll = () => {
      if (frame) return;
      frame = window.requestAnimationFrame(() => {
        frame = 0;
        let current = ALL_ANCHORS[0];
        ALL_ANCHORS.forEach((id) => {
          const el = document.getElementById(id);
          if (el && el.getBoundingClientRect().top <= 120) current = id;
        });
        setActive((prev) => (prev === current ? prev : current));
      });
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();
    return () => {
      window.removeEventListener('scroll', onScroll);
      if (frame) window.cancelAnimationFrame(frame);
    };
  }, []);

  // Единственное место, где собирается адрес. Пропсы со значением по умолчанию не пишем:
  // иначе ссылка на нетронутый компонент выглядит как ссылка на настроенный.
  useEffect(() => {
    const params: Record<string, string> = { mode, theme, pc: propComponent };
    const entry = COMPONENTS.find((c) => c.id === propComponent);
    const defaults = entry ? defaultValues(entry) : {};
    Object.entries(values[propComponent] ?? {}).forEach(([name, value]) => {
      if (value !== '' && value !== defaults[name]) params[PROP_PREFIX + name] = value;
    });
    writeUrlState({ anchor: active, params });
  }, [active, mode, theme, propComponent, values]);

  const setProp = useCallback((componentId: string, name: string, value: string) => {
    setPropComponent(componentId);
    setValues((prev) => ({ ...prev, [componentId]: { ...prev[componentId], [name]: value } }));
  }, []);

  const resetProps = useCallback((componentId: string) => {
    const entry = COMPONENTS.find((c) => c.id === componentId)!;
    setPropComponent(componentId);
    setValues((prev) => ({ ...prev, [componentId]: defaultValues(entry) }));
  }, []);

  const goTo = (anchor: string) => {
    setActive(anchor);
    document.getElementById(anchor)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  };

  const copyLink = () => {
    navigator.clipboard?.writeText(window.location.href).then(
      () => {
        setCopiedLink(true);
        window.setTimeout(() => setCopiedLink(false), 1200);
      },
      () => setCopiedLink(false)
    );
  };

  return (
    <div className="uk-root">
      <header className="uk-top">
        <div className="uk-top__title">
          UI Kit
          <span className="uk-badge">dev only — не собирается в прод</span>
        </div>
        <div className="uk-top__controls">
          <button
            type="button"
            className="uk-chip"
            onClick={() => setMode(mode === 'dark' ? 'light' : 'dark')}
          >
            data-mode: <b>{mode}</b>
          </button>
          <label className="uk-select">
            data-theme
            <select value={theme} onChange={(e) => setTheme(e.target.value)}>
              {THEMES.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
          </label>
          <button type="button" className="uk-chip uk-chip--ghost" onClick={copyLink}>
            {copiedLink ? 'ссылка скопирована' : 'copy link'}
          </button>
          <button
            type="button"
            className={'uk-chip uk-save' + (dirty ? ' is-dirty' : '')}
            title="Запомнить порядок пропов, раскрытость, autoclose и настройки сцен всех панелей"
            onClick={savePanelState}
          >
            {saveNote || (dirty ? 'сохранить вид •' : 'вид сохранён')}
          </button>
        </div>
      </header>

      <div className="uk-body">
        <nav className="uk-nav">
          {NAV.map((group) => (
            <div key={group.title} className="uk-nav__group">
              <div className="uk-nav__title">{group.title}</div>
              {group.items.map((item) => (
                <div key={item.anchor}>
                  <button
                    type="button"
                    className={'uk-nav__item' + (active === item.anchor ? ' is-active' : '')}
                    onClick={() => goTo(item.anchor)}
                  >
                    {item.label}
                  </button>
                  {item.children?.map((child) => (
                    <button
                      key={child.anchor}
                      type="button"
                      className={
                        'uk-nav__item uk-nav__item--child' +
                        (active === child.anchor ? ' is-active' : '')
                      }
                      onClick={() => goTo(child.anchor)}
                    >
                      {child.label}
                    </button>
                  ))}
                </div>
              ))}
            </div>
          ))}
          <div className="uk-nav__foot">
            Страница живёт только на dev-сервере:
            <br />
            <code>/ui-kit.html</code>
          </div>
        </nav>

        <main className="uk-main">
          <SectionLayout />

          {COMPONENTS.map((entry) => (
            <section key={entry.id} id={'c-' + entry.id} className="uk-section">
              <div className="uk-section__head">
                <h2>{entry.name}</h2>
                <a className="uk-anchor" href={'#c-' + entry.id}>
                  #c-{entry.id}
                </a>
                {/* Один переключатель на весь компонент: он же прячет описание, «что нужно
                    знать», подписи к пропам и пояснения под сценой. Место на экране нужно
                    самой сцене и кнопкам, а не тексту, прочитанному в первый заход. */}
                <label className="uk-check uk-check--docs" title="Показать пояснения к компоненту и его пропам">
                  <input
                    type="checkbox"
                    checked={panels[entry.id].docsOpen}
                    onChange={(e) =>
                      setPanels((prev) => ({
                        ...prev,
                        [entry.id]: { ...prev[entry.id], docsOpen: e.target.checked },
                      }))
                    }
                  />
                  объяснения
                </label>
              </div>
              {panels[entry.id].docsOpen && (
                <p className="uk-section__lead">{entry.summary}</p>
              )}
              {entry.notes && panels[entry.id].docsOpen && (
                <ul className="uk-notes">
                  {entry.notes.map((note) => (
                    <li key={note}>{note}</li>
                  ))}
                </ul>
              )}
              <Playground
                entry={entry}
                values={values[entry.id]}
                onChange={(name, value) => setProp(entry.id, name, value)}
                onReset={() => resetProps(entry.id)}
                panel={panels[entry.id]}
                onPanelChange={(updater: (prev: PanelState) => PanelState) =>
                  setPanels((prev) => ({ ...prev, [entry.id]: updater(prev[entry.id]) }))
                }
              />
            </section>
          ))}
        </main>
      </div>
    </div>
  );
};

export default UiKitApp;
