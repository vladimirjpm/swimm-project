import React from 'react';
import UI_SwimmerNameCell from '../swimmer-name-cell/swimmer-name-cell';
import UI_DateIcon from '../date-icon/date-icon';
import UI_SwimTime, { SwimQuality } from '../swim-time/swim-time';

// Единая таблица «♂ MEN | AGE | ♀ WOMEN» в стиле ISR Masters/Age Records.
// Используется и High Point Award (значение = очки), и ISR Masters Records (значение = время).
// Параметризована: значение показывается СТРОКОЙ (value), иконка клуба и дата — опциональны.
// Гендерные цвета — те же, что у ISR Records (male синий / female розовый).

const GENDER = {
  male: {
    accent: 'text-[#1e6fd6] dark:text-[#5aa2f5]',
    soft: 'bg-[#eaf2fd] dark:bg-[rgba(90,162,245,0.16)]',
    deep: 'text-[#123a70] dark:text-[#dbe8fb]',
    head: 'text-[#1e6fd6] dark:text-[#5aa2f5]',
  },
  female: {
    accent: 'text-[#d6417f] dark:text-[#f072a6]',
    soft: 'bg-[#fdeff5] dark:bg-[rgba(240,114,166,0.16)]',
    deep: 'text-[#7a1f4b] dark:text-[#fbdcec]',
    head: 'text-[#d6417f] dark:text-[#f072a6]',
  },
} as const;

export interface GenderAgeEntry {
  firstName: string;
  lastName?: string;
  club?: string;
  /** Значение к показу СТРОКОЙ: время ("00:34.10") или очки ("987"). */
  value: string;
  /** Качество времени (И11): для рекордов — открытая претензия к записи справочника. */
  quality?: SwimQuality | null;
  /** Дата DD/MM/YYYY — показывается, если showDate. */
  date?: string;
  /** Если задан — имя становится ссылкой на страницу пловца. */
  swimmerId?: number;
  /**
   * Мелкая подпись под датой. Сейчас единственный потребитель — отладочная опция
   * ShowAgeRecordsDetails: «born 2011 · age 10» под датой рекорда. Пусто — строки нет.
   */
  note?: string | null;
}

export interface GenderAgeRow {
  /** Возраст или возрастная группа: "12" | "25-29". */
  age: string;
  /** Один участник или несколько — при ничьей (High Point: равное число очков). */
  male?: GenderAgeEntry | GenderAgeEntry[];
  female?: GenderAgeEntry | GenderAgeEntry[];
}

/** Ячейка может нести нескольких (ничья) — приводим к массиву в одном месте. */
function entriesOf(cell: GenderAgeEntry | GenderAgeEntry[] | undefined): GenderAgeEntry[] {
  if (!cell) return [];
  return Array.isArray(cell) ? cell : [cell];
}

/**
 * Ширина ≥ sm (640px). Иконку клуба на мобиле убираем в JS, а не CSS: она рендерится
 * внутри UI_SwimmerNameCell без своего класса-крючка, и прятать её селектором пришлось бы
 * по позиции в разметке — сломается при первой же правке ячейки.
 */
function useIsSmUp(): boolean {
  const query = '(min-width: 640px)';
  const [isSmUp, setIsSmUp] = React.useState(
    () => typeof window !== 'undefined' && window.matchMedia(query).matches,
  );

  React.useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = () => setIsSmUp(mql.matches);
    onChange();
    mql.addEventListener('change', onChange);
    // Подписка на resize — страховка: при программной смене метрик вьюпорта (devtools,
    // эмуляция устройства) событие change у matchMedia приходит не всегда.
    window.addEventListener('resize', onChange);
    return () => {
      mql.removeEventListener('change', onChange);
      window.removeEventListener('resize', onChange);
    };
  }, []);

  return isSmUp;
}

interface UI_GenderAgeTableProps {
  rows: GenderAgeRow[];
  /** Имя/клуб через UI_SwimmerNameCell с иконкой клуба (иначе — простой текст). */
  showClubIcon?: boolean;
  /** Показать дату (UI_DateIcon) под именем/клубом. Игнорируется при showClubIcon. */
  showDate?: boolean;
  menLabel?: string;
  womenLabel?: string;
  ageLabel?: string;
  ageColWidth?: number;
  /** Ширина колонки возраста до брейкпоинта sm. По умолчанию — как на десктопе. */
  ageColWidthMobile?: number;
  /** Прятать иконку клуба до sm: на узком экране она съедает ширину, нужную имени. */
  hideClubIconMobile?: boolean;
  /** Оборачивать возраст в скобки: [25-29]. */
  ageBrackets?: boolean;
}

function Cell({ entries, gender, showClubIcon, showDate }: {
  entries: GenderAgeEntry[]; gender: 'male' | 'female'; showClubIcon?: boolean; showDate?: boolean;
}) {
  const isMale = gender === 'male';
  const s = GENDER[gender];

  // При ничьей значение у всех одинаковое (в этом и суть ничьей) — печатаем его ОДИН раз,
  // а имена ставим рядом с переносом. Дублировать «10 · 10 · 10» было бы шумом.
  const value = (
    <div className={`shrink-0 text-[15px] font-extrabold leading-tight tabular-nums sm:text-[17px] ${s.accent}`}>
      <UI_SwimTime time={entries[0].value} quality={entries[0].quality} />
    </div>
  );

  // Имя/клуб ВСЕГДА через общий UI_SwimmerNameCell (единый модуль для HPA и ISR
  // Masters Records); showClubIcon лишь включает иконку. Дата (если showDate) —
  // отдельной строкой под ним (у UI_SwimmerNameCell своего слота даты нет).
  // Несколько награждённых (ничья): на узком экране — строго в столбик, с sm — в строку
  // с переносом. Рядом на мобиле два имени с клубом не читаются.
  const info = (
    <div
      className={`flex min-w-0 flex-1 flex-col gap-y-1 sm:flex-row sm:flex-wrap sm:items-center sm:gap-x-3 ${
        isMale ? 'items-start' : 'items-end justify-end text-right sm:items-center'
      }`}
    >
      {entries.map((entry, i) => (
        <div key={entry.swimmerId ?? `${entry.firstName}-${i}`} className="min-w-0">
          <UI_SwimmerNameCell
            firstName={entry.firstName}
            lastName={entry.lastName}
            club={entry.club}
            showClubIcon={showClubIcon}
            clubIconSide={isMale ? 'left' : 'right'}
            rowJustify={isMale ? 'start' : 'end'}
            clubIconWidth="7"
            onClick={entry.swimmerId && entry.swimmerId > 0 ? () => { window.location.href = `/swimmers/${entry.swimmerId}`; } : undefined}
            className="min-w-0"
            nameBlockClassName={isMale ? 'min-w-0' : 'min-w-0 flex-1 text-right'}
            firstLineClassName={`truncate text-[11px] font-bold sm:text-[12.5px] ${s.deep}`}
            secondLineClassName="truncate text-[10px] text-[#8a93a3] sm:text-[11px]"
          />
          {showDate && entry.date && (
            <UI_DateIcon
              styleType="row-style-1"
              date={entry.date}
              fontClassName="text-[9px] sm:text-[10px] text-[#aab0bd] tabular-nums"
              className={isMale ? '!justify-start' : '!justify-end'}
            />
          )}
          {entry.note && (
            <div
              className={`text-[9px] sm:text-[10px] tabular-nums text-[#8a93a3] ${
                isMale ? 'text-left' : 'text-right'
              }`}
            >
              {entry.note}
            </div>
          )}
        </div>
      ))}
    </div>
  );

  return (
    <div className={`flex items-center gap-2 rounded-lg px-2.5 py-[5px] sm:py-2 ${s.soft}`}>
      {isMale ? <>{info}{value}</> : <>{value}{info}</>}
    </div>
  );
}

export default function UI_GenderAgeTable({
  rows,
  showClubIcon = false,
  showDate = false,
  menLabel = '♂ MEN',
  womenLabel = '♀ WOMEN',
  ageLabel = 'AGE',
  ageColWidth = 64,
  ageColWidthMobile,
  hideClubIconMobile = false,
  ageBrackets = false,
}: UI_GenderAgeTableProps) {
  const isSmUp = useIsSmUp();
  if (rows.length === 0) return null;
  const showMale = rows.some((r) => entriesOf(r.male).length > 0);
  const showFemale = rows.some((r) => entriesOf(r.female).length > 0);
  const withClubIcon = showClubIcon && (isSmUp || !hideClubIconMobile);

  // Колонка возраста бывает уже на мобиле (там дорог каждый пиксель под имя+клуб).
  // Ширины прокидываем CSS-переменными: шаблон колонок зависит ещё и от showMale/showFemale,
  // одним arbitrary-классом Tailwind это не выразить.
  const template = (ageWidth: number) =>
    // minmax(0,1fr), а не 1fr: у 1fr минимум — max-content, и длинное имя с клубом
    // распирало сетку шире карточки (на мобиле давало горизонтальный скролл).
    `${showMale ? 'minmax(0,1fr)' : ''} ${ageWidth}px ${showFemale ? 'minmax(0,1fr)' : ''}`.trim();

  return (
    <div
      className="grid grid-cols-[var(--gat-cols-mobile)] items-center gap-x-2 gap-y-1.5 sm:grid-cols-[var(--gat-cols)] sm:gap-x-4"
      style={{
        '--gat-cols': template(ageColWidth),
        '--gat-cols-mobile': template(ageColWidthMobile ?? ageColWidth),
      } as React.CSSProperties}
    >
      {showMale && <div className={`text-right text-[10px] font-extrabold sm:text-[11px] ${GENDER.male.head}`}>{menLabel}</div>}
      <div className="text-center text-[10px] font-extrabold text-[#9098a4] sm:text-[11px]">{ageLabel}</div>
      {showFemale && <div className={`text-[10px] font-extrabold sm:text-[11px] ${GENDER.female.head}`}>{womenLabel}</div>}
      {rows.map((r) => {
        const males = entriesOf(r.male);
        const females = entriesOf(r.female);
        return (
          <React.Fragment key={r.age}>
            {showMale && (males.length > 0
              ? <Cell entries={males} gender="male" showClubIcon={withClubIcon} showDate={showDate} />
              : <div />)}
            <div className="whitespace-nowrap text-center text-[10px] font-extrabold text-[#5b6470] dark:text-[#aab0bd] sm:text-[11px]">
              {ageBrackets ? `[${r.age}]` : r.age}
            </div>
            {showFemale && (females.length > 0
              ? <Cell entries={females} gender="female" showClubIcon={withClubIcon} showDate={showDate} />
              : <div />)}
          </React.Fragment>
        );
      })}
    </div>
  );
}
