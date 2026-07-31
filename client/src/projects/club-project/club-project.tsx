import React, { useMemo } from 'react';
import '../../index.css';
import { parseRoute } from '../../utils/routes';

// Заглушка страницы клуба (этап K1 — только идентичность). Данные, карточки и API —
// следующие этапы (см. docs/plans/club-page-plan.md).

function ClubProject() {
  const clubId = useMemo<number | null>(() => parseRoute().clubId, []);

  return (
    <div className="flex min-h-screen items-center justify-center bg-gray-50 px-4">
      {clubId != null ? (
        <div className="text-center">
          <h1 className="text-2xl font-extrabold text-gray-900">Club #{clubId}</h1>
          <p className="mt-2 text-sm font-semibold text-gray-500">Club page — coming soon</p>
        </div>
      ) : (
        <div className="text-center">
          <h1 className="text-2xl font-extrabold text-gray-900">Club not found</h1>
        </div>
      )}
    </div>
  );
}

export default ClubProject;
