import { useState } from 'react';
import { fetchAntiforgeryToken, invalidateTokenCache } from '../../../utils/antiforgery';

/**
 * Кнопка «сбросить серверный кэш этого клуба» — карточка таба `Admin` страницы клуба
 * (`POST /api/admin/clubs/{id}/cache/invalidate`, только админ сайта).
 *
 * Правка через сайт и админку сбрасывает кэш сама (метки таблиц и строк). Кнопка — для
 * правки МИМО этого процесса API: руками в базе, командой `dotnet run -- --флаг`, другим
 * экземпляром сервера. Сбрасывает страницы только этого клуба (метка `page:club:{id}`);
 * «все клубы» — кнопка на /Admin/Cache.
 *
 * После сброса страница перезагружается: ответы клуба отдаются с `no-cache` + ETag, так что
 * перечитанная страница сразу соберётся из свежих данных.
 */
function ClubCacheCard({ clubId }: { clubId: number }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const refresh = async () => {
    setBusy(true);
    setError(null);
    const token = await fetchAntiforgeryToken();
    const res = await fetch(`/api/admin/clubs/${clubId}/cache/invalidate`, {
      method: 'POST',
      credentials: 'include',
      headers: token ? { 'X-XSRF-TOKEN': token } : {},
    }).catch(() => null);
    if (res?.ok) {
      window.location.reload();
      return;
    }
    if (res && (res.status === 400 || res.status === 403)) invalidateTokenCache();
    setBusy(false);
    setError('Could not refresh. Only a site admin can do this.');
  };

  return (
    <section className="deep-card mb-4" aria-label="Server cache">
      <div className="deep-card-title">Server cache</div>
      <div className="deep-card-sub mt-1">pages of this club</div>

      <p className="mt-3 text-[11.5px] font-bold" style={{ color: 'var(--deep-text-ghost)' }}>
        Edits made on the site refresh this page by themselves. Use this only after a change made
        directly in the database or by a command-line tool — it rebuilds this club&apos;s pages from
        fresh data.
      </p>

      {error && (
        <p className="mt-3 text-[12px] font-extrabold" style={{ color: 'var(--deep-danger)' }}>{error}</p>
      )}

      <button
        type="button"
        disabled={busy}
        onClick={refresh}
        className="deep-cta mt-4 px-4 py-2 text-[13px] disabled:opacity-40"
      >
        {busy ? 'Refreshing…' : 'Refresh this club'}
      </button>
    </section>
  );
}

export default ClubCacheCard;
