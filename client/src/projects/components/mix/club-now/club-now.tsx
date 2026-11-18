import React, { createContext, useContext } from 'react';
import { clubNowLabel, type ClubNowMap } from './club-now-model';

/**
 * Подсказка «now: <текущее название>» у клуба, напечатанного в протоколе под прежним
 * названием (Р97, `club-now-model.ts`). Карту «id → текущее» кладёт экран соревнования
 * провайдером, и ячейки берут её сами — без протаскивания пропсов через каждую карточку.
 */
const ClubNowContext = createContext<ClubNowMap>(new Map());

export function ClubNowProvider({ map, children }: { map: ClubNowMap; children: React.ReactNode }) {
  return <ClubNowContext.Provider value={map}>{children}</ClubNowContext.Provider>;
}

/** Карта «id → текущее» от ближайшего провайдера (пустая вне экрана соревнования). */
export function useClubNowMap(): ClubNowMap {
  return useContext(ClubNowContext);
}

/** Текущее название для подсказки или null (см. `clubNowLabel`). */
export function useClubNow(printed?: string | null, clubId?: number | null, explicit?: string | null): string | null {
  const map = useContext(ClubNowContext);
  return clubNowLabel(printed, explicit, clubId, map);
}

interface Props {
  /** Название как напечатано — то, что уже показано рядом. */
  printed?: string | null;
  clubId?: number | null;
  /** Текущее название из самой строки (`club_now`) — важнее карты провайдера. */
  now?: string | null;
  className?: string;
}

/**
 * «now: אלומה עירוני הוד השרון» мелким текстом. Ничего не рисует, если клуб не переименован.
 * Название в `<bdi>`: иврит внутри английской подписи не переворачивает строку.
 */
const UI_ClubNowHint: React.FC<Props> = ({ printed, clubId, now, className = '' }) => {
  const label = useClubNow(printed, clubId, now);
  if (!label) return null;
  return (
    <span
      dir="ltr"
      // Переносится: в узкой колонке клуба протокола одной строкой подсказка вылезала бы из ячейки.
      className={`ui-club-now text-[0.85em] font-normal opacity-70 ${className}`}
      title={`Printed in the protocol as it was then; the club is now called ${label}`}
    >
      now: <bdi>{label}</bdi>
    </span>
  );
};

export default UI_ClubNowHint;
