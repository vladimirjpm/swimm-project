// Общие цвета/классы страницы «My media» — тёмный стиль groups.html (README §Design Tokens).
// Тема намеренно не через var(--theme-mode-*) — осознанное решение (страница в семействе groups/home).

export type PublicationStatus = 'pending' | 'approved' | 'rejected';
export type CardStatus = 'private' | 'pending' | 'published' | 'rejected';

/** {text, border, bg} для статус-чипов — цвет+слово, никогда только цвет. */
export const STATUS_COLORS: Record<CardStatus, { text: string; border: string; bg: string }> = {
  pending: { text: '#ffca7a', border: 'rgba(255,202,122,0.45)', bg: 'rgba(255,202,122,0.08)' },
  published: { text: '#38ef8f', border: 'rgba(56,239,143,0.45)', bg: 'rgba(56,239,143,0.08)' },
  rejected: { text: '#ef5350', border: 'rgba(239,83,80,0.45)', bg: 'rgba(239,83,80,0.08)' },
  private: { text: '#94a3b8', border: 'rgba(148,163,184,0.4)', bg: 'rgba(148,163,184,0.08)' },
};

export function derivedCardStatus(pubs: { status: PublicationStatus }[]): CardStatus {
  if (pubs.length === 0) return 'private';
  if (pubs.some((p) => p.status === 'pending')) return 'pending';
  if (pubs.some((p) => p.status === 'approved')) return 'published';
  return 'rejected';
}

export const hpCardCls =
  'rounded-[16px] border border-[#7dd3fc]/[0.22] bg-[linear-gradient(180deg,rgba(56,189,248,0.08),rgba(8,25,48,0.78))] shadow-[0_24px_60px_rgba(2,10,24,0.5)]';

export function chipClass(active: boolean): string {
  return (
    'hp-mono inline-flex items-center gap-[7px] whitespace-nowrap rounded-[8px] border px-[13px] py-[7px] text-[12px] font-extrabold ' +
    (active
      ? 'border-[#7dd3fc] bg-[#7dd3fc] text-[#04101f]'
      : 'border-[rgba(125,211,252,0.4)] bg-transparent text-[#7dd3fc]')
  );
}

export function segmentClass(active: boolean, isLast: boolean): string {
  return (
    'hp-mono whitespace-nowrap px-3 py-[7px] text-[11.5px] font-extrabold ' +
    (isLast ? '' : 'border-r border-[rgba(125,211,252,0.25)] ') +
    (active ? 'bg-[#7dd3fc] text-[#04101f]' : 'bg-transparent text-[rgba(125,211,252,0.75)]')
  );
}
