/**
 * Песочница одного компонента: слева сцена, справа пропсы кнопками, снизу готовый вызов.
 * Ничего про конкретные компоненты не знает — всё берёт из ComponentEntry (registry.tsx).
 *
 * Вид панели (порядок пропов, раскрытость, autoclose, настройки сцены) сюда приходит сверху
 * и наверх же уходит: сохраняет его каркас, одной кнопкой на всю страницу (panel-store.ts).
 * Своего localStorage у песочницы нет — иначе «сохранить всё» пришлось бы собирать по кускам.
 */
import React, { useRef, useState } from 'react';
import type { ComponentEntry, PropSpec } from './registry';
import { buildSnippet } from './registry';
import type { PanelState } from './panel-store';
import { reorder } from './panel-store';

const BG_LABELS: Record<string, string> = {
  surface: 'карточка (тема)',
  page: 'страница (тема)',
  white: 'белый',
  dark: 'тёмный',
  checker: 'шахматка',
};

/** Ступени ширины сцены. `0` = auto: сцена ширину не навязывает, работает className. */
const WIDTHS = [0, 24, 32, 48, 64, 96, 140];
const FONT_SIZES = [12, 14, 16, 20, 24];
/** Ряд «как выглядит в разных размерах» — с текущими пропсами, но своей шириной. */
const SCALE = [24, 32, 48, 64, 96];

/** Сколько нужно увести указатель, чтобы это считалось перетаскиванием, а не кликом (px). */
const DRAG_THRESHOLD = 4;

interface DragState {
  name: string;
  x: number;
  y: number;
  pointerId: number;
  /** Порог уже пройден: это перетаскивание, а не нажатие. */
  moved: boolean;
}

interface Props {
  entry: ComponentEntry;
  values: Record<string, string>;
  onChange: (name: string, value: string) => void;
  onReset: () => void;
  panel: PanelState;
  /**
   * Обновление вида ФУНКЦИЕЙ, а не готовым объектом: два изменения в одном тике (сменили
   * фон и тут же ширину) строились бы оба из одного и того же `panel`, и второе затирало
   * бы первое.
   */
  onPanelChange: (updater: (prev: PanelState) => PanelState) => void;
}

const Playground: React.FC<Props> = ({ entry, values, onChange, onReset, panel, onPanelChange }) => {
  const [copied, setCopied] = useState(false);
  /** Что тащим и над кем висим — в state только ради подсветки, на время перетаскивания. */
  const [dragName, setDragName] = useState<string | null>(null);
  const [overName, setOverName] = useState<string | null>(null);
  /* Те же данные в ref: обработчик pointerup читает их сразу, не дожидаясь ререндера. */
  const dragRef = useRef<DragState | null>(null);
  const overRef = useRef<string | null>(null);
  const swallowNextClick = useRef(false);

  const specs = new Map(entry.props.map((p) => [p.name, p]));
  /** Пропы в порядке панели; спека берётся из реестра, порядок — из сохранённого вида. */
  const orderedProps = panel.props
    .map((p) => specs.get(p.name))
    .filter((s): s is PropSpec => Boolean(s));

  const { bg, width, fontSize } = panel.scene;
  /** Пояснительные тексты панели скрыты вместе с остальными объяснениями компонента. */
  const showDocs = panel.docsOpen;
  const snippet = buildSnippet(entry, values, orderedProps);
  const allAuto = panel.props.every((p) => p.autoClose);

  const patchProp = (name: string, patch: Partial<{ open: boolean; autoClose: boolean }>) =>
    onPanelChange((prev) => ({
      ...prev,
      props: prev.props.map((p) => (p.name === name ? { ...p, ...patch } : p)),
    }));

  const setScene = (patch: Partial<PanelState['scene']>) =>
    onPanelChange((prev) => ({ ...prev, scene: { ...prev.scene, ...patch } }));

  /** Галка заодно приводит проп в свой вид: включили — свернулся, выключили — раскрылся. */
  const toggleAutoClose = (name: string, next: boolean) =>
    patchProp(name, { autoClose: next, open: !next });

  /** Массовое переключение — чтобы не щёлкать галки по одной. */
  const setAllAutoClose = (next: boolean) =>
    onPanelChange((prev) => ({
      ...prev,
      props: prev.props.map((p) => ({ ...p, autoClose: next, open: !next })),
    }));

  /** Выбор готового значения (кнопкой), в отличие от ввода руками в поле. */
  const pick = (name: string, value: string) => {
    onChange(name, value);
    if (panel.props.find((p) => p.name === name)?.autoClose) patchProp(name, { open: false });
  };

  const moveProp = (from: string, to: string) =>
    onPanelChange((prev) => ({
      ...prev,
      props: reorder(
        prev.props,
        prev.props.findIndex((p) => p.name === from),
        prev.props.findIndex((p) => p.name === to)
      ),
    }));

  /**
   * Перетаскивание на pointer-событиях, а НЕ на HTML5 drag-and-drop: тот не существует на
   * тач-экранах и не воспроизводится автоматизацией браузера (нативный drag не запускается
   * синтетическими событиями мыши). Здесь же обычные pointerdown/move/up — одни и те же для
   * мыши, пальца и стилуса.
   */
  const startDrag = (e: React.PointerEvent<HTMLElement>, name: string) => {
    if (e.button !== 0) return; // правая кнопка и средняя перетаскиванием не считаются
    // Новое нажатие — новая история: после перетаскивания браузер часто не шлёт click вовсе
    // (нажали на одном пропе, отпустили на другом), и взведённый флаг съел бы следующий
    // честный клик по шапке.
    swallowNextClick.current = false;
    dragRef.current = { name, x: e.clientX, y: e.clientY, pointerId: e.pointerId, moved: false };
  };

  const moveDrag = (e: React.PointerEvent<HTMLElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;

    if (!drag.moved) {
      // Порог: без него любой клик по шапке считался бы перетаскиванием на 1px.
      const shift = Math.abs(e.clientX - drag.x) + Math.abs(e.clientY - drag.y);
      if (shift < DRAG_THRESHOLD) return;
      drag.moved = true;
      setDragName(drag.name);
      // Захват ставим ЗДЕСЬ, а не на pointerdown: он перенаправляет на шапку все события
      // указателя, включая click, и обычное нажатие переставало доходить до кнопки-
      // переключателя внутри — проп не раскрывался. Пока порог не пройден, это ещё клик.
      // Отказ (у синтетического события нет живого указателя) не мешает: без захвата
      // перетаскивание работает, пока указатель над списком.
      try {
        e.currentTarget.setPointerCapture(e.pointerId);
      } catch {
        /* обойдёмся без захвата */
      }
    }

    // Из-за захвата события все приходят в исходную шапку, поэтому цель ищем по координатам.
    const under = document.elementFromPoint(e.clientX, e.clientY);
    const target = under?.closest('[data-uk-prop]')?.getAttribute('data-uk-prop') ?? null;
    const valid = target && panel.props.some((p) => p.name === target) ? target : null;
    overRef.current = valid;
    setOverName((prev) => (prev === valid ? prev : valid));
  };

  const endDrag = (e: React.PointerEvent<HTMLElement>) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    if (drag.moved) {
      if (overRef.current && overRef.current !== drag.name) moveProp(drag.name, overRef.current);
      // Отпустили над кнопкой-переключателем — click после pointerup свернул бы проп заодно.
      swallowNextClick.current = true;
    }
    cancelDrag(e);
  };

  const cancelDrag = (e: React.PointerEvent<HTMLElement>) => {
    if (e.currentTarget.hasPointerCapture?.(e.pointerId)) {
      e.currentTarget.releasePointerCapture(e.pointerId);
    }
    dragRef.current = null;
    overRef.current = null;
    setDragName(null);
    setOverName(null);
  };

  const swallowClickAfterDrag = (e: React.MouseEvent) => {
    if (!swallowNextClick.current) return;
    swallowNextClick.current = false;
    e.stopPropagation();
    e.preventDefault();
  };

  const copy = () => {
    navigator.clipboard?.writeText(snippet).then(
      () => {
        setCopied(true);
        window.setTimeout(() => setCopied(false), 1200);
      },
      () => setCopied(false)
    );
  };

  const stage = (w: number) => (
    <div
      className={`uk-stage-box${w ? ' uk-stage-box--sized' : ''}`}
      style={w ? { width: w, fontSize } : { fontSize }}
    >
      {entry.render(values)}
    </div>
  );

  return (
    <div className="uk-play">
      <div className="uk-play__left">
        <div className="uk-toolbar">
          <span className="uk-toolbar__label">фон</span>
          {Object.keys(BG_LABELS).map((key) => (
            <button
              key={key}
              type="button"
              className={`uk-chip${bg === key ? ' is-active' : ''}`}
              onClick={() => setScene({ bg: key })}
            >
              {BG_LABELS[key]}
            </button>
          ))}
        </div>

        <div className={`uk-scene uk-scene--${bg}`}>{stage(width)}</div>

        <div className="uk-toolbar">
          <span className="uk-toolbar__label">ширина</span>
          {WIDTHS.map((w) => (
            <button
              key={w}
              type="button"
              className={`uk-chip${width === w ? ' is-active' : ''}`}
              onClick={() => setScene({ width: w })}
            >
              {w === 0 ? 'auto' : `${w}px`}
            </button>
          ))}
          <span className="uk-toolbar__label">кегль</span>
          {FONT_SIZES.map((f) => (
            <button
              key={f}
              type="button"
              className={`uk-chip${fontSize === f ? ' is-active' : ''}`}
              onClick={() => setScene({ fontSize: f })}
            >
              {f}
            </button>
          ))}
        </div>
        {showDocs && (
          <p className="uk-note">
            «auto» — сцена ширину не навязывает: размер целиком из <code>className</code>. Любая
            другая ступень имитирует вызывающего, который дал коробке ширину, и перебивает
            <code> w-*</code> в классах.
          </p>
        )}

        <div className="uk-scale">
          <span className="uk-toolbar__label">шкала</span>
          {SCALE.map((w) => (
            <div key={w} className="uk-scale__item">
              <div className={`uk-scene uk-scene--${bg} uk-scene--tight`}>{stage(w)}</div>
              <span>{w}</span>
            </div>
          ))}
        </div>
      </div>

      <div className="uk-play__right">
        <div className="uk-props__head">
          <span>props</span>
          <button
            type="button"
            className="uk-chip uk-chip--ghost"
            title="Поставить или снять autoclose сразу у всех пропов"
            onClick={() => setAllAutoClose(!allAuto)}
          >
            {allAuto ? 'auto: снять' : 'auto: всем'}
          </button>
          <button type="button" className="uk-chip uk-chip--ghost" onClick={onReset}>
            сбросить
          </button>
        </div>

        {orderedProps.map((spec) => {
          const value = values[spec.name] ?? '';
          const state = panel.props.find((p) => p.name === spec.name)!;
          const classes = [
            'uk-prop',
            state.open ? 'is-open' : '',
            dragName === spec.name ? 'is-dragging' : '',
            overName === spec.name && dragName !== spec.name ? 'is-drop-target' : '',
          ].filter(Boolean);

          return (
            <div key={spec.name} className={classes.join(' ')} data-uk-prop={spec.name}>
              {/* Тащим за шапку, а не за весь блок: иначе нельзя было бы выделить текст в
                  поле ввода внутри раскрытого пропа. Галка вынесена из кнопки-переключателя —
                  вложенный интерактив внутри <button> невалиден. */}
              <div
                className="uk-prop__head"
                onPointerDown={(e) => startDrag(e, spec.name)}
                onPointerMove={moveDrag}
                onPointerUp={endDrag}
                onPointerCancel={cancelDrag}
                onClickCapture={swallowClickAfterDrag}
                /* Ведя мышь с зажатой кнопкой по тексту, браузер заводит СВОЁ перетаскивание
                   выделения. Оно перехватывает указатель, pointerup до нас не доходит, и
                   подсветка остаётся висеть. Гасим его в зародыше. */
                onDragStart={(e) => e.preventDefault()}
              >
                <span className="uk-prop__grip" title="Перетащить: изменить порядок пропов">
                  ⠿
                </span>
                <button
                  type="button"
                  className="uk-prop__toggle"
                  aria-expanded={state.open}
                  onClick={() => patchProp(spec.name, { open: !state.open })}
                >
                  <span className="uk-prop__caret">{state.open ? '▾' : '▸'}</span>
                  <span className="uk-prop__name">{spec.name}</span>
                  <span className="uk-prop__value" title={value || 'не задан'}>
                    {value || '—'}
                  </span>
                </button>
                <label
                  className="uk-check"
                  title={`Сворачивать «${spec.name}» сразу после выбора значения`}
                >
                  <input
                    type="checkbox"
                    checked={state.autoClose}
                    onChange={(e) => toggleAutoClose(spec.name, e.target.checked)}
                  />
                  auto
                </label>
              </div>

              {state.open && (
                <div className="uk-prop__body">
                  {spec.doc && showDocs && <div className="uk-prop__doc">{spec.doc}</div>}

                  {spec.kind === 'enum' ? (
                    <div className="uk-prop__row">
                      {spec.options.map((opt) => (
                        <button
                          key={opt}
                          type="button"
                          title={spec.hints?.[opt]}
                          className={`uk-chip${value === opt ? ' is-active' : ''}`}
                          onClick={() => pick(spec.name, opt)}
                        >
                          {opt}
                          {/* Подпись значения — тоже объяснение: со снятой галкой она
                              остаётся только в подсказке при наведении (title выше). */}
                          {spec.hints?.[opt] && showDocs && <em>{spec.hints[opt]}</em>}
                        </button>
                      ))}
                    </div>
                  ) : (
                    <>
                      <div className="uk-prop__row">
                        {(spec.presets ?? []).map((preset) => (
                          <button
                            key={preset}
                            type="button"
                            className={`uk-chip${value === preset ? ' is-active' : ''}`}
                            onClick={() => pick(spec.name, value === preset ? '' : preset)}
                          >
                            {preset}
                          </button>
                        ))}
                      </div>
                      {/* Ввод руками autoclose НЕ закрывает: поле схлопнулось бы на первом
                          набранном символе. Закрывает только выбор готового значения. */}
                      <input
                        className="uk-input"
                        value={value}
                        placeholder={spec.placeholder}
                        onChange={(e) => onChange(spec.name, e.target.value)}
                      />
                    </>
                  )}
                </div>
              )}
            </div>
          );
        })}

        <div className="uk-snippet">
          <div className="uk-snippet__head">
            <span>вызов</span>
            <button type="button" className="uk-chip uk-chip--ghost" onClick={copy}>
              {copied ? 'скопировано' : 'copy'}
            </button>
          </div>
          <pre>{snippet}</pre>
          <div className="uk-snippet__file">{entry.file}</div>
        </div>
      </div>
    </div>
  );
};

export default Playground;
