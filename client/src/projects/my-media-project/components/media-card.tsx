import React, { useState } from 'react';
import { AllUserMediaDto } from '../use-all-my-media';
import { UserMediaPublicationDto } from '../../../hooks/useUserMedia';
import { HelperMedia } from '../../../utils/helpers';
import { hpCardCls, STATUS_COLORS, CardStatus } from './status-styles';

interface Props {
  item: AllUserMediaDto;
  publications: UserMediaPublicationDto[];
  onOpenLightbox: () => void;
  onDelete: () => void;
  onWithdraw: (hubGroupId: number) => void;
  onLinkToSwim: () => void;
  onShareWithGroup: () => void;
  /** Есть ли группы, куда можно подать (для disabled + tooltip) — считается лениво по клику. */
  shareDisabledHint?: string | null;
}

/** Media card — README §"Media card" (thumb 16:9 + body + publication chips + footer). */
function MediaCard({ item, publications, onOpenLightbox, onDelete, onWithdraw, onLinkToSwim, onShareWithGroup, shareDisabledHint }: Props) {
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);

  const isEmbeddable = item.media_type === 'video' && (item.source_type === 'youtube' || item.source_type === 'vimeo');
  const thumb = HelperMedia.resolveThumbUrl(item.media_type, item.source_type, item.url);
  const srcLabel = item.media_type === 'image' ? 'PHOTO' : item.source_type.toUpperCase();
  const playGlyph = item.media_type === 'image' ? '🖼' : '▶';

  const chips: { label: string; status: CardStatus }[] =
    publications.length === 0
      ? [{ label: 'private', status: 'private' }]
      : publications.map((p) => ({
          label: `${p.hub_group_name} · ${p.status === 'approved' ? 'published' : p.status}${p.status === 'approved' && p.level === 'public' ? ' 🌐' : ''}`,
          status: p.status === 'approved' ? 'published' : p.status === 'rejected' ? 'rejected' : 'pending',
        }));

  return (
    <div className={`flex flex-col overflow-hidden ${hpCardCls}`}>
      <div
        className="relative flex aspect-video cursor-pointer items-center justify-center bg-[linear-gradient(140deg,#12314f,#0a1c33)]"
        onClick={() => { if (isEmbeddable) onOpenLightbox(); else window.open(item.url, '_blank', 'noopener,noreferrer'); }}
      >
        {thumb ? (
          <img src={thumb} alt="" className="absolute inset-0 h-full w-full object-cover" />
        ) : null}
        <span className="relative z-10 flex h-11 w-11 items-center justify-center rounded-full border border-[rgba(125,211,252,0.4)] bg-[rgba(2,10,24,0.65)] text-[15px] text-[#7dd3fc]">
          {playGlyph}
        </span>
        <span className="hp-mono absolute left-[10px] top-[10px] rounded-[6px] bg-[rgba(2,10,24,0.7)] px-[7px] py-[2px] text-[9.5px] font-extrabold tracking-[0.08em] text-[rgba(203,224,240,0.7)]">
          {srcLabel}
        </span>
      </div>

      <div className="flex flex-1 flex-col gap-[9px] p-[12px_14px]">
        <div className="flex items-start gap-[9px]">
          <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-[#2c3d52] text-[9.5px] font-black text-[#bfe0f5]">
            {(item.swimmer_name || '?').trim().charAt(0).toUpperCase()}
          </span>
          <div className="min-w-0">
            {item.result_label ? (
              <>
                <div className="text-[13px] font-extrabold text-[#f3f8fd]">{item.result_label}</div>
                {item.competition_name && (
                  <div dir="rtl" className="truncate text-left text-[11.5px] text-[rgba(203,224,240,0.55)]">
                    {item.competition_name}
                  </div>
                )}
              </>
            ) : (
              <div className="flex flex-col gap-[3px]">
                <span className="block text-[12.5px] italic leading-[1.3] text-[rgba(203,224,240,0.5)]">Not linked to a swim</span>
                <button
                  type="button"
                  onClick={onLinkToSwim}
                  className="block cursor-pointer text-left text-[11.5px] font-extrabold leading-[1.3] text-[#7dd3fc]"
                >
                  Link to a swim →
                </button>
              </div>
            )}
          </div>
        </div>

        <div className="flex flex-wrap gap-[5px]">
          {chips.map((c, i) => {
            const colors = STATUS_COLORS[c.status];
            const pub = publications[i];
            return (
              <span
                key={i}
                className="hp-mono inline-flex items-center gap-1 whitespace-nowrap rounded-[6px] px-2 py-[2.5px] text-[10.5px] font-extrabold"
                style={{ color: colors.text, border: `1px solid ${colors.border}`, background: colors.bg }}
              >
                {c.label}
                {pub && (
                  <button
                    type="button"
                    title="Withdraw"
                    onClick={() => onWithdraw(pub.hub_group_id)}
                    className="leading-none opacity-60 hover:opacity-100"
                  >
                    ×
                  </button>
                )}
              </span>
            );
          })}
        </div>

        <div className="mt-auto flex items-center justify-between gap-2 border-t border-dashed border-[rgba(125,211,252,0.18)] pt-2">
          <button
            type="button"
            onClick={onShareWithGroup}
            disabled={!!shareDisabledHint}
            title={shareDisabledHint ?? undefined}
            className="cursor-pointer text-[11.5px] font-extrabold text-[#7dd3fc] disabled:cursor-not-allowed disabled:opacity-40"
          >
            Share with a group
          </button>
          <div className="relative">
            <button
              type="button"
              onClick={() => setMenuOpen((v) => !v)}
              className="cursor-pointer text-[14px] font-extrabold tracking-[2px] text-[rgba(203,224,240,0.5)]"
            >
              ⋯
            </button>
            {menuOpen && (
              <div className="absolute right-0 top-[24px] z-20 w-[140px] rounded-[10px] border border-[rgba(125,211,252,0.3)] bg-[#0e2138] p-1 shadow-[0_18px_44px_rgba(0,0,0,0.45)]">
                {!confirmDelete ? (
                  <button
                    type="button"
                    onClick={() => setConfirmDelete(true)}
                    className="hp-mono block w-full rounded-[7px] px-2.5 py-2 text-left text-[11.5px] font-extrabold text-[#ef5350] hover:bg-[rgba(239,83,80,0.1)]"
                  >
                    Delete
                  </button>
                ) : (
                  <div className="flex flex-col gap-1 p-1">
                    <span className="hp-mono text-[10.5px] font-extrabold text-[#cbe0f0]">Delete?</span>
                    <div className="flex gap-1">
                      <button
                        type="button"
                        onClick={() => { setMenuOpen(false); setConfirmDelete(false); onDelete(); }}
                        className="hp-mono flex-1 rounded-[6px] bg-[#ef5350] px-2 py-1 text-[10.5px] font-extrabold text-white"
                      >
                        Yes
                      </button>
                      <button
                        type="button"
                        onClick={() => setConfirmDelete(false)}
                        className="hp-mono flex-1 rounded-[6px] bg-[rgba(255,255,255,0.15)] px-2 py-1 text-[10.5px] font-extrabold text-white"
                      >
                        No
                      </button>
                    </div>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

export default MediaCard;
