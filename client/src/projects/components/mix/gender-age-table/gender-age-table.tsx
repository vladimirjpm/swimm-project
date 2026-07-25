import React from 'react';
import UI_SwimmerNameCell from '../swimmer-name-cell/swimmer-name-cell';
import UI_DateIcon from '../date-icon/date-icon';

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
  /** Дата DD/MM/YYYY — показывается, если showDate. */
  date?: string;
  /** Если задан — имя становится ссылкой на страницу пловца. */
  swimmerId?: number;
}

export interface GenderAgeRow {
  /** Возраст или возрастная группа: "12" | "25-29". */
  age: string;
  male?: GenderAgeEntry;
  female?: GenderAgeEntry;
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
  /** Оборачивать возраст в скобки: [25-29]. */
  ageBrackets?: boolean;
}

function Cell({ entry, gender, showClubIcon, showDate }: {
  entry: GenderAgeEntry; gender: 'male' | 'female'; showClubIcon?: boolean; showDate?: boolean;
}) {
  const isMale = gender === 'male';
  const s = GENDER[gender];
  const value = (
    <div className={`shrink-0 text-[15px] font-extrabold leading-tight tabular-nums sm:text-[17px] ${s.accent}`}>
      {entry.value}
    </div>
  );

  // Имя/клуб ВСЕГДА через общий UI_SwimmerNameCell (единый модуль для HPA и ISR
  // Masters Records); showClubIcon лишь включает иконку. Дата (если showDate) —
  // отдельной строкой под ним (у UI_SwimmerNameCell своего слота даты нет).
  const info = (
    <div className={`min-w-0 flex-1 ${isMale ? '' : 'text-right'}`}>
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
  ageBrackets = false,
}: UI_GenderAgeTableProps) {
  if (rows.length === 0) return null;
  const showMale = rows.some((r) => r.male);
  const showFemale = rows.some((r) => r.female);

  return (
    <div
      className="grid items-center gap-x-2 gap-y-1.5 sm:gap-x-4"
      style={{ gridTemplateColumns: `${showMale ? '1fr' : ''} ${ageColWidth}px ${showFemale ? '1fr' : ''}`.trim() }}
    >
      {showMale && <div className={`text-right text-[10px] font-extrabold sm:text-[11px] ${GENDER.male.head}`}>{menLabel}</div>}
      <div className="text-center text-[10px] font-extrabold text-[#9098a4] sm:text-[11px]">{ageLabel}</div>
      {showFemale && <div className={`text-[10px] font-extrabold sm:text-[11px] ${GENDER.female.head}`}>{womenLabel}</div>}
      {rows.map((r) => (
        <React.Fragment key={r.age}>
          {showMale && (r.male ? <Cell entry={r.male} gender="male" showClubIcon={showClubIcon} showDate={showDate} /> : <div />)}
          <div className="whitespace-nowrap text-center text-[10px] font-extrabold text-[#5b6470] dark:text-[#aab0bd] sm:text-[11px]">
            {ageBrackets ? `[${r.age}]` : r.age}
          </div>
          {showFemale && (r.female ? <Cell entry={r.female} gender="female" showClubIcon={showClubIcon} showDate={showDate} /> : <div />)}
        </React.Fragment>
      ))}
    </div>
  );
}
