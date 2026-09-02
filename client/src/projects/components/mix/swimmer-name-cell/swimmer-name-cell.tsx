import React from 'react';
import { RelaySwimmer } from '../../../../utils/interfaces/results';
import UI_ClubIcon from '../club-icon/club-icon';
import UI_RecordBadge, { type RecordKind } from '../record-badge/record-badge';
import '../text-effect/text-effect.css';

interface SwimmerNameCellProps {
  firstName: string;
  lastName?: string;
  firstNameEn?: string;
  lastNameEn?: string;
  club?: string;
  /** Id клуба — задан, эмблема клуба становится ссылкой на /clubs/{id} (UI_ClubIcon). */
  clubId?: number | null;
  isRelay?: boolean;
  relaySwimmersName?: string;
  relaySwimmersList?: RelaySwimmer[];
  onClick?: () => void;
  firstLineClassName?: string;
  secondLineClassName?: string;
  className?: string;
  showClubIcon?: boolean;
  /** Ширина/высота иконки клуба как Tailwind-размер (w-{n} h-{n}); по умолчанию '10'. */
  clubIconWidth?: string;
  /** Сторона иконки клуба относительно блока имя/клуб: 'right' (как сейчас) или 'left'. */
  clubIconSide?: 'left' | 'right';
  /** Выравнивание ряда «иконка+имя»: 'start' (по умолч.) или 'end' (правая колонка). */
  rowJustify?: 'start' | 'end';
  /** Классы блока «имя/клуб». По умолч. flex-1 (иконка к краю); задай фикс-ширину
   *  (напр. 'w-[120px] min-w-0'), чтобы иконка встала столбиком близко к тексту. */
  nameBlockClassName?: string;
  /** Пловец ДЕРЖИТ рекорд этой дистанции (матч по имени в справочнике). */
  isRecordHolder?: boolean;
  /** Класс рекорда: мастерская полоса или возрастная ступень — справочники у них разные. */
  recordKind?: RecordKind;
  /** primary-избранное («это я») — бейдж ★ ME рядом с именем */
  isMe?: boolean;
}

const UI_SwimmerNameCell: React.FC<SwimmerNameCellProps> = ({
  firstName,
  lastName,
  firstNameEn,
  lastNameEn,
  club,
  clubId,
  isRelay,
  relaySwimmersName,
  relaySwimmersList,
  onClick,
  firstLineClassName = 'text-xl font-bold',
  secondLineClassName = 'text-xs',
  className = '',
  showClubIcon = false,
  clubIconWidth = '10',
  clubIconSide = 'right',
  rowJustify = 'start',
  nameBlockClassName = 'min-w-0 flex-1',
  isRecordHolder = false,
  recordKind = 'age',
  isMe = false,
}) => {
  const displayName = isRelay
    ? club || 'Relay Team'
    : `${firstName}${lastName ? ` ${lastName}` : ''}`;

  const hasEnglishName =
    Boolean(firstNameEn && firstNameEn.trim()) ||
    Boolean(lastNameEn && lastNameEn.trim());

  // Формируем subText для эстафеты
  let subText: string | undefined;
  let relayList: React.ReactNode = null;
  if (isRelay) {
    if (relaySwimmersList && relaySwimmersList.length > 0) {
      relayList = (
        <div className={secondLineClassName}>
          {relaySwimmersList.map((s, i) => (
            <div key={i}>
              {s.first_name} {s.last_name}{s.birth_year ? ` (${s.birth_year})` : ''}
            </div>
          ))}
        </div>
      );
    } else if (relaySwimmersName) {
      relayList = (
        <div className={secondLineClassName}>
          {relaySwimmersName.split(',').map((name, i) => (
            <div key={i}>{name.trim()}</div>
          ))}
        </div>
      );
    }
  } else {
    subText = club;
  }

  // Иконка клуба показывается справа от стопки «имя / клуб» (только для личных заплывов).
  const showRightClubIcon = showClubIcon && Boolean(club) && !isRelay;

  const nameBlock = (
    <div className={nameBlockClassName}>
      <div className={`rowJustify flex items-center gap-2 ${rowJustify === 'end' ? 'justify-end' : 'justify-start'}`}>
        <div className={`${firstLineClassName} overflow-hidden`}>{displayName}</div>
        {isMe && (
          <span
            title="This is me"
            className="flex-shrink-0 whitespace-nowrap rounded-lg px-1.5 py-0.5 text-[9px] font-extrabold tracking-wide"
            style={{ color: '#b8860b', background: '#fdf3d6' }}
          >
            ★ ME
          </span>
        )}
      </div>
      {relayList}
      {subText && <div className={secondLineClassName}>{subText}</div>}
    </div>
  );

  return (
    <div
      className={`${className} ${onClick ? 'cursor-pointer' : ''}`}
      onClick={onClick ? (e) => { e.stopPropagation(); onClick(); } : undefined}
    >
      {/* Держатель рекорда этой дистанции. Бейдж общий на весь продукт (UI_RecordBadge). */}
      {isRecordHolder && (
        <div className="mb-1 flex">
          <UI_RecordBadge kind={recordKind} />
        </div>
      )}
      {showRightClubIcon ? (
        <div className={`rowJustify flex items-center gap-2 ${rowJustify === 'end' ? 'justify-end' : 'justify-start'}`}>
          {clubIconSide === 'left' && (
            <UI_ClubIcon clubName={club!} clubId={clubId} className="shrink-0" iconWidth={clubIconWidth} styleType="icon-notext" />
          )}
          {nameBlock}
          {clubIconSide === 'right' && (
            <UI_ClubIcon clubName={club!} clubId={clubId} className="shrink-0" iconWidth={clubIconWidth} styleType="icon-notext" />
          )}
        </div>
      ) : nameBlock}
    </div>
  );
};

export default UI_SwimmerNameCell;
