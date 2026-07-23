import React, { useMemo, useState } from 'react';
import { ModerationRowDto } from '../use-my-media-moderation';
import UI_SwimmerGallery from '../../components/mix/swimmer-gallery/swimmer-gallery';
import { GalleryItem } from '../../../utils/interfaces/results';
import { chipClass, segmentClass } from './status-styles';

interface Props {
  rows: ModerationRowDto[];
  onDecide: (hubGroupId: number, publicationId: number, approve: boolean) => void | Promise<void>;
}

type ModStatusFilter = 'pending' | 'published' | 'all';

/** Таб «Moderation» — README §"Moderation tab (desktop)". Строки, не сетка — решения принимаются быстро. */
function ModerationPanel({ rows, onDecide }: Props) {
  const [groupId, setGroupId] = useState<number | 'all'>('all');
  const [statusFilter, setStatusFilter] = useState<ModStatusFilter>('pending');
  const [busyId, setBusyId] = useState<number | null>(null);
  const [openIndex, setOpenIndex] = useState<number | null>(null);

  const groupChips = useMemo(() => {
    const byId = new Map<number, string>();
    for (const r of rows) byId.set(r.hub_group_id, r.hub_group_name);
    return Array.from(byId.entries()).map(([id, name]) => ({
      id,
      name,
      pendingCount: rows.filter((r) => r.hub_group_id === id && r.status === 'pending').length,
    }));
  }, [rows]);

  const byGroup = groupId === 'all' ? rows : rows.filter((r) => r.hub_group_id === groupId);
  const visible = byGroup.filter((r) =>
    statusFilter === 'all' ? r.status !== 'rejected' : statusFilter === 'published' ? r.status === 'approved' : r.status === 'pending'
  );
  const totalPending = rows.filter((r) => r.status === 'pending').length;

  const embeddable = visible.filter((r) => r.media_type === 'video' && (r.source_type === 'youtube' || r.source_type === 'vimeo'));
  const lightboxItems: GalleryItem[] = embeddable.map((r) => ({ type: 'video', sourceType: r.source_type as GalleryItem['sourceType'], url: r.url }));

  const decide = async (row: ModerationRowDto, approve: boolean) => {
    setBusyId(row.id);
    try {
      await onDecide(row.hub_group_id, row.id, approve);
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div className="mt-[18px] flex flex-col gap-4">
      <div className="flex flex-wrap items-center gap-2">
        <button type="button" onClick={() => setGroupId('all')} className={chipClass(groupId === 'all')}>
          All my groups · {rows.filter((r) => r.status === 'pending').length}
        </button>
        {groupChips.map((g) => (
          <button key={g.id} type="button" onClick={() => setGroupId(g.id)} className={chipClass(groupId === g.id)}>
            <span dir="auto">{g.name}</span> · {g.pendingCount}
          </button>
        ))}
        <div className="ml-auto inline-flex overflow-hidden rounded-[10px] border border-[rgba(125,211,252,0.35)]">
          {(['pending', 'published', 'all'] as ModStatusFilter[]).map((k, i, arr) => (
            <button key={k} type="button" onClick={() => setStatusFilter(k)} className={segmentClass(statusFilter === k, i === arr.length - 1)}>
              {k === 'pending' ? `Pending · ${totalPending}` : k === 'published' ? 'Published' : 'All'}
            </button>
          ))}
        </div>
      </div>

      {visible.length === 0 ? (
        <div className="rounded-[16px] border border-dashed border-[rgba(56,239,143,0.35)] p-12 text-center">
          <p className="m-0 text-[15px] font-extrabold text-[#38ef8f]">No pending requests 🎉</p>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {visible.map((r) => {
            const highlightPublic = r.level === 'public' && r.status === 'pending';
            const embedIdx = embeddable.findIndex((e) => e.id === r.id && e.hub_group_id === r.hub_group_id);
            const isEmbeddable = r.media_type === 'video' && (r.source_type === 'youtube' || r.source_type === 'vimeo');
            return (
              <div
                key={`${r.hub_group_id}-${r.id}`}
                className="flex flex-wrap items-center gap-3.5 rounded-[14px] p-[10px_14px] shadow-[0_12px_32px_rgba(2,10,24,0.4)]"
                style={{
                  border: `1px solid ${highlightPublic ? 'rgba(255,202,122,0.45)' : 'rgba(125,211,252,0.22)'}`,
                  background: 'linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))',
                }}
              >
                <div
                  className="flex aspect-video w-[88px] shrink-0 cursor-pointer items-center justify-center rounded-[8px] bg-[linear-gradient(140deg,#12314f,#0a1c33)] text-[13px] text-[#7dd3fc]"
                  onClick={() => { if (isEmbeddable && embedIdx >= 0) setOpenIndex(embedIdx); }}
                >
                  {r.media_type === 'video' ? '▶' : '🖼'}
                </div>
                <div className="min-w-0 flex-[1.3]">
                  <div dir="auto" className="truncate text-[13.5px] font-extrabold text-[#f3f8fd]">{r.swimmer_name}</div>
                  {r.result_label && <div className="text-[11.5px] text-[rgba(203,224,240,0.55)]">{r.result_label}</div>}
                </div>
                <div className="min-w-0 flex-1 truncate text-[11.5px] text-[rgba(203,224,240,0.6)]">{r.owner_email}</div>
                <div dir="rtl" className="min-w-0 flex-[0.8] truncate text-left text-[12px] font-bold text-[rgba(226,240,252,0.85)]">{r.hub_group_name}</div>
                <span
                  className="hp-mono shrink-0 rounded-[6px] px-2 py-[2.5px] text-[10.5px] font-extrabold"
                  style={
                    r.level === 'public'
                      ? { color: '#ffca7a', border: '1.5px solid rgba(255,202,122,0.45)', background: 'rgba(255,202,122,0.08)' }
                      : { color: '#94a3b8', border: '1px solid rgba(148,163,184,0.4)', background: 'rgba(148,163,184,0.08)' }
                  }
                >
                  {r.level === 'public' ? 'Public 🌐' : 'Members'}
                </span>
                <span className="hp-mono w-14 shrink-0 text-[10.5px] text-[rgba(203,224,240,0.5)]">
                  {new Date(r.created_at).toLocaleDateString()}
                </span>
                <div className="flex shrink-0 gap-1.5">
                  {r.status === 'pending' && (
                    <>
                      <button
                        type="button"
                        disabled={busyId === r.id}
                        onClick={() => decide(r, true)}
                        className="hp-mono rounded-[8px] border-none bg-[#38ef8f] px-3 py-[6px] text-[11.5px] font-extrabold text-[#04101f] disabled:opacity-50"
                      >
                        Publish
                      </button>
                      <button
                        type="button"
                        disabled={busyId === r.id}
                        onClick={() => decide(r, false)}
                        className="hp-mono rounded-[8px] border border-[rgba(239,83,80,0.5)] bg-transparent px-3 py-[6px] text-[11.5px] font-extrabold text-[#ef5350] disabled:opacity-50"
                      >
                        Reject
                      </button>
                    </>
                  )}
                  {r.status === 'approved' && (
                    <button
                      type="button"
                      disabled={busyId === r.id}
                      onClick={() => decide(r, false)}
                      className="hp-mono rounded-[8px] border border-[rgba(125,211,252,0.4)] bg-transparent px-3 py-[6px] text-[11.5px] font-extrabold text-[#7dd3fc] disabled:opacity-50"
                    >
                      Unpublish
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      <p className="m-0 text-[11px] italic text-[rgba(203,224,240,0.4)]">
        Public = visible to everyone on the internet. Click a thumbnail to watch before deciding.
      </p>

      <UI_SwimmerGallery gallery={lightboxItems} openIndex={openIndex} onClose={() => setOpenIndex(null)} />
    </div>
  );
}

export default ModerationPanel;
