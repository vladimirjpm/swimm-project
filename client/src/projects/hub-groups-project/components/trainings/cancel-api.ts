import { publicationsApiFetch } from '../group-bits';

/**
 * Отмена занятия и возврат (Р9): `PUT` / `DELETE /api/hub-groups/{id}/rsvp/{key}/cancel`, только
 * управляющему. Ключ занятия — `yyyy-MM-dd-HHmm` (`NextTraining.id`). Антифорджери и cookie —
 * через `publicationsApiFetch`, как у соседних записей. Ответ сервера (ответы занятия) здесь не
 * нужен: вызывающий перечитывает ответы и список ближайших сам. Возвращает текст отказа либо `null`.
 */

async function send(groupId: number, sessionKey: string, method: 'PUT' | 'DELETE', body: unknown, fallback: string) {
  try {
    const r = await publicationsApiFetch(
      `/api/hub-groups/${groupId}/rsvp/${encodeURIComponent(sessionKey)}/cancel`,
      { method, body: body === undefined ? undefined : JSON.stringify(body) },
    );
    if (r.ok) return null;
    const payload = await r.json().catch(() => null) as { error?: string } | null;
    return payload?.error ?? fallback;
  } catch {
    return fallback;
  }
}

export const cancelApi = {
  cancel: (groupId: number, sessionKey: string, reason: string | null) =>
    send(groupId, sessionKey, 'PUT', { reason }, 'Could not cancel the training.'),
  restore: (groupId: number, sessionKey: string) =>
    send(groupId, sessionKey, 'DELETE', undefined, 'Could not restore the training.'),
};
