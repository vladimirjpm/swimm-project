import React from 'react';
import UI_InfoPopup, { InfoText } from '../../info-popup/info-popup';

/**
 * Время заплыва — ЕДИНСТВЕННЫЙ разрешённый способ вывести его в UI
 * (docs/plans/swim-time-quality-everywhere-plan.md, фаза К1).
 *
 * Зачем шов: признак «время под вопросом» должен быть виден везде, где время показывается,
 * а показывается оно в дюжине мест. Пока каждая страница форматирует время сама, «везде
 * показывать признак» означает «не забыть в двенадцати местах» — и забудут в первом же новом.
 *
 * Качества ДВА, и они не взаимозаменяемы:
 *  • protocol — ошибка самого протокола федерации (`Results.SuspectReason`);
 *  • record   — спорная запись справочника рекордов (`Sys_RecordIssues`).
 * Текст объяснения у них разный: в первом случае врёт протокол соревнования, во втором —
 * опубликованный рекорд. Общее одно: источник мы не правим, а помечаем.
 *
 * Нет признака — рисуется просто время, без единого лишнего пикселя.
 */

export type SwimQualityKind = 'protocol' | 'record';

/**
 * Полная формулировка для `title`/`aria-label` носителя пометки (строка результата,
 * плитка рекорда). В самом чипе только «Under review» — короткая подпись; развёрнутый
 * текст обязан быть рядом, иначе значок читается как украшение. Держим здесь, чтобы
 * подпись и объяснение не разъехались по экранам.
 */
export const SWIM_QUALITY_TITLE =
  'Suspected timing error. The result is being verified and may be corrected or removed.';

/**
 * Атрибуты СТРОКИ/КАРТОЧКИ, несущей спорное время (гибрид 15d): caution-лента слева
 * (класс `swim-flagged-row`, стили и токены — в `index.css`) плюс полный текст в
 * title/aria-label. Чип рисует сам `UI_SwimTime` — здесь только «обвязка» носителя.
 *
 * Живёт рядом с компонентом по той же причине, что и он сам: помеченный заплыв
 * показывается на нескольких экранах (таблица результатов, карточка спортсмена,
 * стена рекордов), и три копии одного className с одним текстом разъедутся на первой же
 * правке. Возвращает пустой объект, если качество не задано, — вызывающий просто
 * разворачивает его в JSX.
 */
export const swimFlaggedRowProps = (quality?: SwimQuality | null) =>
  quality
    ? { className: 'swim-flagged-row', title: SWIM_QUALITY_TITLE, 'aria-label': SWIM_QUALITY_TITLE }
    : {};

export interface SwimQuality {
  kind: SwimQualityKind;
  /** Код причины (`manual`, `personal_outlier`, …) — сейчас только для отладки/аналитики. */
  reason?: string | null;
}

const TITLE: Record<SwimQualityKind, InfoText> = {
  protocol: {
    en: 'This result looks wrong',
    ru: 'Результат выглядит ошибочным',
    he: 'התוצאה נראית שגויה',
  },
  record: {
    en: 'This official record looks wrong',
    ru: 'Официальный рекорд выглядит ошибочным',
    he: 'השיא הרשמי נראה שגוי',
  },
};

const BODY: Record<SwimQualityKind, InfoText> = {
  protocol: {
    en:
      'This swim does not fit the rest of the protocol — most likely a mistake in the official '
      + 'results file itself, not in our data.\n\n'
      + 'We keep the row exactly as published: our copy has to match the federation source. '
      + 'But a flagged swim does not count towards records and is not shown as an achievement.',
    ru:
      'Этот заплыв не сходится с остальным протоколом — скорее всего, ошибка в самом '
      + 'официальном файле результатов, а не в наших данных.\n\n'
      + 'Мы оставляем строку ровно такой, какой её опубликовали: наша копия обязана совпадать '
      + 'с источником федерации. Но помеченный заплыв не участвует в рекордах и не показывается '
      + 'как достижение.',
    he:
      'השחייה הזו לא מסתדרת עם שאר הפרוטוקול — ככל הנראה טעות בקובץ התוצאות הרשמי עצמו, '
      + 'ולא בנתונים שלנו.\n\n'
      + 'אנחנו משאירים את השורה בדיוק כפי שפורסמה: העותק שלנו חייב להתאים למקור של ההתאחדות. '
      + 'אבל שחייה מסומנת אינה נחשבת לשיאים ואינה מוצגת כהישג.',
  },
  record: {
    en:
      'This record is published by the federation as shown, but it does not add up — for '
      + 'example it is faster than the record of an older age group.\n\n'
      + 'We do not correct the source: our copy has to match the official list, otherwise the '
      + 'next update would silently bring the error back. We flag it instead and report it.',
    ru:
      'Этот рекорд опубликован федерацией именно так, но он не сходится — например, быстрее '
      + 'рекорда более старшей возрастной ступени.\n\n'
      + 'Мы не правим источник: наша копия обязана совпадать с официальным списком, иначе '
      + 'следующее обновление молча вернёт ошибку назад. Вместо этого мы её помечаем и сообщаем '
      + 'о ней федерации.',
    he:
      'השיא הזה פורסם על ידי ההתאחדות בדיוק כך, אבל הוא לא מסתדר — למשל הוא מהיר יותר '
      + 'מהשיא של קבוצת גיל מבוגרת יותר.\n\n'
      + 'אנחנו לא מתקנים את המקור: העותק שלנו חייב להתאים לרשימה הרשמית, אחרת העדכון הבא '
      + 'יחזיר את הטעות בשקט. במקום זאת אנחנו מסמנים אותה ומדווחים עליה.',
  },
};

const FOOTNOTE: Record<SwimQualityKind, InfoText> = {
  protocol: {
    en: 'Flagged after review by an administrator.',
    ru: 'Помечено администратором после разбора.',
    he: 'סומן על ידי מנהל לאחר בדיקה.',
  },
  record: {
    en: 'Source: official federation record list.',
    ru: 'Источник: официальный список рекордов федерации.',
    he: 'מקור: רשימת השיאים הרשמית של ההתאחדות.',
  },
};

/**
 * «+2.73» — насколько это время хуже лучшего в срезе. Формат один на продукт и живёт
 * рядом со временем: это его свойство, а не отдельная ячейка строки.
 *
 * У самого лидера отставания НЕТ — возвращается null и не рисуется ничего.
 * Прочерк был осмыслен в таблице с заголовком колонки «Behind leader»; в карточке
 * заголовков нет, и голое «—» ничего не значит.
 */
export const swimGapLabel = (ms?: number | null): string | null =>
  ms == null || ms <= 0 ? null : `+${(ms / 1000).toFixed(2)}`;

/**
 * Сравнение этого времени с эталоном — отставание от лидера среза, от лучшего в клубе,
 * от национального возрастного рекорда. Все они — СВОЙСТВА ВРЕМЕНИ, а не отдельные
 * ячейки строки: цифра читается только рядом со временем, к которому относится, иначе её
 * приходится объяснять заголовком колонки.
 *
 * НЕЧЕГО ПОКАЗЫВАТЬ — НЕ ПОКАЗЫВАЕМ: если эталона в данных нет (`ms` пуст и `holds`
 * не взведён), строка не рисуется вовсе. Голый прочерк был понятен в таблице с
 * заголовками колонок; в карточке заголовков нет, и он ничего не значит.
 */
export interface SwimTimeDelta {
  /** Подпись слева от числа («Δ club»). Без неё — одно число, как у отставания от лидера. */
  label?: string;
  /** На сколько мс это время хуже эталона. null и без `holds` — строка не рисуется. */
  ms?: number | null;
  /** Эталон принадлежит этому же пловцу — вместо числа печатается «record». */
  holds?: boolean;
  /** Качество самого эталона (спорный официальный рекорд) — значок рядом. */
  quality?: SwimQuality | null;
  /** Пояснение на наведении: что именно взято за эталон. */
  title?: string;
}

/** Есть ли что показывать: без эталона строка не рисуется. */
export const hasSwimDelta = (d: SwimTimeDelta): boolean => !!d.holds || d.ms != null;

interface SwimTimeProps {
  time: string;
  /** null/undefined — время в порядке, значок не рисуется. */
  quality?: SwimQuality | null;
  /** Классы на само время — типографика остаётся за вызывающим экраном. */
  className?: string;
  /**
   * Вид метки: 'icon' — значок ⚠ рядом со временем; 'chip' — подпись «⚠ Under review»
   * (стена рекордов — вариант 13a, строка результатов — гибрид 15d): голый значок не
   * считывался, а места на подпись хватает. Объяснялка у обоих одна.
   */
  marker?: 'icon' | 'chip';
  /**
   * Размер чипа: 'md' — стена рекордов (11px, паддинг 3/10), 'sm' — строка таблицы
   * результатов (10px, паддинг 2/9), где чип стоит под временем в узкой колонке.
   * Цвета у обоих одни и те же токены `--theme-flag-*` — разный только кегль.
   */
  chipSize?: 'sm' | 'md';
  /**
   * Отставание от лидера среза в мс — печатается «+2.73» СРАЗУ ЗА временем.
   * Здесь, а не отдельной ячейкой строки: цифра читается только рядом со временем,
   * к которому относится, иначе её приходится объяснять заголовком колонки.
   */
  gapMs?: number | null;
  /**
   * Остальные сравнения с эталонами — каждое своей строкой под временем
   * («Δ club +13.39», «Δ Israel record»). Пустые отбрасываются, см. `hasSwimDelta`.
   */
  deltas?: SwimTimeDelta[];
  /** Кегль и цвет строк сравнения — типографика за вызывающим экраном. */
  gapClassName?: string;
}

const UI_SwimTime: React.FC<SwimTimeProps> = ({
  time, quality, className = '', marker = 'icon', chipSize = 'md',
  gapMs = null, deltas, gapClassName = '',
}) => {
  const [open, setOpen] = React.useState(false);

  // Все сравнения с эталонами идут одним списком и встают СТРОКАМИ ПОД временем
  // (решение Влада 2026-08-27): в строку с цифрами они лезли в ширину узкой
  // колонки времени и спорили с ними кеглем. Отставание от лидера — такая же строка,
  // просто без подписи, поэтому `gapMs` — сахар над тем же механизмом.
  const allDeltas: SwimTimeDelta[] = [
    ...(gapMs != null && gapMs > 0 ? [{ ms: gapMs, title: 'Behind the leader' }] : []),
    ...(deltas ?? []),
  ].filter(hasSwimDelta);

  const gapNode = allDeltas.length > 0 && (
    <>
      {allDeltas.map((d, i) => (
        <span
          key={i}
          className={`block leading-none text-[11px] font-extrabold ${gapClassName}`}
          title={d.title}
        >
          {d.label && (
            <span className="mr-1 text-[9px] font-bold uppercase tracking-wide opacity-70">
              {d.label}
            </span>
          )}
          {d.holds ? (
            <span className="swim-time__delta--holds">record</span>
          ) : (
            swimGapLabel(d.ms)
          )}
          {/* Спорный ЭТАЛОН — значок с той же объяснялкой; вложенный вызов без своих
              сравнений, поэтому рекурсии нет. */}
          {d.quality && <UI_SwimTime time="" quality={d.quality} marker="icon" />}
        </span>
      ))}
    </>
  );

  if (!quality) {
    return (
      <>
        <span className={className}>{time}</span>
        {gapNode}
      </>
    );
  }

  // Карточки и строки раскрываются по клику — объяснялка не должна их трогать.
  const openInfo = (e: React.MouseEvent) => {
    e.stopPropagation();
    e.preventDefault();
    setOpen(true);
  };

  return (
    <>
      <span className={className}>{time}</span>
      {gapNode}
      {marker === 'chip' ? (
        <button
          type="button"
          onClick={openInfo}
          aria-label={TITLE[quality.kind].en}
          className={`inline-flex shrink-0 items-center gap-[5px] whitespace-nowrap rounded-full font-extrabold leading-none hover:opacity-80 ${
            chipSize === 'sm' ? 'px-[9px] py-[2px] text-[10px]' : 'px-[10px] py-[3px] text-[11px]'
          }`}
          style={{
            background: 'var(--theme-flag-chip-bg)',
            color: 'var(--theme-flag-text)',
            border: '1px solid var(--theme-flag-chip-border)',
          }}
        >
          <span aria-hidden="true">⚠</span>
          Under review
        </button>
      ) : (
      <button
        type="button"
        onClick={openInfo}
        aria-label="Quality warning"
        // Значок, а не подпись: в строках и таблицах на подпись нет места.
        className="ml-1 align-middle text-[11px] leading-none text-amber-600 hover:opacity-80 dark:text-amber-400"
      >
        ⚠
      </button>
      )}
      <UI_InfoPopup
        open={open}
        onClose={() => setOpen(false)}
        title={TITLE[quality.kind]}
        body={BODY[quality.kind]}
        footnote={FOOTNOTE[quality.kind]}
      />
    </>
  );
};

export default UI_SwimTime;
