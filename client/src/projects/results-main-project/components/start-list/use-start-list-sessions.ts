import { useCallback, useMemo, useState } from 'react';
import { useStartListProgrammes } from './use-start-list';
import { buildSessions, sessionKey } from './plan-model';
import type { FilterZoneSession } from './filter-zone';
import type { StartListSource } from '../competition-header/types';

/**
 * Сессии таба Start list (протокол × день) и ОТКРЫТАЯ из них — одно место на оба режима.
 *
 * Вынесено из `start-list-tab.tsx` 22.10.2026 вместе с разбором зоны фильтров на части: таб
 * решает «какой экран», а этот хук — «какой протокол и день». Адрес — `?src=&day=`:
 * сессия переживает перезагрузку и пересылку ссылки.
 *
 * Дни берутся из программ протоколов (`buildSessions`), а не из привязки: у привязки одна
 * дата, а протокол бывает двухдневным (compID 16854). Без привязок (страница предстоящего
 * старта) протокол один — сам `orgCompId`.
 */
export function useStartListSessions(
  orgCompId: number,
  sources: StartListSource[],
  /** Мои строки плана по `sessionKey` — счётчики чипов в My plan (`planRowsBySession`). */
  planRows: ReadonlyMap<string, number>,
) {
  // Неизвестный/чужой src игнорируем: иначе подтаб «выбран», а данных под ним нет.
  const [activeSource, setActiveSource] = useState<number>(() => {
    const raw = Number(new URLSearchParams(window.location.search).get('src'));
    return sources.some((s) => s.org_comp_id === raw) ? raw : orgCompId;
  });
  // День проверять здесь нечем (дни приходят с программой): чужой день просто не найдёт
  // сессию, и откроется первый день протокола.
  const [activeDay, setActiveDay] = useState<string | null>(() => {
    const raw = new URLSearchParams(window.location.search).get('day');
    return raw && /^\d{4}-\d{2}-\d{2}$/.test(raw) ? raw : null;
  });

  // Все источники соревнования: поиск и карточка пловца работают по ним ЦЕЛИКОМ, а не по
  // активному подтабу — родителю всё равно, в каком окружном протоколе плывёт его ребёнок.
  const allOrgCompIds = useMemo(
    () => (sources.length > 0 ? sources.map((s) => s.org_comp_id) : [orgCompId]),
    [sources, orgCompId],
  );

  const { data: programmes } = useStartListProgrammes(allOrgCompIds);
  const sessions: FilterZoneSession[] = useMemo(() => buildSessions(
    sources.length > 0
      ? sources.map((s) => ({
        orgCompId: s.org_comp_id, index: s.index, date: s.date, dateIso: s.date_iso,
        sourceName: s.source_name, entries: s.entry_count,
      }))
      : [{ orgCompId, index: 1, date: null, dateIso: null, sourceName: null, entries: 0 }],
    programmes,
    planRows,
  ), [sources, orgCompId, programmes, planRows]);

  // Открытая сессия: точное совпадение протокол+день, иначе первый день протокола (день из
  // адреса чужой или дни ещё не загружены), иначе первая сессия.
  const activeSession = sessions.find((s) => s.key === sessionKey(activeSource, activeDay))
    ?? sessions.find((s) => s.orgCompId === activeSource)
    ?? sessions[0];

  /** Открыть сессию: состояние + `?src=&day=`. Зум не трогает — это решает таб. */
  const openSession = useCallback((s: FilterZoneSession) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(s.orgCompId));
    if (s.day) url.searchParams.set('day', s.day);
    else url.searchParams.delete('day');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(s.orgCompId);
    setActiveDay(s.day);
  }, []);

  /** Сменить только протокол (разворот заплыва из карточки пловца): день — первый у него. */
  const openSource = useCallback((srcOrgCompId: number) => {
    const url = new URL(window.location.href);
    url.searchParams.set('src', String(srcOrgCompId));
    url.searchParams.delete('day');
    window.history.replaceState(null, '', url.toString());
    setActiveSource(srcOrgCompId);
    setActiveDay(null);
  }, []);

  return {
    sessions,
    activeSession,
    /** compID открытой сессии — им ходят программа и зум заплыва. */
    activeOrgCompId: activeSession?.orgCompId ?? orgCompId,
    /** yyyy-MM-dd открытой сессии; null — день ещё не известен. */
    activeDay: activeSession?.day ?? null,
    allOrgCompIds,
    openSession,
    openSource,
  };
}
