import React from 'react';
import '../../components/mix/h2h/h2h.css';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import { routes } from '../../../utils/routes';

/**
 * Сторона сравнения стран в шапке `/records/compare` — та же геометрия, что у мини-карточки
 * пловца в H2H (`UI_H2HMiniCard`): классы `h2h-mini*` взяты как есть, чтобы шапка сравнения
 * стран стояла в той же сетке и ужималась теми же контейнерными запросами.
 *
 * Своя разметка, а не `UI_H2HMiniCard`, потому что стороны здесь РАЗНОЙ природы: у страны
 * нет портрета, возраста, клуба и избранного, а есть флаг и число рекордов. Прятать пол-
 * карточки пропами значило бы вшить в общий компонент ветку «а если это не человек».
 *
 * Карточка — ссылка на рекорды этой страны (`/records?tab=world&region=CODE`), как
 * мини-карточка пловца ведёт на его страницу.
 */
interface Props {
  code: string;
  /** Сколько рекордов у страны в текущем разрезе; null — подпись не рисуется. */
  records?: number | null;
  align: 'left' | 'right';
  /** Сменить сторону: ✕ во внутреннем углу, как у сменяемой карточки пловца. */
  onClear?: (() => void) | null;
  /** Эту сторону заполнит следующий выбор в пикере: рамка-предупреждение. */
  active?: boolean;
}

const RcNationCard: React.FC<Props> = ({ code, records = null, align, onClear = null, active = false }) => {
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
    <a
      className={`h2h-mini h2h-mini--${align} rc-nation${active ? ' h2h-mini--active' : ''}`}
      href={routes.records({ tab: 'world', region: code })}
      title={`All records of ${code}`}
    >
      {onClear && (
        <button
          type="button"
          className="h2h-mini__clear"
          title="Choose another country"
          onClick={(e) => { e.preventDefault(); e.stopPropagation(); onClear(); }}
        >
          ✕
        </button>
      )}
      {align === 'left' ? <>{text}{flag}</> : <>{flag}{text}</>}
    </a>
  );
};

export default RcNationCard;
