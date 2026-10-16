import React from 'react';
import { loadClientConfig } from '../../../utils/helpers/client-config';

// Статичный плейсхолдер: подключение реальных данных — отдельная задача.
// ⚠ Имена и времена ВЫДУМАНЫ, поэтому в прод-сборку лента не попадает (см. RecordTicker):
// на публичном сайте «NEW RECORD · Ben Adam» читался бы как настоящий результат.
const TICKER_ITEMS: { label?: string; labelClass?: string; text: string; time?: string }[] = [
  { label: 'NEW RECORD', labelClass: 'text-[var(--t-warn)]', text: '· 50m Free · Ben Adam ·', time: '00:23.41' },
  { text: '100m Fly · Maya Levi ·', time: '01:02.87' },
  { label: 'LIVE', labelClass: 'text-[var(--t-live)]', text: '· 2026 Horef Masters Arena · Day 2' },
  { text: '200m Back · Daniel K. ·', time: '02:11.85' },
];

function TickerRow({ ariaHidden }: { ariaHidden?: boolean }) {
  return (
    <div
      aria-hidden={ariaHidden}
      className="flex items-center gap-11 pr-11 lg:gap-14 lg:pr-14"
    >
      {TICKER_ITEMS.map((item, i) => (
        <span key={i} className="flex items-center gap-2">
          {item.label && <span className={item.labelClass}>{item.label}</span>}
          <span>{item.text}</span>
          {item.time && <span className="text-[var(--t-accent)]">{item.time}</span>}
        </span>
      ))}
    </div>
  );
}

function RecordTicker() {
  // Включается настройкой RecordTickerEnabled в /Admin/Settings (дефолт false, 16.10.2026).
  // До ответа конфига ленты нет — чтобы выключенная не мелькнула при загрузке.
  const [enabled, setEnabled] = React.useState(false);
  React.useEffect(() => {
    let alive = true;
    loadClientConfig().then((cfg) => {
      if (alive) setEnabled(cfg?.recordTickerEnabled === true);
    });
    return () => {
      alive = false;
    };
  }, []);

  // И при включённой настройке — только dev-сервер: имена в ленте выдуманы, в проде её нет, пока
  // нет реальных данных. Кнопка режима сама опускается на место, когда ленты нет
  // (`home.css`, `:has(.hp-ticker)`).
  if (!enabled || !import.meta.env.DEV) return null;

  return (
    <div className="hp-ticker fixed bottom-0 left-0 right-0 z-20 overflow-hidden border-t border-[var(--t-border)] bg-[var(--t-surface-strong)] backdrop-blur-[10px]">
      <div className="hp-ticker-track hp-mono flex w-max whitespace-nowrap text-[11.5px] font-bold text-[var(--t-text-2)] lg:text-[14px]">
        <TickerRow />
        <TickerRow ariaHidden />
      </div>
    </div>
  );
}

export default RecordTicker;
