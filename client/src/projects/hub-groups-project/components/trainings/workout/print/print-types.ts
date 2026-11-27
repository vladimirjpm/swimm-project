import type { FC } from 'react';
import type { Workout } from '../../../../types';

/**
 * Печать workout крупным шрифтом (docs/plans/workout-print-plan.md, §4): типы шва «что печатаем —
 * вид — лист». Не зависят от места кнопки и от выбранного вида. Чистые модули `print/*.ts` без DOM.
 */

/** Что печатаем: один уровень (В1), версия уровня или полный workout вместе с `levelId`. */
export interface WorkoutPrintSource {
  /**
   * Обычно уже версия уровня (`level_workout` сервера / `workoutForLevel`): сеты уровня, в ячейках
   * только его время. Полный workout тоже годится — модель сама пропустит сеты, которых уровень не
   * плывёт, и оставит у остальных номера тренера.
   */
  workout: Workout;
  /** Уровень версии; null — дорожка без уровня (времён нет). */
  levelId: number | null;
  level: { rank: number; name: string } | null;
  groupName: string;
  /**
   * Номера сетов у тренера (①②…) по порядку `workout.sets`; нужны, когда в `workout` дырка от
   * отсева уровнем уже сделана и номера иначе сдвинутся (серверная версия уровня номер не хранит).
   * Нет — номер по порядку в `workout.sets`.
   */
  setNumbers?: number[];
  // дата/время занятия — не нужны виду large (В5); добавятся, когда понадобятся другому виду
}

/**
 * Вид печати — всё, что окно предпросмотра знает о виде. Новый вид = файл `views/<id>-view.tsx`,
 * экспортирующий такой объект, + строка в `PRINT_VIEWS` (`print-views.ts`); убрать вид — убрать
 * строку. Окно, кнопка и механика печати от вида не зависят.
 */
export interface WorkoutPrintView {
  /** Ключ в `PRINT_VIEWS`; совпадает с ним. */
  id: string;
  /** Английский, это подпись сегмента в окне: 'Large'. */
  label: string;
  /** Ориентация ВСЕХ листов вида; `@page` один на печать. large — portrait (В4). */
  orientation: 'portrait' | 'landscape';
  /**
   * Листы вида. Каждый `.wprint-sheet` (класс из `workout-print.css`) — ровно одна напечатанная
   * страница; листов может быть сколько угодно — `Sheet` рисует их подряд, братьями, без обёрток:
   * разрыв страницы между ними ставит `workout-print.css`. Раскладку и подгонку вид делает сам.
   * `onFit` — итог подгонки для окна (подпись «Main on the right · 28 pt», «не влезло»); звать
   * необязательно.
   */
  Sheet: FC<{ source: WorkoutPrintSource; onFit?: (fit: PrintSheetFit) => void }>;
  /**
   * Вид задуман на две стороны одного листа бумаги: окно подсказывает включить двустороннюю печать.
   * Включить её сама страница не может — это настройка диалога печати браузера / принтера.
   */
  duplex?: boolean;
  /** Годится ли вид для этого workout (например, «все уровни» — только при нескольких уровнях). Нет — годится всегда. */
  available?: (source: WorkoutPrintSource) => boolean;
}

/** Итог подгонки листа (§3.5) — для подписи и предупреждения в окне предпросмотра. */
export interface PrintSheetFit {
  /** «Main on the right · 28 pt» / «Blocks · 3 per row · 22 pt». */
  caption: string;
  /** Не влезло и на полу шрифта: низ листа обрезан (В13). */
  overflow: boolean;
}

// ── Модель листа (`print-sheet-model.ts`) ───────────────────────────────────────

/** Направление листа: решает раскладку строк и начало «змейки» кубов (§3.6). */
export type SheetDir = 'rtl' | 'ltr';

/**
 * Кусок текста строки после разбора уровней нагрузки (хендофф 27.11 §5): текст как есть или уровень
 * чипом. `prefix` — «1-» записи тренера `1-3V` («один раз на V3»): печатается перед чипом и не
 * отрывается от него при переносе; пусто — чип сам по себе.
 */
export type SheetTextPart =
  | { kind: 'text'; text: string }
  | { kind: 'level'; label: string; prefix: string };

/** Текст строки с уровнями чипами — для листа строк (`rows`, `two-sides`). */
export interface SheetRichText {
  parts: SheetTextPart[];
  /** Направление текста: по буквам; букв нет («1-4V, 1-1V») — направление листа. */
  dir: SheetDir;
  /** Чипы поля `intensity` после текста; null — их нет или уровень уже стоит в тексте (без дублей). */
  chips: string[] | null;
}

/** Строка сета на листе: подпись + ячейка уровня. */
export interface SheetLine {
  /** «4×100», «100», «4×» или пусто; свои повторы уровня уже учтены. */
  head: string;
  text: string;
  /** Направление текста строки (для `<bdi dir>` внутри подписи). */
  textDir: SheetDir;
  /** Чипы нагрузки контуром: «V2», «V1 V2 V3», «Sprint»; null — нагрузки нет или она не по шкале. */
  chips: string[] | null;
  /** Нагрузка не по шкале — текстом (иначе null). */
  intensityText: string | null;
  /** Ячейка уровня: «R:30», «1:45», «3× 1:45»; пусто — ячейки нет. */
  cell: string;
  /** Только время или отдых уровня, без своих повторов («3×» уже в `head`): «R:30», «1:45»; пусто — нет. */
  time: string;
  /** Текст с уровнями `4V` / `V4` / `1-3V` чипами — лист строк. */
  rich: SheetRichText;
}

/** Сет — «куб» листа (§3.2). */
export interface SheetCube {
  /** Номер по порядку выполнения (①②…): номер сета у тренера. */
  no: number;
  /** Своё название или «Set N». */
  title: string;
  /**
   * Только своё название тренера; null — его нет (в том числе «Set N», которое версия уровня
   * подставляет вместо пустого). Лист строк подписи «Set N» не печатает.
   */
  ownTitle: string | null;
  /** Повтор сета целиком; 1 — без «2×». */
  repeat: number;
  meters: number;
  /** «1100 m»; пусто — метров нет (0). */
  metersLabel: string | null;
  main: boolean;
  lines: SheetLine[];
}

/** Место в левой колонке «главного справа»: куб или указатель «➜ ④» на месте главного. */
export type SheetLeftItem =
  | { kind: 'cube'; cube: SheetCube }
  | { kind: 'pointer'; nos: number[] };

/** Раскладка «главный справа» (§3.3); у листа, где её нет, — null. */
export interface MainLayoutModel {
  /** Главные сеты по порядку — правая колонка. */
  right: SheetCube[];
  /** Остальные в порядке листа; на месте главных — указатели. */
  left: SheetLeftItem[];
}

export interface SheetModel {
  dir: SheetDir;
  header: {
    groupName: string;
    level: { rank: number; name: string } | null;
    /** «2.5 km». */
    kmLabel: string;
  };
  note: string | null;
  /** Все кубы в порядке выполнения. */
  cubes: SheetCube[];
  /** Самая длинная ячейка листа в знаках (после «R:») — ширина колонки ячеек одна на весь лист. */
  cellChars: number;
  totalMeters: number;
  mainLayout: MainLayoutModel | null;
}
