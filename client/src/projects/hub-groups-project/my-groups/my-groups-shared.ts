import { useMyHubGroupEdit } from '../use-my-hub-groups';

export const cardCls =
  'hp-card-std rounded-[18px] border border-[var(--t-border)] p-[18px] shadow-[var(--t-shadow)] backdrop-blur-[14px] lg:rounded-[24px] lg:p-[26px]';
export const inputCls =
  'w-full rounded-[10px] border border-[var(--t-accent-border)] bg-[var(--t-input-bg)] px-3 py-2 text-[13px] text-[var(--t-text)] outline-none placeholder:text-[var(--t-text-3)] focus:border-[var(--t-accent)]';
export const btnCls =
  'cursor-pointer rounded-[9px] border border-[var(--t-accent-border)] bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-accent)] transition-colors hover:bg-[var(--t-accent-soft)] disabled:cursor-not-allowed disabled:opacity-40';
export const btnDangerCls =
  'cursor-pointer rounded-[9px] border border-[var(--t-danger-border)] bg-transparent px-3 py-[7px] text-[12px] font-extrabold text-[var(--t-danger)] transition-colors hover:bg-[var(--t-danger-soft)] disabled:cursor-not-allowed disabled:opacity-40';

export type GroupEdit = ReturnType<typeof useMyHubGroupEdit>;
