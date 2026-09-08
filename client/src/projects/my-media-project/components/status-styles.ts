// Общие цвета/классы страницы «My media». Своих цветов здесь больше НЕТ: всё через роли
// `--t-*`, которые страница отображает на палитру deep (`my-media.css`, Ф5). Хекс в этом
// файле — снова разошедшаяся тема: добавляй роль, а не значение.

export type PublicationStatus = 'pending' | 'approved' | 'rejected';
export type CardStatus = 'private' | 'pending' | 'published' | 'rejected';

/** {text, border, bg} для статус-чипов — цвет+слово, никогда только цвет. */
export const STATUS_COLORS: Record<CardStatus, { text: string; border: string; bg: string }> = {
  pending: { text: 'var(--t-warn)', border: 'var(--t-warn-border)', bg: 'var(--t-warn-soft)' },
  published: { text: 'var(--t-accent)', border: 'var(--t-accent-border)', bg: 'var(--t-accent-soft)' },
  rejected: { text: 'var(--t-danger)', border: 'var(--t-danger-border)', bg: 'var(--t-danger-soft)' },
  private: { text: 'var(--t-text-2)', border: 'var(--t-border)', bg: 'var(--t-surface2)' },
};

export function derivedCardStatus(pubs: { status: PublicationStatus }[]): CardStatus {
  if (pubs.length === 0) return 'private';
  if (pubs.some((p) => p.status === 'pending')) return 'pending';
  if (pubs.some((p) => p.status === 'approved')) return 'published';
  return 'rejected';
}

/** Кто это видит: private (никто), members (группа), everyone (все); pending/rejected — промежуточные. */
export function visibilityLabel(status: CardStatus, isPublic: boolean): string {
  if (status === 'published') return isPublic ? 'everyone' : 'members';
  return status;
}

export const hpCardCls =
  'rounded-[16px] border border-[var(--t-border)] bg-[var(--t-card)] shadow-[var(--t-shadow)]';

export function chipClass(active: boolean): string {
  return (
    'hp-mono inline-flex items-center gap-[7px] whitespace-nowrap rounded-[8px] border px-[13px] py-[7px] text-[12px] font-extrabold ' +
    (active
      ? 'border-[var(--t-accent)] bg-[var(--t-accent)] text-[var(--t-accent-ink)]'
      : 'border-[var(--t-accent-border)] bg-transparent text-[var(--t-accent)]')
  );
}

export function segmentClass(active: boolean, isLast: boolean): string {
  return (
    'hp-mono whitespace-nowrap px-3 py-[7px] text-[11.5px] font-extrabold ' +
    (isLast ? '' : 'border-r border-[var(--t-border)] ') +
    (active ? 'bg-[var(--t-accent)] text-[var(--t-accent-ink)]' : 'bg-transparent text-[var(--t-accent-dim)]')
  );
}
