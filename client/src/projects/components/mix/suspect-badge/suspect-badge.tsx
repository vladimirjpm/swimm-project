import React from 'react';
import UI_InfoPopup, { InfoText } from '../../info-popup/info-popup';

/**
 * Бейдж «⚠ CHECK SOURCE» у заплыва, помеченного админом как ошибка ПРОТОКОЛА
 * (`suspect_reason`, docs/data-integrity.md).
 *
 * Строку мы не прячем и не правим — протокол напечатан так, как напечатан, а наша копия
 * обязана совпадать с источником. Но и молчать нельзя: бессмыслица вроде 200 вольным за
 * 1:53 у 13-летнего иначе читается как достижение и получает бейдж рекорда.
 *
 * Клик открывает объяснение: смотрят его родители и тренеры, а не разработчик, поэтому
 * текст есть на трёх языках (по умолчанию английский — правило интерфейса).
 */

const TITLE: InfoText = {
  en: 'This result looks wrong',
  ru: 'Результат выглядит ошибочным',
  he: 'התוצאה נראית שגויה',
};

const BODY: InfoText = {
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
};

const FOOTNOTE: InfoText = {
  en: 'Flagged manually by an administrator after review.',
  ru: 'Помечено администратором вручную после разбора.',
  he: 'סומן ידנית על ידי מנהל לאחר בדיקה.',
};

const UI_SuspectBadge: React.FC = () => {
  const [open, setOpen] = React.useState(false);

  return (
    <>
      <button
        type="button"
        onClick={(e) => {
          // Строка результата раскрывается по клику — объяснялка не должна её трогать.
          e.stopPropagation();
          setOpen(true);
        }}
        className="inline-flex items-center gap-1 rounded-[7px] border border-current px-1.5 py-0.5
                   text-[8.5px] font-extrabold tracking-wide text-amber-600 dark:text-amber-400
                   cursor-pointer hover:opacity-80"
      >
        ⚠ CHECK SOURCE
      </button>
      <UI_InfoPopup
        open={open}
        onClose={() => setOpen(false)}
        title={TITLE}
        body={BODY}
        footnote={FOOTNOTE}
      />
    </>
  );
};

export default UI_SuspectBadge;
