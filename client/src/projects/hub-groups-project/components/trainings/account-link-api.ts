import { request, type ApiResult } from '../lane-plans-api';

/**
 * Привязка аккаунта к пловцу и решение по заявке из таба Trainings (docs/plans/trainings-coach-flow-plan.md
 * §5.7, CF4) — те же ручки, что у My groups (`use-my-hub-groups.ts`): метка тренера и одобрение.
 * Тело метки — camelCase, как у всей `/api/me/hub-groups`.
 */

const base = (groupId: number, userId: number) => `/api/me/hub-groups/${groupId}/user-members/${userId}`;

export const accountLinkApi = {
  /** Метка «за какого пловца этот аккаунт»: `note` null — это сам пловец, «parent» — родитель. */
  setLabel: (groupId: number, userId: number, swimmerId: number | null, note: string | null): Promise<ApiResult<void>> =>
    request<void>('PUT', `${base(groupId, userId)}/label`, { swimmerId, note }),
  approve: (groupId: number, userId: number): Promise<ApiResult<void>> =>
    request<void>('POST', `${base(groupId, userId)}/approve`),
  /**
   * «Not a swimmer» (решение 13): тренер решил — аккаунт не пловец (родитель, наблюдатель); строка
   * «Needs you» больше не возвращается. Метка о пловце при этом снимается.
   */
  setNotSwimmer: (groupId: number, userId: number): Promise<ApiResult<void>> =>
    request<void>('PUT', `${base(groupId, userId)}/not-swimmer`),
  /** Отклонить заявку — участие (pending) снимается. */
  decline: (groupId: number, userId: number): Promise<ApiResult<void>> =>
    request<void>('DELETE', base(groupId, userId)),
};
