import React from 'react';
import type { StartListSource } from '../competition-header/types';

/**
 * Подтабы источников. Нужны там, где наш один старт собран из нескольких протоколов
 * федерации: окружные чемпионаты 8-11 лежат под четырьмя compID, и без подтабов таб
 * показывал бы один округ из четырёх.
 *
 * Подпись — дата и номер («16/02 · #2»): имена протоколов у федерации на иврите, а видимый
 * UI у нас только английский. Полное имя — в тултипе.
 *
 * ⚠ Подтабы БОЛЬШЕ НЕ МЕНЯЮТ СМЫСЛ при открытой карточке пловца (§4 хендоффа 29.08.2026).
 * Раньше они подсвечивали «в какие дни плывёт открытый пловец» — то есть один и тот же ряд
 * кнопок отвечал то на «какой протокол смотрим», то на «когда плывёт мой». На этот второй
 * вопрос теперь отвечают дни-чипы внутри карточки плана (шаг Т6), и там он не путается
 * с выбором источника.
 */
export default function SourceTabs({ sources, activeOrgCompId, onSelect }: {
  sources: StartListSource[];
  activeOrgCompId: number;
  onSelect: (orgCompId: number) => void;
}) {
  return (
    <div className="mb-3 flex flex-wrap items-center gap-1.5" role="tablist" aria-label="Start list sources">
      {sources.map((s) => {
        const active = s.org_comp_id === activeOrgCompId;
        return (
          <button
            key={s.org_comp_id}
            type="button"
            role="tab"
            aria-selected={active}
            title={s.source_name ?? undefined}
            onClick={() => onSelect(s.org_comp_id)}
            className={`rounded-full border px-3 py-1 text-[11px] font-bold ${active ? 'opacity-100' : 'opacity-70 hover:opacity-100'}`}
            style={{
              borderColor: 'var(--theme-mode-border-input)',
              background: active ? 'var(--theme-mode-surface-2)' : 'transparent',
            }}
          >
            {s.date ? `${s.date} · ` : ''}#{s.index}
            {s.entry_count > 0 && <span className="ml-1.5 opacity-60">{s.entry_count}</span>}
          </button>
        );
      })}
    </div>
  );
}
