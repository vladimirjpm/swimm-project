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
import UI_SwimmStyleIcon from '../../projects/components/mix/swimm-style-icon/swimm-style-icon';

interface PropCommon {
  name: string;
  doc?: string;
  def: string;
  /** Обязательный по сигнатуре: печатаем в коде даже со значением по умолчанию. */
  required?: boolean;
}

export type PropSpec =
  | (PropCommon & {
      kind: 'enum';
      options: string[];
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
  render: (v: Record<string, string>) => React.ReactNode;
}

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
      'Размер задаёт ВЫЗЫВАЮЩИЙ через className — своей ширины у компонента нет. Кегль дистанции — 1.25em от плитки, поэтому вместе с шириной обычно задают и text-*.',
      'PNG нарисованы под светлый фон. В тёмной теме иконку кладут на белую плиту (см. swim-row__plate), иначе она пропадает — включи тёмный фон сцены и увидишь.',
      'Неизвестное имя стиля не роняет компонент: onError подменяет картинку на no-swim.png. Нажми «нет-такого-стиля».',
      'На экране уже 768px у компонента своя ветка CSS (кегль в px + подложка под числом). Ширина сцены её НЕ включает — медиазапрос слушает окно браузера, а не колонку.',
    ],
    defaultWidth: 64,
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
  const lines = written.map((p) => `  ${p.name}="${values[p.name]}"`);
  return `<${entry.name}\n${lines.join('\n')}\n/>`;
}
