import React from 'react';

/**
 * Переключатель «✎ Edit | 👁 View» на месте чипа «✎ You can edit» (решение Влада 15.11.2026):
 * тот, кто правит страницу, одним нажатием видит её так, как обычный участник. Вид — тот же чип
 * (`--deep-ow*`, пилюля, прописные 10px), только из двух половинок; активная залита `--deep-ow`.
 * Что значит «как участник», решает страница (`editMode` у `DeepEntityPage`).
 */
function DeepEditModeToggle({ viewing, onChange }: {
  viewing: boolean;
  onChange: (viewing: boolean) => void;
}) {
  const options: { id: 'edit' | 'view'; label: string; title: string }[] = [
    { id: 'edit', label: '✎ Edit', title: 'Edit mode — your coach tools' },
    { id: 'view', label: '👁 View', title: 'See this page the way a member sees it' },
  ];
  const current = viewing ? 'view' : 'edit';
  return (
    <div
      role="group"
      aria-label="Page mode"
      className="inline-flex h-[26px] items-center gap-0.5 rounded-full border p-[2px]"
      style={{ borderColor: 'var(--deep-ow-border)', background: 'var(--deep-ow-chip)' }}
    >
      {options.map(({ id, label, title }) => {
        const active = id === current;
        return (
          <button
            key={id}
            type="button"
            aria-pressed={active}
            title={title}
            onClick={() => onChange(id === 'view')}
            className="inline-flex h-full cursor-pointer items-center rounded-full border-0 px-2 text-[10px] font-extrabold uppercase tracking-[.04em]"
            style={active
              ? { background: 'var(--deep-ow)', color: 'var(--deep-accent-ink)' }
              : { background: 'transparent', color: 'var(--deep-ow)' }}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
}

export default DeepEditModeToggle;
