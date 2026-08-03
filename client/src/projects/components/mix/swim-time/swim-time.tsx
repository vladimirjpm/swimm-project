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

interface SwimTimeProps {
  time: string;
  /** null/undefined — время в порядке, значок не рисуется. */
  quality?: SwimQuality | null;
  /** Классы на само время — типографика остаётся за вызывающим экраном. */
  className?: string;
}

const UI_SwimTime: React.FC<SwimTimeProps> = ({ time, quality, className = '' }) => {
  const [open, setOpen] = React.useState(false);
  if (!quality) return <span className={className}>{time}</span>;

  return (
    <>
      <span className={className}>{time}</span>
      <button
        type="button"
        onClick={(e) => {
          // Карточки и строки раскрываются по клику — объяснялка не должна их трогать.
          e.stopPropagation();
          setOpen(true);
        }}
        aria-label="Quality warning"
        // Значок, а не подпись: на компактных карточках (Record wall клуба) текста не хватит,
        // а цвет самого времени уже занят тонами ступени рекорда.
        className="ml-1 align-middle text-[11px] leading-none text-amber-600 hover:opacity-80 dark:text-amber-400"
      >
        ⚠
      </button>
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
