import React from 'react';

/**
 * Карточка пустого состояния таба Trainings (хендофф `!design_handoff/design_handoff_trainings_tab/`,
 * Empty states 4a): пунктирная рамка, круг с глифом, заголовок, одна строка «откуда это появится».
 * У тренера под текстом — кнопки действия (`TrainingsEmptyButton`), у участника — только объяснение.
 *
 * ⚠ Зависит ТОЛЬКО от пропсов: замок гостя (`lockNotice` страницы группы) рисуется вне
 * `TrainingsTabProvider`, контекстом таба здесь пользоваться нельзя.
 */
function TrainingsEmpty({
  glyph, title, text, children,
}: {
  /** Глиф в круге — текстом (`🔒`, `◷`, `▶`); для скринридера скрыт. */
  glyph: string;
  title: string;
  text: React.ReactNode;
  /** Строки деталей и кнопки под текстом. */
  children?: React.ReactNode;
}) {
  return (
    <section
      aria-label={title}
      className="flex min-w-0 flex-col items-start"
      style={{
        gap: 10,
        padding: '24px 18px',
        borderRadius: 16,
        border: '1px dashed var(--deep-card-border)',
      }}
    >
      <span
        aria-hidden="true"
        className="flex shrink-0 items-center justify-center"
        style={{ width: 40, height: 40, borderRadius: '50%', background: 'var(--deep-seg-track)', fontSize: 17 }}
      >
        {glyph}
      </span>
      <div className="font-extrabold" style={{ fontSize: 15, color: 'var(--deep-text)' }}>{title}</div>
      <div className="font-semibold" style={{ fontSize: 12.5, lineHeight: 1.5, color: 'var(--t-text-3)' }}>{text}</div>
      {children}
    </section>
  );
}

/** Кнопка действия в карточке пустого состояния: `primary` — фиолетовая (тренерская), иначе контурная. */
export function TrainingsEmptyButton({
  primary = false, onClick, children,
}: {
  primary?: boolean;
  onClick: () => void;
  children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="cursor-pointer border px-4 font-extrabold"
      style={{
        height: 44,
        borderRadius: 12,
        fontSize: 13,
        borderColor: primary ? 'var(--deep-ow-border)' : 'var(--deep-card-border)',
        background: primary ? 'var(--deep-ow-chip)' : 'transparent',
        color: primary ? 'var(--deep-ow)' : 'var(--t-text-3)',
      }}
    >
      {children}
    </button>
  );
}

/** Ряд кнопок карточки: перенос строк, gap 8, отступ сверху 4. */
export function TrainingsEmptyActions({ children }: { children: React.ReactNode }) {
  return <div className="flex flex-wrap" style={{ gap: 8, marginTop: 4 }}>{children}</div>;
}

export default TrainingsEmpty;
