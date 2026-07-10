import { useEffect, useState } from 'react';
import ResultsLoadModeHelper, { ResultsLoadMode } from '../utils/helpers/results-load-mode';

/** Реактивная обёртка над ResultsLoadModeHelper.getMode() (async, кэшируется внутри хелпера). */
export function useResultsLoadMode(): ResultsLoadMode {
  const [mode, setMode] = useState<ResultsLoadMode>('full');

  useEffect(() => {
    let alive = true;
    ResultsLoadModeHelper.getMode().then((m) => {
      if (alive) setMode(m);
    });
    return () => {
      alive = false;
    };
  }, []);

  return mode;
}
