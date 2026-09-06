/**
 * Реестр компонентов витрины.
 *
 * Витрина не знает ни одного компонента в лицо: сцена, кнопки пропсов и строка кода
 * рисуются из этих описаний. Добавить компонент = дописать сюда запись, отдельный экран
 * для этого верстать не нужно.
 *
 * `render` берёт значения пропсов строками (в URL всё равно строки) и сам приводит их к
 * типам компонента. Строки же идут в генератор кода — поэтому дефолт пишем ровно тем
 * литералом, что стоит в сигнатуре компонента: значение, равное дефолту, из кода выпадает.
 */
import React from 'react';
import UI_SwimmStyleIcon, {
  SWIM_ICON_SIZES,
  type SwimIconSize,
} from '../../projects/components/mix/swimm-style-icon/swimm-style-icon';

interface PropCommon {
  name: string;
  doc?: string;
  def: string;
  /** Обязательный по сигнатуре: печатаем в коде даже со значением по умолчанию. */
  required?: boolean;
  /** Проп принимает число: в сниппете печатаем `name={96}`, иначе скопированное не соберётся. */
  numeric?: boolean;
}

export type PropSpec =
  | (PropCommon & {
      kind: 'enum';
      options: string[];
      /**
       * Что писать на кнопке вместо самого значения. В код и в адрес всё равно уходит
       * значение — подпись нужна там, где голое число не говорит, что оно означает
       * (`96` против `96 · 24`, где второе число — кегль дистанции).
       */
      labels?: Record<string, string>;
      /** Подпись под кнопкой — когда голое значение не объясняет себя. */
      hints?: Record<string, string>;
    })
  | (PropCommon & {
      kind: 'text';
      presets?: string[];
      placeholder?: string;
    });

export interface ComponentEntry {
  /** Часть якоря: секция получает id `c-<id>`. Менять нельзя — по нему живут ссылки. */
  id: string;
  name: string;
  file: string;
  summary: string;
  /** Что стоит знать, прежде чем звать компонент. Пишем только выстраданное. */
  notes?: string[];
  props: PropSpec[];
  /** Ширина сцены по умолчанию, px. */
  defaultWidth: number;
  /**
   * Связь ручек сцены с пропами компонента: имя пропа, который на самом деле задаёт размер.
   *
   * Без неё у компонента с собственными размерными пропами получаются ДВЕ пары ручек об
   * одном и том же — сцена жмёт коробку снаружи, пропы задают размер внутри, — и они спорят
   * прямо на экране. Со связью «ширина» и «кегль» просто пишут в эти пропы: одно состояние,
   * подсветка ступеней честная, и шкала внизу показывает компонент в разных размерах, а не
   * в разных коробках.
   */
  sceneBind?: { width?: string; fontSize?: string };
  /**
   * Шкала под сценой: ряд копий компонента в разных размерах.
   *
   * `prop` — что перебираем, `steps` — ступени с подписями, `clear` — пропы, которые на
   * время шкалы обнуляются (точечные размеры перебили бы ступень, и весь ряд вышел бы
   * одинаковым). Ступени описаны здесь, а не зашиты в песочницу, потому что у каждого
   * компонента своя шкала — и потому что менять её нужно в одном месте с самим набором пар.
   *
   * Клик по копии применяет её ступень: шкала заодно работает переключателем.
   */
  scale?: {
    prop: string;
    steps: { value: string; label: string }[];
    clear?: string[];
  };
  render: (v: Record<string, string>) => React.ReactNode;
}

// Ступени `size` — числа, а не строки: проверка живёт здесь, чтобы сломаться на сборке,
// если тип однажды «съедет» на строковый и подсказка в IDE перестанет предлагать 48|64|96|140.
const _sizeIsNumeric: SwimIconSize = 96;
void _sizeIsNumeric;

/** Имена картинок в client/public/images/swimm-style-icon (плюс заведомо битое). */
const STYLE_NAMES = [
  'freestyle',
  'backstroke',
  'breaststroke',
  'butterfly',
  'medley',
  'individual_medley',
  'no-swim',
  'нет-такого-стиля',
];

export const COMPONENTS: ComponentEntry[] = [
  {
    id: 'ui-swimm-style-icon',
    name: 'UI_SwimmStyleIcon',
    file: 'src/projects/components/mix/swimm-style-icon/swimm-style-icon.tsx',
    summary: 'Иконка стиля плавания; опционально с подписью стиля или дистанции.',
    notes: [
      'Ступень size — один проп на оба размера: 48→10, 64→14, 96→24, 140→32. Точечные iconSize и lenSize перебивают её по своей оси.',
      'Размер картинки и кегль дистанции — две независимые ручки: iconSize и lenSize. Задал только iconSize — кегль берётся как 0.35 от него; не задал ничего — размер целиком из className, как было исторически.',
      'Заданный lenSize действует на ВСЕХ экранах. Без него на экране уже 768px кегль пиксельный (19px) и за размером плитки не следует — это не поломка, а старый дефолт.',
      'PNG нарисованы под светлый фон. В тёмной теме иконку кладут на белую плиту (см. swim-row__plate), иначе она пропадает — включи тёмный фон сцены и увидишь.',
      'Неизвестное имя стиля не роняет компонент: onError подменяет картинку на no-swim.png. Нажми «нет-такого-стиля».',
      'На экране уже 768px у компонента своя ветка CSS (кегль в px + подложка под числом). Ширина сцены её НЕ включает — медиазапрос слушает окно браузера, а не колонку.',
    ],
    defaultWidth: 0,
    sceneBind: { width: 'iconSize', fontSize: 'lenSize' },
    // Шкала = те же пары, что и проп `size`: смотреть имеет смысл на связки, которыми потом
    // и будут пользоваться, а не на произвольные ширины.
    scale: {
      prop: 'size',
      clear: ['iconSize', 'lenSize'],
      steps: Object.entries(SWIM_ICON_SIZES).map(([icon, len]) => ({
        value: icon,
        label: `${icon} · ${len}`,
      })),
    },
    props: [
      {
        kind: 'enum',
        name: 'styleName',
        required: true,
        doc: 'Имя стиля. Идёт прямо в путь картинки: пробелы → дефисы.',
        def: 'freestyle',
        options: STYLE_NAMES,
        hints: { 'нет-такого-стиля': 'проверка фоллбека' },
      },
      {
        kind: 'enum',
        name: 'styleType',
        doc: 'Что показывать рядом с иконкой.',
        def: 'icon-notext',
        options: ['icon-notext', 'icon-text', 'icon-len'],
        hints: {
          'icon-notext': 'только иконка',
          'icon-text': '+ название стиля',
          'icon-len': '+ дистанция',
        },
      },
      {
        kind: 'enum',
        name: 'lenPlacement',
        doc: 'Где стоит дистанция. Работает только при styleType="icon-len".',
        def: 'overlay',
        options: ['overlay', 'below', 'right'],
        hints: {
          overlay: 'поверх иконки (дефолт)',
          below: 'строкой под',
          right: 'столбиком справа',
        },
      },
      {
        kind: 'enum',
        name: 'size',
        numeric: true,
        doc: 'Ступень: одно значение задаёт пару «картинка + кегль». 48→10, 64→14, 96→24, 140→32.',
        def: '',
        options: ['', '48', '64', '96', '140'],
        // Кнопки подписаны парой — как ступени шкалы, из той же таблицы: иначе на кнопке
        // видно только сторону картинки, а второе число ступени остаётся невидимым.
        labels: {
          '': 'не задана',
          ...Object.fromEntries(
            Object.entries(SWIM_ICON_SIZES).map(([icon, len]) => [icon, `${icon} · ${len}`])
          ),
        },
      },
      {
        kind: 'text',
        name: 'iconSize',
        numeric: true,
        doc: 'Сторона картинки, px. Перебивает картинку из size; не задан ни тот, ни другой — размер из className.',
        def: '',
        presets: ['24', '32', '48', '64', '96', '140'],
        placeholder: '64',
      },
      {
        kind: 'text',
        name: 'lenSize',
        numeric: true,
        doc: 'Кегль дистанции, px (виден при styleType="icon-len"). Перебивает кегль из size; без обоих — 0.35 от iconSize, а без него 1.25em от плитки.',
        def: '',
        presets: ['10', '12', '14', '18', '24', '32'],
        placeholder: '18',
      },
      {
        kind: 'enum',
        name: 'lenPlate',
        doc: 'Подложка под числом. auto = как решает сам компонент: включена в overlay, выключена в below/right.',
        def: 'auto',
        options: ['auto', 'on', 'off'],
        hints: { auto: 'дефолт компонента', on: 'фон всегда', off: 'без фона' },
      },
      {
        kind: 'text',
        name: 'styleLen',
        doc: 'Дистанция. Пятизначная («10000») уменьшается сама — иначе не влезает в 46px.',
        def: '',
        presets: ['50', '100', '400', '1500', '3000', '10000'],
        placeholder: '100',
      },
      {
        kind: 'text',
        name: 'className',
        doc: 'Классы вызывающего. Пресеты — реальные строки из кода приложения.',
        def: '',
        presets: [
          'h-10 w-10 shrink-0',
          'font-bold text-base',
          'font-bold text-2xl',
          'w-[46px] shrink-0 rounded-[8px] bg-[rgba(226,240,252,0.92)] px-1 py-0.5 text-[15px]',
        ],
        placeholder: 'h-10 w-10',
      },
    ],
    render: (v) => (
      <UI_SwimmStyleIcon
        styleName={v.styleName}
        styleLen={v.styleLen}
        styleType={v.styleType as 'icon-notext' | 'icon-text' | 'icon-len'}
        lenPlacement={v.lenPlacement as 'overlay' | 'below' | 'right'}
        size={v.size ? (Number(v.size) as SwimIconSize) : undefined}
        iconSize={v.iconSize ? Number(v.iconSize) : undefined}
        lenSize={v.lenSize ? Number(v.lenSize) : undefined}
        lenPlate={v.lenPlate === 'auto' ? undefined : v.lenPlate === 'on'}
        className={v.className}
      />
    ),
  },
];

export function defaultValues(entry: ComponentEntry): Record<string, string> {
  return Object.fromEntries(entry.props.map((p) => [p.name, p.def]));
}

/**
 * JSX-вызов для копирования. Пропс со значением по умолчанию не печатаем — кроме
 * обязательных: без них скопированный код не скомпилируется.
 */
export function buildSnippet(
  entry: ComponentEntry,
  values: Record<string, string>,
  /** Порядок печати — тот же, что в панели: переставили пропы там, переставились и в коде. */
  props: PropSpec[] = entry.props
): string {
  const written = props.filter((p) => {
    const value = values[p.name] ?? '';
    if (p.required) return true;
    return value !== '' && value !== p.def;
  });
  if (written.length === 0) return `<${entry.name} />`;
  const lines = written.map((p) =>
    p.numeric ? `  ${p.name}={${values[p.name]}}` : `  ${p.name}="${values[p.name]}"`
  );
  return `<${entry.name}\n${lines.join('\n')}\n/>`;
}
