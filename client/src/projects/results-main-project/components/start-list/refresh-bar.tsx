import React from 'react';

/**
 * «Обновлено в HH:MM» + кнопка обновить — общая шапка всех экранов таба.
 *
 * Автообновления в проекте нет и не будет (решение 7 плана стартового протокола): посев
 * меняют до последнего дня, а механизма дожать изменение до уже открытой страницы нет.
 * Поэтому каждый экран обязан показать, НАСКОЛЬКО СВЕЖЕЕ то, что видно, и дать обновить
 * руками.
 */

export function updatedLabel(iso: string | null): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `Updated ${hh}:${mm}`;
}

export default function RefreshBar({ updatedAt, onRefresh }: {
  updatedAt: string | null;
  onRefresh: () => void;
}) {
  return (
    <div className="mb-3 flex items-center justify-between gap-2 text-[11px] font-semibold opacity-70">
      <span>{updatedLabel(updatedAt)}</span>
      <button
        type="button"
        onClick={onRefresh}
        className="rounded-full border px-2.5 py-1 text-[11px] font-bold"
        // Токен поверхности Deep, а не страницы: полоса стоит и на карточке плана, где
        // вокруг карточки темы Deep, и в зумах — обе живут внутри одного контейнера темы.
        style={{ borderColor: 'var(--deep-card-border)' }}
      >
        ↻ Refresh
      </button>
    </div>
  );
}
