import { useCallback, useEffect, useState } from 'react';

/**
 * Режим страницы сущности у того, кто может её править: `Edit` (по умолчанию) или `View` — страница
 * так, как её видит обычный участник (переключатель `DeepEditModeToggle` на месте чипа «✎ You can
 * edit»). Запоминается в браузере отдельно для каждой сущности (`key`, например `group:17`).
 *
 * Хранилище — удобство одного зрителя: недоступно (приватное окно, запрет) — режим просто живёт до
 * перезагрузки. `key` null — сущность ещё не пришла: всегда Edit.
 */
const PREFIX = 'deep-view-mode:';

function read(key: string | null): boolean {
  if (!key) return false;
  try {
    return window.localStorage.getItem(PREFIX + key) === 'view';
  } catch {
    return false;
  }
}

export function useEditViewMode(key: string | null): [boolean, (viewing: boolean) => void] {
  const [viewing, setViewingState] = useState(() => read(key));
  // Сущность пришла позже первого рендера (или сменилась) — перечитать её режим.
  useEffect(() => { setViewingState(read(key)); }, [key]);

  const setViewing = useCallback((next: boolean) => {
    setViewingState(next);
    if (!key) return;
    try {
      if (next) window.localStorage.setItem(PREFIX + key, 'view');
      else window.localStorage.removeItem(PREFIX + key);
    } catch {
      // Хранилище недоступно — режим держится до перезагрузки.
    }
  }, [key]);

  return [viewing, setViewing];
}
