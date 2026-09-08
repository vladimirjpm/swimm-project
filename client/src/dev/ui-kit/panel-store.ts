/**
 * Вид панелей витрины: порядок пропов, что раскрыто, у кого стоит autoclose, настройки сцены.
 *
 * Хранится ОДНОЙ записью в localStorage и одним массивом внутри — по записи на компонент,
 * внутри массив пропов. Так «сохранить всё, что на странице» — это буквально один
 * `JSON.stringify` состояния, а не обход разрозненных ключей; и добавление компонента в
 * реестр не требует ничего менять здесь.
 *
 * Чего тут сознательно НЕТ — значений пропсов: они живут в адресе (url-state.ts). Значение
 * это вид, которым делятся ссылкой, а порядок и раскрытость — привычка того, кто смотрит.
 *
 * Сохранение ручное, по кнопке: витрину крутят постоянно, и автосохранение каждого клика
 * незаметно закрепляло бы случайно оставленное состояние.
 */

export interface PanelPropState {
  name: string;
  open: boolean;
  autoClose: boolean;
}

export interface PanelScene {
  bg: string;
  width: number;
  fontSize: number;
}

export interface PanelState {
  /** Порядок = порядок этого массива. Он же источник порядка на экране. */
  props: PanelPropState[];
  scene: PanelScene;
  /**
   * Показывать ли объяснения: описание компонента, «что нужно знать», подписи к пропам и
   * пояснения под сценой. По умолчанию нет — их читают один раз, а место они занимают
   * всегда, отодвигая вниз то, ради чего витрину и открыли.
   */
  docsOpen: boolean;
}

/** Состояние всех панелей страницы: ключ — id компонента из реестра. */
export type PanelsState = Record<string, PanelState>;

interface StoredShape {
  v: number;
  components: { id: string; props: PanelPropState[]; scene: PanelScene; docsOpen?: boolean }[];
}

const STORAGE_KEY = 'swimm-ui-kit-panels';
const VERSION = 1;

export function defaultPanel(propNames: string[], defaultWidth: number): PanelState {
  return {
    props: propNames.map((name) => ({ name, open: true, autoClose: false })),
    scene: { bg: 'surface', width: defaultWidth, fontSize: 16 },
    docsOpen: false,
  };
}

/**
 * Сохранённое накладывается на дефолт, а не заменяет его: реестр меняется чаще хранилища —
 * пропс могли добавить, переименовать или убрать уже после того, как вид сохранили.
 * Известные пропы встают в сохранённом порядке, новые — в конец, исчезнувшие отбрасываются.
 */
export function mergePanel(base: PanelState, stored?: PanelState): PanelState {
  if (!stored) return base;
  const known = new Map(base.props.map((p) => [p.name, p]));
  const ordered: PanelPropState[] = [];
  stored.props.forEach((p) => {
    if (known.has(p.name)) {
      ordered.push({ name: p.name, open: p.open, autoClose: p.autoClose });
      known.delete(p.name);
    }
  });
  known.forEach((p) => ordered.push(p));
  return {
    props: ordered,
    scene: { ...base.scene, ...stored.scene },
    // Записи, сохранённые до появления флага, его не имеют — тогда берём дефолт.
    docsOpen: stored.docsOpen ?? base.docsOpen,
  };
}

export function loadPanels(): PanelsState {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return {};
    const parsed = JSON.parse(raw) as StoredShape;
    if (!parsed || parsed.v !== VERSION || !Array.isArray(parsed.components)) return {};
    return Object.fromEntries(
      parsed.components.map((c) => [
        c.id,
        { props: c.props ?? [], scene: c.scene, docsOpen: c.docsOpen === true },
      ])
    );
  } catch {
    // Мусор или формат прошлой версии — начинаем с дефолтов, а не падаем.
    return {};
  }
}

export function savePanels(panels: PanelsState): boolean {
  const payload: StoredShape = {
    v: VERSION,
    components: Object.entries(panels).map(([id, panel]) => ({ id, ...panel })),
  };
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
    return true;
  } catch {
    return false;
  }
}

/** Отпечаток для сравнения «есть ли несохранённое». */
export function fingerprint(panels: PanelsState): string {
  return JSON.stringify(
    Object.entries(panels)
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([id, panel]) => [id, panel])
  );
}

/** Порядок пропов внутри одного массива: перенос элемента `from` на позицию `to`. */
export function reorder<T>(list: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= list.length || to >= list.length) return list;
  const next = [...list];
  const [moved] = next.splice(from, 1);
  next.splice(to, 0, moved);
  return next;
}
