import React from 'react';
import '../../components/mix/h2h/h2h.css';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';

/**
 * Сторона сравнения стран в шапке `/records/compare` — та же геометрия, что у мини-карточки
 * пловца в H2H (`UI_H2HMiniCard`): классы `h2h-mini*` взяты как есть, чтобы шапка сравнения
 * стран стояла в той же сетке и ужималась теми же контейнерными запросами.
 *
 * Своя разметка, а не `UI_H2HMiniCard`, потому что стороны здесь РАЗНОЙ природы: у страны
 * нет портрета, возраста, клуба и избранного, а есть флаг и число рекордов. Прятать пол-
 * карточки пропами значило бы вшить в общий компонент ветку «а если это не человек».
 *
 * ⚠ **Карточка — КНОПКА «выбрать другую страну», а не ссылка, и крестика у неё нет**
 * (решение Влада 23.09.2026). У пловца ✕ нужен, потому что сама карточка ведёт в профиль
 * и клик уже занят. Здесь занимать его нечем: экран про сравнение, и единственное, что
 * делают со стороной, — меняют её. Отдельный ✕ в углу поверх флага к тому же плохо
 * читался — символ без подложки терялся на пёстрой картинке.
 *
 * Цена решения: ссылки на рекорды страны (`/records?tab=world&region=CODE`) с карточки
 * больше нет. Понадобится — она отдельной строкой, а не кликом по карточке.
 */
interface Props {
  code: string;
  /** Сколько рекордов у страны в текущем разрезе; null — подпись не рисуется. */
  records?: number | null;
  align: 'left' | 'right';
  /** Выбрать другую страну на эту сторону: делает её активной и уводит фокус в поиск. */
  onSelect: () => void;
  /** Эту сторону заполнит следующий выбор в пикере: рамка-предупреждение. */
  active?: boolean;
}

const RcNationCard: React.FC<Props> = ({ code, records = null, align, onSelect, active = false }) => {
  const flag = (
    <UI_FlagEmoji countryCode={code} size="48x36" className="rc-nation__flag src-rc-nation-card" />
  );

  const text = (
    <span className="h2h-mini__text">
      <span className="h2h-mini__name rc-nation__code">{code}</span>
      {records != null && (
        <span className="h2h-mini__club">
          {records} {records === 1 ? 'record' : 'records'} in this cut
        </span>
      )}
    </span>
  );

  return (
    <button
      type="button"
      className={`h2h-mini rc-nation h2h-mini--${align}${active ? ' h2h-mini--active' : ''}`}
      // Подсказка обязана назвать действие: карточка выглядит как плашка со сводкой, и
      // без неё «кликабельно ли это» проверяют только тыком.
      title={`${code} — click to pick another country`}
      aria-label={`${code}, click to pick another country for this side`}
      onClick={onSelect}
    >
      {align === 'left' ? <>{text}{flag}</> : <>{flag}{text}</>}
    </button>
  );
};

export default RcNationCard;
