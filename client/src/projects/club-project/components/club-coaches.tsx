import React from 'react';

/**
 * Coaches — карточка рисуется ПУСТОЙ намеренно: тренеров в БД не существует вовсе
 * (решение Влада 2026-08-01, club-page-cards-sonnet.md §3.6). Не выдумывать источник данных,
 * ничего не запрашивать.
 */
function ClubCoaches() {
  return (
    <section className="deep-card mb-4">
      <div className="deep-card-title">Coaches</div>
      <div className="mt-3 text-[13px] font-bold" style={{ color: 'var(--deep-text-mute)' }}>
        Coach data is not collected yet
      </div>
    </section>
  );
}

export default ClubCoaches;
