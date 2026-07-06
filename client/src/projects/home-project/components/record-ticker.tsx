import React from 'react';

// Статичный плейсхолдер: подключение реальных данных — отдельная задача
const TICKER_ITEMS: { label?: string; labelClass?: string; text: string; time?: string }[] = [
  { label: 'NEW RECORD', labelClass: 'text-[#facc15]', text: '· 50m Free · Ben Adam ·', time: '00:23.41' },
  { text: '100m Fly · Maya Levi ·', time: '01:02.87' },
  { label: 'LIVE', labelClass: 'text-[#38ef8f]', text: '· 2026 Horef Masters Arena · Day 2' },
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
          {item.time && <span className="text-[#7dd3fc]">{item.time}</span>}
        </span>
      ))}
    </div>
  );
}

function RecordTicker() {
  return (
    <div className="hp-ticker fixed bottom-0 left-0 right-0 z-20 overflow-hidden border-t border-[#7dd3fc]/25 bg-[rgba(2,10,24,0.82)] py-4 backdrop-blur-[10px]">
      <div className="hp-ticker-track hp-mono flex w-max whitespace-nowrap text-[11.5px] font-bold text-[#bfe3f7] lg:text-[14px]">
        <TickerRow />
        <TickerRow ariaHidden />
      </div>
    </div>
  );
}

export default RecordTicker;
