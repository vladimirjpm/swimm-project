/**
 * Единая логика «место → медаль»: используется и UI_PositionBadge (плоский кружок места),
 * и везде, где нужно решить, красить ли позицию 1/2/3 золотом/серебром/бронзой.
 * Медаль положена ТОЛЬКО если соревнование/заплыв award-eligible (is_award) —
 * иначе 1-е место — это просто место, без награды (см. [[athlete-alltime-card]]).
 */
export type MedalTier = 'gold' | 'silver' | 'bronze' | null;

export default class HelperMedal {
  static getMedalTier(position: string | number | null | undefined, isAward: boolean | undefined): MedalTier {
    if (!isAward) return null;
    const num = Number(position);
    if (num === 1) return 'gold';
    if (num === 2) return 'silver';
    if (num === 3) return 'bronze';
    return null;
  }
}
