import React from 'react';

/**
 * Заглушка карточки, которой ещё нет (Media, Best season из хендоффа табов 3a).
 *
 * Нужна, чтобы таб не открывался пустым экраном: пустой таб читается как поломка,
 * а «скоро» — как обещание. Форма — обычная `.deep-card`, чтобы место в раскладке
 * было тем же, куда потом встанет настоящая карточка.
 */

interface Props {
  title: string;
  sub: string;
  text: string;
}

function ClubSoonCard({ title, sub, text }: Props) {
  return (
    <section className="deep-card mb-4">
      <div className="deep-card-title">{title}</div>
      <div className="deep-card-sub mt-1">{sub}</div>
      <div
        className="mt-4 flex min-h-[120px] items-center justify-center rounded-[12px] text-center text-[13px] font-bold"
        style={{
          border: `1px dashed var(--deep-card-border)`,
          background: 'var(--deep-card-bg-raised)',
          color: 'var(--deep-text-mute)',
        }}
      >
        {text}
      </div>
    </section>
  );
}

export default ClubSoonCard;
