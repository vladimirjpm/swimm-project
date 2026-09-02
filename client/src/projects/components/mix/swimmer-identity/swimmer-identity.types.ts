/**
 * Общий вход семейства `UI_SwimmerIdentity*` — «кто этот пловец», без единой цифры о том,
 * как он плывёт. Статистика (KPI, медали, разряд, достижения) остаётся за вызывающим
 * экраном и приезжает слотом: иначе мини-вариант потащил бы за собой то, что ему не нужно.
 *
 * Варианты семейства различаются МЕСТОМ, а не сущностью:
 *  • `UI_SwimmerIdentityHero` — шапка страницы `/swimmers/{id}` (палитра Deep);
 *  • `UI_SwimmerIdentityCard` — identity-бар попапа карточки (палитра results);
 *  • `UI_SwimmerIdentityMini` — компактная строка/чип (появится позже).
 *
 * Имя приходит УЖЕ выбранным (иврит по умолчанию, английский фоллбеком — правило проекта):
 * у разных источников свои пары полей, и решать это заново внутри компонента нечем.
 */
export interface SwimmerIdentity {
  /** null — пловец не заведён в базе (строка протокола без привязки): действия недоступны. */
  id?: number | null;
  name: string;
  birthYear?: number | null;
  /**
   * Возраст В СЕЗОНЕ (SeasonMath.AgeInSeason), а не на сегодня: осенний и весенний старты
   * одного сезона обязаны показывать один возраст.
   */
  ageInSeason?: number | null;
  /**
   * Готовая подпись возраста, когда у экрана свой источник. В карточке-попапе это
   * ВОЗРАСТНАЯ ГРУППА заплыва (`event_style_age`), а она бывает диапазоном («17-18»), то
   * есть числом её не выразить. Задана — печатается как есть, иначе считается из
   * `ageInSeason`/`birthYear`.
   */
  ageLabel?: string | null;
  clubName?: string | null;
  clubId?: number | null;
  /** alpha-3 или alpha-2 — конвертирует сам `UI_FlagEmoji`. */
  countryCode?: string | null;
  avatarUrl?: string | null;
  /** male | female — только для дефолтной картинки там, где она рисуется. */
  gender?: string | null;
}

/**
 * Подпись под именем: «14 year (2012)», иначе «b. 2012», иначе прочерк.
 * Возраст без года рождения не показываем — он ничем не проверяется читателем.
 */
export const identityAgeLabel = (identity: SwimmerIdentity): string => {
  if (identity.ageLabel) return identity.ageLabel;
  if (identity.ageInSeason != null && (identity.birthYear ?? 0) > 0) {
    return `${identity.ageInSeason} year (${identity.birthYear})`;
  }
  if ((identity.birthYear ?? 0) > 0) return `b. ${identity.birthYear}`;
  return '—';
};

/** Заглушка аватара — первая буква имени. Пустое имя даёт «?», а не пустой круг. */
export const identityInitial = (name: string): string =>
  (name.trim().charAt(0) || '?').toUpperCase();

/**
 * Дефолтная картинка пловца по полу (`public/images/swimmers/default-*.png`).
 * Пол в данных бывает пустым — тогда женская, как и было в карточке до выноса.
 */
export const identityDefaultAvatar = (gender?: string | null, base = '/'): string =>
  `${base}images/swimmers/default-${gender === 'male' ? 'male' : 'female'}.png`;
