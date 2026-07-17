import React, { useMemo, useState, useEffect } from 'react';
import { useAuth } from '../../hooks/useAuth';
import { useLoginModal } from '../components/login-modal/login-modal-context';
import { useMyMediaPublications } from '../../hooks/useUserMedia';
import { useAllMyMedia, AllUserMediaDto } from './use-all-my-media';
import { HelperMedia } from '../../utils/helpers';
import UI_SwimmerGallery from '../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../utils/interfaces/results';

// Страница «My media» — сводное управление всем медиа пользователя по всем его
// пловцам (все ссылки размазаны по карточкам пловцов, Влад просил единый экран).
// Светлая тема (var(--theme-mode-*)), как у results_main — НЕ тёмный стиль groups.

const pubStatusLabel: Record<string, string> = {
  pending: 'pending review',
  approved: 'published',
  rejected: 'rejected',
};

function MyMedia() {
  const auth = useAuth();
  const { openLoginModal } = useLoginModal();

  if (auth.loading) {
    return (
      <div className="min-h-screen" style={{ background: 'var(--theme-mode-page-bg)' }} />
    );
  }

  if (!auth.isAuthenticated) {
    return (
      <div className="min-h-screen flex items-center justify-center px-4" style={{ background: 'var(--theme-mode-page-bg)' }}>
        <div
          className="max-w-md w-full rounded-2xl p-6 text-center shadow-sm"
          style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border)' }}
        >
          <h1 className="text-lg font-extrabold mb-2" style={{ color: 'var(--theme-mode-text)' }}>My media</h1>
          <p className="text-sm mb-4" style={{ color: 'var(--theme-mode-text-secondary)' }}>
            Sign in to manage your media
          </p>
          <button
            type="button"
            onClick={openLoginModal}
            className="rounded-[11px] px-4 py-2 text-sm font-black text-white"
            style={{ background: 'var(--theme-primary)' }}
          >
            Sign in
          </button>
        </div>
      </div>
    );
  }

  return <MyMediaContent />;
}

function MyMediaContent() {
  const { media, loading, remove } = useAllMyMedia();
  const { publications, submit: submitPublication, withdraw: withdrawPublication } = useMyMediaPublications();

  const [myGroups, setMyGroups] = useState<{ id: number; name: string }[]>([]);
  useEffect(() => {
    fetch('/api/me/hub-groups/joined', { credentials: 'include' })
      .then((r) => (r.ok ? r.json() : []))
      .then((list: { id: number; name: string; status: string }[]) =>
        setMyGroups(list.filter((g) => g.status === 'active').map((g) => ({ id: g.id, name: g.name }))))
      .catch(() => setMyGroups([]));
  }, []);

  const [confirmDeleteId, setConfirmDeleteId] = useState<number | null>(null);
  const [publishMediaId, setPublishMediaId] = useState<number | null>(null);
  const [pubGroupId, setPubGroupId] = useState<number | ''>('');
  const [pubLevel, setPubLevel] = useState<'members' | 'public'>('members');
  const [pubSubmitting, setPubSubmitting] = useState(false);
  const [pubError, setPubError] = useState<string | null>(null);
  const [lightboxIndex, setLightboxIndex] = useState<number | null>(null);
  const [lightboxGroup, setLightboxGroup] = useState<AllUserMediaDto[]>([]);

  // Группировка по пловцу — порядок как первое появление в ответе сервера.
  const groups = useMemo(() => {
    const bySwimmer = new Map<number, { name: string; items: AllUserMediaDto[] }>();
    for (const item of media) {
      const g = bySwimmer.get(item.swimmer_id);
      if (g) g.items.push(item);
      else bySwimmer.set(item.swimmer_id, { name: item.swimmer_name, items: [item] });
    }
    return Array.from(bySwimmer.values());
  }, [media]);

  const openLightboxFor = (groupItems: AllUserMediaDto[], item: AllUserMediaDto) => {
    const embeddable = groupItems.filter((m) => m.source_type === 'youtube' || m.source_type === 'vimeo');
    const idx = embeddable.findIndex((m) => m.id === item.id);
    if (idx >= 0) {
      setLightboxGroup(embeddable);
      setLightboxIndex(idx);
    }
  };

  const lightboxItems = useMemo<GalleryItem[]>(
    () => lightboxGroup.map((m) => ({ type: 'video', sourceType: m.source_type, url: m.url })),
    [lightboxGroup]
  );

  const handleDelete = async (id: number) => {
    await remove(id);
    setConfirmDeleteId(null);
  };

  const handlePublish = async () => {
    if (publishMediaId == null || pubGroupId === '') return;
    setPubError(null);
    setPubSubmitting(true);
    const res = await submitPublication(publishMediaId, pubGroupId, pubLevel);
    setPubSubmitting(false);
    if (res.ok) {
      setPublishMediaId(null);
      setPubGroupId('');
    } else {
      setPubError(res.error ?? 'Could not submit the request');
    }
  };

  return (
    <div className="min-h-screen px-4 py-6 sm:px-8" style={{ background: 'var(--theme-mode-page-bg)' }}>
      <div className="max-w-5xl mx-auto">
        <h1 className="text-xl font-extrabold mb-1" style={{ color: 'var(--theme-mode-text)' }}>My media</h1>
        <p className="text-sm mb-6" style={{ color: 'var(--theme-mode-text-secondary)' }}>
          All the links you added across your athletes, in one place.
        </p>

        {loading ? (
          <div className="text-sm italic" style={{ color: 'var(--theme-mode-text-muted)' }}>Loading…</div>
        ) : groups.length === 0 ? (
          <div
            className="rounded-2xl p-6 text-sm italic"
            style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border)', color: 'var(--theme-mode-text-muted)' }}
          >
            No media yet. Add links from an athlete's card on the results page.
          </div>
        ) : (
          <div className="flex flex-col gap-8">
            {groups.map((group) => (
              <section key={group.name}>
                <h2 className="text-base font-extrabold mb-3" style={{ color: 'var(--theme-primary)' }}>{group.name}</h2>
                <ul className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-3">
                  {group.items.map((item) => {
                    const thumb = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);
                    const isEmbeddable = item.source_type === 'youtube' || item.source_type === 'vimeo';
                    const itemPublications = publications.filter((p) => p.user_media_id === item.id);

                    return (
                      <li
                        key={item.id}
                        className="relative rounded-xl overflow-hidden shadow-sm"
                        style={{ background: 'var(--theme-mode-surface)', border: '1px solid var(--theme-mode-border)' }}
                      >
                        <button
                          type="button"
                          onClick={() => setConfirmDeleteId(item.id)}
                          title="Delete"
                          className="absolute top-1 right-1 z-10 w-5 h-5 rounded-full inline-flex items-center justify-center text-xs font-bold text-white"
                          style={{ background: 'rgba(0,0,0,0.55)' }}
                        >
                          ×
                        </button>

                        {myGroups.length > 0 && (
                          <button
                            type="button"
                            onClick={() => {
                              setPublishMediaId(publishMediaId === item.id ? null : item.id);
                              setPubError(null);
                            }}
                            title="Share with a group"
                            className="absolute top-1 left-1 z-10 w-5 h-5 rounded-full inline-flex items-center justify-center text-[10px] font-bold text-white"
                            style={{ background: publishMediaId === item.id ? 'var(--theme-primary)' : 'rgba(0,0,0,0.55)' }}
                          >
                            ↗
                          </button>
                        )}

                        {isEmbeddable ? (
                          <div
                            className="cursor-pointer aspect-video flex items-center justify-center"
                            onClick={() => openLightboxFor(group.items, item)}
                          >
                            {thumb ? (
                              <img src={thumb} alt="" className="w-full h-full object-cover" />
                            ) : (
                              <span className="text-xs" style={{ color: 'var(--theme-mode-text-muted)' }}>video</span>
                            )}
                          </div>
                        ) : (
                          <a
                            href={item.url}
                            target="_blank"
                            rel="noopener noreferrer nofollow"
                            className="aspect-video flex items-center justify-center"
                          >
                            {thumb ? (
                              <img src={thumb} alt="" className="w-full h-full object-cover" />
                            ) : (
                              <span className="text-xs underline" style={{ color: 'var(--theme-mode-text-muted)' }}>link</span>
                            )}
                          </a>
                        )}

                        <div className="p-2 flex flex-col gap-1.5">
                          {(item.result_label || item.level) && (
                            <span
                              className="self-start rounded px-1.5 py-0.5 text-[10px] font-bold"
                              style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text-secondary)' }}
                            >
                              {item.result_label || item.level}
                            </span>
                          )}

                          {itemPublications.length > 0 && (
                            <div className="flex flex-wrap gap-1">
                              {itemPublications.map((p) => (
                                <span
                                  key={p.id}
                                  className="inline-flex items-center gap-1 rounded-full px-1.5 py-0.5 text-[9px] font-bold"
                                  style={{
                                    background: 'var(--theme-mode-input-bg)',
                                    color: p.status === 'approved' ? 'var(--theme-primary)'
                                      : p.status === 'rejected' ? '#e23b5a' : 'var(--theme-mode-text-secondary)',
                                    border: '1px solid var(--theme-mode-border)',
                                  }}
                                >
                                  {p.hub_group_name} · {pubStatusLabel[p.status] ?? p.status}{p.status === 'approved' && p.level === 'public' ? ' (everyone)' : ''}
                                  <button
                                    type="button"
                                    title="Withdraw"
                                    onClick={() => withdrawPublication(p.user_media_id, p.hub_group_id)}
                                    className="leading-none opacity-60 hover:opacity-100"
                                  >
                                    ×
                                  </button>
                                </span>
                              ))}
                            </div>
                          )}
                        </div>

                        {confirmDeleteId === item.id && (
                          <div className="absolute inset-0 z-20 flex flex-col items-center justify-center gap-1 text-center p-1" style={{ background: 'rgba(0,0,0,0.75)' }}>
                            <span className="text-[10px] text-white">Delete?</span>
                            <div className="flex gap-1">
                              <button
                                type="button"
                                onClick={() => handleDelete(item.id)}
                                className="text-[10px] font-bold px-2 py-0.5 rounded"
                                style={{ background: '#e23b5a', color: '#fff' }}
                              >
                                Yes
                              </button>
                              <button
                                type="button"
                                onClick={() => setConfirmDeleteId(null)}
                                className="text-[10px] font-bold px-2 py-0.5 rounded"
                                style={{ background: 'rgba(255,255,255,0.2)', color: '#fff' }}
                              >
                                No
                              </button>
                            </div>
                          </div>
                        )}

                        {publishMediaId === item.id && (
                          <div className="p-2 pt-0">
                            <div
                              className="flex flex-col gap-2 rounded-lg p-2"
                              style={{ background: 'var(--theme-mode-surface-alt)' }}
                            >
                              <div className="flex flex-wrap gap-1.5">
                                <select
                                  value={pubGroupId}
                                  onChange={(e) => setPubGroupId(e.target.value === '' ? '' : Number(e.target.value))}
                                  className="rounded-lg px-2 py-1 text-[11px]"
                                  style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text)', border: '1px solid var(--theme-mode-border)' }}
                                >
                                  <option value="">— group —</option>
                                  {myGroups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
                                </select>
                                <select
                                  value={pubLevel}
                                  onChange={(e) => setPubLevel(e.target.value as 'members' | 'public')}
                                  className="rounded-lg px-2 py-1 text-[11px]"
                                  style={{ background: 'var(--theme-mode-input-bg)', color: 'var(--theme-mode-text)', border: '1px solid var(--theme-mode-border)' }}
                                >
                                  <option value="members">group members</option>
                                  <option value="public">public (visible to everyone)</option>
                                </select>
                                <button
                                  type="button"
                                  onClick={handlePublish}
                                  disabled={pubSubmitting || pubGroupId === ''}
                                  className="rounded-lg px-2 py-1 text-[11px] font-bold text-white disabled:opacity-50"
                                  style={{ background: 'var(--theme-primary)' }}
                                >
                                  Submit
                                </button>
                              </div>
                              {pubError && <div className="text-[10px]" style={{ color: '#e23b5a' }}>{pubError}</div>}
                            </div>
                          </div>
                        )}
                      </li>
                    );
                  })}
                </ul>
              </section>
            ))}
          </div>
        )}
      </div>

      <UI_SwimmerGallery
        gallery={lightboxItems}
        openIndex={lightboxIndex}
        onClose={() => setLightboxIndex(null)}
      />
    </div>
  );
}

export default MyMedia;
