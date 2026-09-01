/**
 * Токен антифоржери для мутирующих запросов к своему API.
 *
 * Один модуль на всё приложение: копий этой пары функций было уже две (`useFavorites`,
 * `useLogligStatus`), а с планом стартового протокола стало бы три — и кэш токена у каждой
 * свой, то есть протухший токен одна сбрасывает, а другие продолжают слать. Тот же довод,
 * по которому в проекте сведены `SwimRow` и `swimLabel`.
 *
 * Кэш модульный (не в React-состоянии): токен один на вкладку, и переживать перерисовки
 * он обязан.
 */

let cachedToken: string | null = null;

export async function fetchAntiforgeryToken(): Promise<string | null> {
  if (cachedToken) return cachedToken;
  try {
    const r = await fetch('/api/antiforgery/token', { credentials: 'include' });
    if (!r.ok) return null;
    const data = await r.json();
    cachedToken = data.token ?? null;
    return cachedToken;
  } catch {
    return null;
  }
}

/** Сбросить кэш: сервер ответил 400/403 — токен протух вместе с сессией. */
export function invalidateTokenCache(): void {
  cachedToken = null;
}
