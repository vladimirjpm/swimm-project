import './swimm-style-icon.css';
import { useAppDispatch } from '../../../../store/store';
import React from 'react';

/**
 * Готовые пары «размер картинки → кегль дистанции». Одно число вместо двух: подобранные
 * сочетания, на которых число читается и не спорит с рисунком.
 *
 * Ключ — сторона картинки в px, значение — кегль дистанции в px.
 */
export const SWIM_ICON_SIZES = {
  /** Плитка строки и ячейки таблицы. */
  48: 10,
  /** Карточки и чипы заплыва. */
  64: 14,
  /** Крупный чип фильтра, зумы стартового протокола. */
  96: 24,
  /** Витринный/герой-размер: одна иконка на весь блок. */
  140: 32,
} as const;

/** Допустимые значения пропа `size`: 48 | 64 | 96 | 140. */
export type SwimIconSize = keyof typeof SWIM_ICON_SIZES;

/**
 * Пара размеров по ступени — для случаев, когда компонент рисует кто-то другой, а размеры
 * нужны те же (своя вёрстка плитки, макет, замер).
 *
 * ```ts
 * const { iconSize, lenSize } = swimIconSizes(96); // { iconSize: 96, lenSize: 24 }
 * ```
 */
export const swimIconSizes = (size: SwimIconSize) => ({
  iconSize: size,
  lenSize: SWIM_ICON_SIZES[size],
});

interface UI_SwimmStyleIconProps {
  styleName: string;
  styleLen?: string;
  styleType?: 'icon-notext' | 'icon-text' | 'icon-len';
  /**
   * Где стоит дистанция в режиме `icon-len` (добавлено 31.08.2026):
   *
   * - `overlay` — **по умолчанию**, как было всегда: числом поверх иконки в правом верхнем
   *   углу. Плотно, но число ложится на рисунок и на узкой плитке спорит с ним.
   * - `below` — строкой ПОД иконкой. Число не перекрывает рисунок и читается как подпись.
   * - `right` — столбиком справа от иконки, когда по высоте места нет, а по ширине есть.
   *
   * Дефолт менять нельзя: `overlay` стоит на дюжине экранов (таблица результатов, My media,
   * карточки), и смена умолчания переставила бы число сразу везде.
   */
  lenPlacement?: 'overlay' | 'below' | 'right';
  /**
   * Ступень размера: одно число задаёт СРАЗУ пару «картинка + кегль дистанции» из
   * `SWIM_ICON_SIZES` — 48 → 10, 64 → 14, 96 → 24, 140 → 32.
   *
   * Это способ по умолчанию: пары подобраны так, что число читается и не спорит с рисунком,
   * и на всех экранах связка остаётся одинаковой. `iconSize` и `lenSize` нужны, только когда
   * ступени не подходят — они перебивают `size` каждый по отдельности:
   *
   * ```tsx
   * <UI_SwimmStyleIcon size={96} … />                        // 96 / 24
   * <UI_SwimmStyleIcon size={96} lenSize={12} … />           // 96 / 12
   * ```
   */
  size?: SwimIconSize;
  /**
   * Размер картинки: число = пиксели, строка = любая CSS-длина (`'4rem'`, `'100%'`).
   * Перебивает картинку из `size`, кегль при этом остаётся ступенчатым.
   *
   * Раньше размер задавался только классом вызывающего (`w-10`, `w-[46px]`), а кегль
   * дистанции считался от `font-size` того же корня — покрутить одно, не задев другое, было
   * нельзя. Здесь это две независимые ручки: `iconSize` и `lenSize`.
   *
   * Не задан — поведение прежнее, размер целиком из `className`. Поэтому существующие
   * вызовы не двигаются.
   */
  iconSize?: number | string;
  /**
   * Кегль дистанции: число = пиксели, строка = любая CSS-длина.
   *
   * Не задан, но задан `iconSize` — берётся 0.35 от него (проверенная пропорция: число
   * читается и не спорит с рисунком). Не заданы оба — 1.25em от плитки, то есть кегль идёт
   * за `text-*` вызывающего. Одинаково на всех экранах: пиксельный кегль из мобильного
   * медиазапроса, который раньше перебивал и то и другое, снят 2026-09-06.
   */
  lenSize?: number | string;
  /**
   * Подложка под числом. **По умолчанию включена в режиме `overlay`** (решение Влада,
   * 06.09.2026): там число лежит ПОВЕРХ рисунка и без фона теряется на брызгах и тёмном
   * силуэте. В `below` и `right` подложки нет и по умолчанию — число там стоит рядом с
   * картинкой, а не на ней.
   *
   * Явное `lenPlate={false}` снимает фон — например там, где иконка и так лежит на белой
   * плите (`swim-row__plate`) и подложка читается как лишний прямоугольник вокруг числа.
   * Обратное включение действует только на `overlay`: в остальных режимах фон рисовать не
   * подо что, число стоит рядом с картинкой.
   *
   * Плотность — токен `--swim-len-plate` на вызывающем.
   */
  lenPlate?: boolean;
  className?: string; // ✅ Добавлен className
}

/** Число — в пиксели, строка — как есть. Пустое значение переменную не ставит вовсе. */
const cssLength = (value?: number | string): string | undefined =>
  typeof value === 'number' ? `${value}px` : value || undefined;

const UI_SwimmStyleIcon: React.FC<UI_SwimmStyleIconProps> = ({
  styleName,
  styleLen = '',
  styleType = 'icon-notext',
  lenPlacement = 'overlay',
  size,
  iconSize,
  lenSize,
  lenPlate,
  className = '', // ✅ Значение по умолчанию
}) => {
  const dispatch = useAppDispatch();

  // Формируем путь к изображению
 let imageSrc;
 const base = import.meta.env.BASE_URL;
try {
  if (typeof styleName !== 'string') {
    throw new TypeError(`styleName должен быть string, а сейчас ${typeof styleName}`);
  }
  imageSrc = `${base}images/swimm-style-icon/${styleName.replaceAll(' ', '-')}.png`;
} catch (err) {
  console.error('Ошибка при формировании imageSrc:', err);
  // Запасной вариант, чтобы компонент не совсем упал
  imageSrc = `${base}images/swimm-style-icon/no-swim.png`;
}

  // Размеры едут CSS-переменными, а не готовыми классами: их видит и вложенная подпись
  // дистанции, и картинка, а вызывающий может перебить любую из них своим стилем.
  // Ступень `size` — основа, точечные `iconSize`/`lenSize` перебивают её каждый по своей
  // оси: так «мне нужен тот же размер, но число помельче» пишется одним пропом, а не двумя.
  const step = size !== undefined ? swimIconSizes(size) : null;
  const iconValue = cssLength(iconSize ?? step?.iconSize);
  // `lenSize` ставится в обе переменные: пятизначная дистанция читает свою (`--swim-len-xl`,
  // у неё меньшая доля от иконки), и без этой строки явно заданный кегль на «10000» не
  // действовал бы — выигрывала бы пропорция.
  const lenValue = cssLength(lenSize ?? step?.lenSize);
  const sizeVars = {
    ...(iconValue ? { '--swim-icon': iconValue } : null),
    ...(lenValue ? { '--swim-len': lenValue, '--swim-len-xl': lenValue } : null),
  } as React.CSSProperties;

  // Умолчание зависит от места числа: поверх рисунка фон нужен, рядом с ним — нет.
  // `??`, а не `||`: `lenPlate={false}` обязан отключать подложку, а не проваливаться в дефолт.
  // Проверка режима обязательна: без неё класс подложки липнул и к иконкам без дистанции
  // (фильтр стилей — `icon-notext`), где подкладывать нечего подо что.
  const plateOn = styleType === 'icon-len' && (lenPlate ?? lenPlacement === 'overlay');

  const rootClass = [
    'dv-swimm-icon',
    iconValue ? 'dv-swimm-icon--sized' : '',
    plateOn ? 'dv-swimm-icon--len-plate' : '',
    className,
  ]
    .filter(Boolean)
    .join(' ');

  const img = (
    <img
      src={imageSrc}
      alt={styleName}
      /* width={300} */
      className="object-contain"
      onError={(e) => {
        e.currentTarget.src = `${base}images/swimm-style-icon/no-swim.png`; // fallback картинка
      }}
    />
  );

  if (styleType === 'icon-text') {
    return (
      <div className={`${rootClass} flex flex-col items-center space-y-1 text-gray-800`} style={sizeVars}>
        {img}
        <span>{styleName}</span>
      </div>
    );
  }

  if (styleType === 'icon-len') {
    const len = String(styleLen ?? '');
    // Дистанции бывают четырёх- и пятизначные: чемпионат на 3 км в бассейне, открытая
    // вода (1600/5000/10000). Раньше подпись стояла ВЫШЕ своей коробки
    // (`margin-top: -10px`) и там, где у карточки `overflow: hidden`, её срезало.
    // Теперь она прижата внутрь угла; кегль уменьшается только у пятизначной — она одна
    // не помещается в самую узкую плитку (чип заплыва My media, 46px).

    // Раскладка зависит от места дистанции: `right` ставит её в строку с иконкой, остальные
    // оставляют колонку. `relative` нужен только `overlay` — абсолютной подписи.
    const box = lenPlacement === 'right'
      ? 'flex flex-row items-center gap-1.5'
      : 'relative flex flex-col items-center space-y-1';

    return (
      <div className={`${rootClass} ${box} text-gray-800`} style={sizeVars}>
        {img}
        <div
          className={`style-len style-len--${lenPlacement} text-red-700 ${len.length >= 5 ? 'style-len--xl' : ''}`}
        >
          {len}
        </div>
      </div>
    );
  }

  return (
    <div className={`${rootClass} flex items-center justify-center shadow text-gray-900`} style={sizeVars}>
      {img}
    </div>
  );
};

export default UI_SwimmStyleIcon;
