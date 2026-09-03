/**
 * Прогрев сезонной таблицы SB для страницы результатов (`SeasonBestTable`).
 *
 * Хук не отдаёт данные — он лишь грузит таблицу нужного сезона и перерисовывает вызывающего,
 * когда она пришла: сами проверки строки делают синхронно через `SeasonBestTable.isSeasonBest`,
 * как и со справочником рекордов. `ready` = таблица в памяти (или её не удалось загрузить —
 * тогда бейджей просто нет).
 */
import { useEffect, useState } from 'react';
import SeasonBestTable from '../utils/helpers/season-best-table';

export function useSeasonBestTable(season: number | null | undefined): boolean {
  const [ready, setReady] = useState(() => (season != null ? SeasonBestTable.isLoaded(season) : false));

  useEffect(() => {
    if (season == null) {
      setReady(false);
      return;
    }
    if (SeasonBestTable.isLoaded(season)) {
      setReady(true);
      return;
    }

    let alive = true;
    setReady(false);
    void SeasonBestTable.load(season).then(() => {
      if (alive) setReady(true);
    });
    return () => { alive = false; };
  }, [season]);

  return ready;
}

export default useSeasonBestTable;
