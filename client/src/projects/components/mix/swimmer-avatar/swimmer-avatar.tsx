import React from 'react';
import './swimmer-avatar.css';
import UI_FlagEmoji from '../flag-icon/flag-icon';
import { identityDefaultAvatar } from '../swimmer-identity/swimmer-identity.types';

/**
 * Портрет пловца с флагом — ЕДИНСТВЕННОЕ место, где рисуется аватар спортсмена.
 *
 * Заведён потому, что три экрана делали это по-своему и расходились: карточка-попап
 * показывала фото с флагом, шапка страницы — фото либо БУКВУ и флаг только при заданной
 * стране, мини-карточка H2H — фото либо букву и флага не имела вовсе. Пловец без загруженного
 * фото выглядел в одном месте человеком, в другом — инициалом (замечено Владом 03.09.2026).
 *
 * Правила, которые компонент несёт за все экраны сразу:
 *  • нет своего фото — ставится дефолтный портрет ПО ПОЛУ (`identityDefaultAvatar`), а не
 *    буква: буква читается как «данных нет», хотя пловец в базе есть;
 *  • битая ссылка на фото падает на тот же дефолт (`onError`), а не на пустой квадрат;
 *  • флаг рисуется ВСЕГДА; страна не задана — домашняя (витрина израильская), как это и
 *    делала карточка-попап.
 *
 * Размер задаётся числом (px) и тянет за собой рамку и флаг: у шапки страницы он 96, у
 * карточки 76, у мини-карточки H2H 46.
 */
interface Props {
  avatarUrl?: string | null;
  /** male | female — от него зависит дефолтный портрет. */
  gender?: string | null;
  /** alpha-3 или alpha-2 — конвертирует сам `UI_FlagEmoji`. Пусто — домашняя страна. */
  countryCode?: string | null;
  /** Имя — только в `alt`; на картинке оно не печатается. */
  name?: string | null;
  /** Диаметр портрета в px. */
  size?: number;
  className?: string;
}

/** Страна по умолчанию: витрина израильская, отдельного флага «неизвестно» у нас нет. */
const HOME_FLAG = 'il';

const UI_SwimmerAvatar: React.FC<Props> = ({
  avatarUrl, gender, countryCode, name, size = 76, className,
}) => {
  const base = import.meta.env.BASE_URL;
  const fallback = identityDefaultAvatar(gender, base);

  return (
    <span
      className={`swimmer-avatar${className ? ` ${className}` : ''}`}
      style={{ width: size, height: size }}
    >
      <img
        src={avatarUrl || fallback}
        alt={name ?? ''}
        className="swimmer-avatar__img"
        onError={(e) => {
          // Второй раз не подставляем — иначе битый дефолт зациклил бы onError.
          if (e.currentTarget.src.endsWith(fallback)) return;
          e.currentTarget.src = fallback;
        }}
      />
      <span className="swimmer-avatar__flag">
        {/* Размер флага задаёт CSS долей от портрета, а не проп: экраны ужимают аватар
            медиазапросами, и фиксированный флаг на уменьшенном портрете занимал половину
            картинки. Картинку просим крупную — вниз она масштабируется без потерь. */}
        <UI_FlagEmoji countryCode={countryCode || HOME_FLAG} size="28x21" className="rounded" />
      </span>
    </span>
  );
};

export default UI_SwimmerAvatar;
