import React from 'react';
import type { ClubTimelineItem } from '../../../hooks/useClubOverview';
import { routes } from '../../../utils/routes';

/**
 * Competition timeline — вертикальная линия зачётов клуба в выбранном скоупе
 * (CARDS.md §8, club-page-cards-sonnet.md §4.2). Бейдж ❄/☀/🌊 · группа рисуется ТОЛЬКО
 * если kind != null (OW-зачёты и старые записи без роли зачёта его не имеют).
 */

interface Props {
  timeline: ClubTimelineItem[];
}

const KIND_ICON: Record<string, string> = { winter: '❄', summer: '☀', openwater: '🌊' };

const KIND_STYLE: Record<string, { bg: string; border: string; fg: string }> = {
  winter: { bg: 'var(--deep-accent-chip)', border: 'var(--deep-accent-border)', fg: 'var(--deep-accent)' },
  summer: { bg: 'var(--deep-gold-chip)', border: 'var(--deep-gold-border)', fg: 'var(--deep-gold)' },
  openwater: { bg: 'var(--deep-ow-chip)', border: 'var(--deep-ow-border)', fg: 'var(--deep-ow)' },
};

function ClubTimeline({ timeline }: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="deep-card-title">Competition timeline</div>

      {timeline.length === 0 ? (
        <div className="mt-3 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
          No competitions yet
        </div>
      ) : (
        <div className="mt-4 flex flex-col">
          {timeline.map((item, i) => {
            const style = item.kind ? KIND_STYLE[item.kind] : null;
            return (
              <div key={item.competition_id} className="relative flex gap-3 pb-5 last:pb-0">
                {i < timeline.length - 1 && (
                  <span
                    className="absolute left-[15px] top-8 bottom-0 w-px"
                    style={{ background: 'var(--deep-divider)' }}
                  />
                )}

                {/* Единственный glow в карточке — на ранге #1 (README «правила цвета»). */}
                <span
                  className="z-10 flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-[12px] font-black"
                  style={{
                    background: item.rank === 1 ? 'var(--deep-gold-chip)' : 'var(--deep-card-bg-raised)',
                    border: `1px solid ${item.rank === 1 ? 'var(--deep-gold-border)' : 'var(--deep-card-border)'}`,
                    color: item.rank === 1 ? 'var(--deep-gold)' : 'var(--deep-text-mute)',
                    boxShadow:
                      item.rank === 1
                        ? '0 0 16px color-mix(in srgb, var(--deep-gold) 35%, transparent)'
                        : undefined,
                  }}
                >
                  #{item.rank}
                </span>

                <div className="min-w-0 flex-1 pt-1">
                  <div className="flex flex-wrap items-center gap-2">
                    {item.kind != null && (
                      <span
                        className="rounded-full border px-2 py-[2px] text-[10px] font-extrabold"
                        style={{
                          background: style?.bg ?? 'var(--deep-card-bg-raised)',
                          borderColor: style?.border ?? 'var(--deep-card-border)',
                          color: style?.fg ?? 'var(--deep-text-mute)',
                        }}
                      >
                        {KIND_ICON[item.kind] ?? ''} {item.group_name ?? ''}
                      </span>
                    )}
                    <a
                      href={routes.competition(item.competition_id)}
                      className="min-w-0 truncate text-[13px] font-extrabold no-underline"
                      style={{ color: 'var(--deep-text)' }}
                    >
                      {item.name}
                    </a>
                  </div>
                  <div className="mt-1 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                    {item.date}
                  </div>
                </div>

                <div className="shrink-0 text-right">
                  <div
                    className="text-[16px] leading-none"
                    style={{ fontFamily: 'var(--deep-font-display)', color: 'var(--deep-text)' }}
                  >
                    {item.points}
                  </div>
                  <div className="mt-1 text-[11px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
                    🥇{item.gold} 🥈{item.silver} 🥉{item.bronze}
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}

export default ClubTimeline;
