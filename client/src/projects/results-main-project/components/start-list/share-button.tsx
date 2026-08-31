import React, { useState } from 'react';

/**
 * «Share link ↗» — ОДНА кнопка на все экраны таба (карточка плана, программа, заплыв,
 * карточка пловца). Раньше жила внутри `plan-card.tsx`, и поделиться можно было только из
 * плана; вынесена 31.08.2026, когда кнопка понадобилась и на программе.
 *
 * На телефоне отдаём системному «Поделиться» (ссылку шлют в родительский чат — это и есть
 * основной сценарий), на десктопе кладём в буфер.
 */
export default function ShareButton({ url }: { url: string }) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle');

  const share = async () => {
    try {
      if (navigator.share) {
        await navigator.share({ url });
        return;
      }
      await navigator.clipboard.writeText(url);
      setState('copied');
      window.setTimeout(() => setState('idle'), 2000);
    } catch {
      // Отмена системного диалога тоже приходит сюда — но врать «скопировано» нельзя.
      setState('failed');
      window.setTimeout(() => setState('idle'), 2000);
    }
  };

  return (
    <button
      type="button"
      onClick={share}
      className="shrink-0 rounded-full px-3 py-1.5 text-[11.5px] font-black"
      style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
    >
      {state === 'copied' ? 'Link copied' : state === 'failed' ? 'Copy failed' : 'Share link ↗'}
    </button>
  );
}
