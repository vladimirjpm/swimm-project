import React from 'react';
import UI_FlagEmoji from '../../components/mix/flag-icon/flag-icon';
import UI_H2HSideCard from '../../components/mix/h2h/h2h-side-card';
import { routes } from '../../../utils/routes';

/**
 * Сторона сравнения стран в шапке `/records/compare` — общий `UI_H2HSideCard` с флагом
 * вместо портрета. Фигура, зеркальность, активная рамка и строка-ссылка под карточкой
 * приходят оттуда же, что у пловца в H2H: два похожих куска вёрстки расходились бы.
 *
 * Здесь остаётся только то, чем страна отличается от человека: флаг, код вместо имени,
 * покрытие справочника вместо клуба, ссылка на её рекорды вместо профиля. Сердечка и
 * возрастного чипа у страны нет — соответствующие слоты просто не заполняются.
 */
interface Props {
  code: string;
  /** Сколько рекордов у страны в текущем разрезе; null — подпись не рисуется. */
  records?: number | null;
  /**
   * Год САМОГО СВЕЖЕГО рекорда стороны в этом разрезе. Не украшение: справочник
   * World Aquatics для некоторых стран фактически заморожен (у России он стоит на 2021-м —
   * домашние чемпионаты туда не попадают), и без этой цифры сравнение молча выдаёт
   * устаревший набор за действующий. Вопрос Влада 23.09.2026, случай 50 вольным: у нас
   * Морозов 21.27 (2019), в жизни Корнев 21.06 (2026).
   */
  latestYear?: number | null;
  /** Сколько ДЕЙСТВУЮЩИХ мировых рекордов держат пловцы страны (все дисциплины, open). */
  worldRecords?: number | null;
  align: 'left' | 'right';
  /** Выбрать другую страну на эту сторону: делает её активной и уводит фокус в поиск. */
  onSelect: () => void;
  /** Эту сторону заполнит следующий выбор в пикере: рамка-предупреждение. */
  active?: boolean;
}

const RcNationCard: React.FC<Props> = ({
  code, records = null, latestYear = null, worldRecords = null, align, onSelect, active = false,
}) => (
  <UI_H2HSideCard
    align={align}
    className="rc-nation"
    media={(
      <UI_FlagEmoji countryCode={code} size="48x36" className="rc-nation__flag src-rc-nation-card" />
    )}
    name={<span className="rc-nation__code">{code}</span>}
    sub={records != null ? `${records} ${records === 1 ? 'record' : 'records'} in this cut` : null}
    // Чип несёт ДВЕ цифры о наборе целиком: насколько он свежий и сколько мировых рекордов
    // за страной. Обе про сторону, а не про дисциплину, поэтому стоят в шапке, а не в
    // карточках заплывов.
    chip={(latestYear != null || (worldRecords ?? 0) > 0) ? (
      <span
        title={[
          latestYear != null
            ? `Newest record in this cut: ${latestYear}. The reference is World Aquatics; marks a federation sets at home appear only after World Aquatics lists them.`
            : null,
          (worldRecords ?? 0) > 0
            ? `${worldRecords} standing world record${worldRecords === 1 ? '' : 's'} held by ${code} swimmers`
            : null,
        ].filter(Boolean).join(' · ')}
      >
        {latestYear != null && `latest ${latestYear}`}
        {latestYear != null && (worldRecords ?? 0) > 0 && ' · '}
        {(worldRecords ?? 0) > 0 && `${worldRecords} WR`}
      </span>
    ) : null}
    onSelect={onSelect}
    selectHint={`${code} — click to pick another country`}
    active={active}
    link={{ href: routes.records({ tab: 'world', region: code }), label: 'all records →' }}
  />
);

export default RcNationCard;
