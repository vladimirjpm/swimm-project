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

  const label = state === 'copied' ? 'Link copied' : state === 'failed' ? 'Copy failed' : 'Share link ↗';

  return (
    // Мобайл (5dm) — круглая кнопка 42×42 с одной стрелкой: в строку с сегмент-контролом
    // полноразмерная пилюля не влезает и выталкивает её за край экрана. От 640px — обычная
    // пилюля с подписью. Состояние («скопировано») одно на оба вида, поэтому это ОДНА
    // кнопка с адаптивной подписью, а не две по брейкпойнту.
    <button
      type="button"
      onClick={share}
      aria-label={label}
      title={label}
      className="flex h-[42px] w-[42px] flex-none items-center justify-center rounded-full text-[15px] font-black sm:h-auto sm:w-auto sm:px-3 sm:py-1.5 sm:text-[11.5px]"
      style={{ background: 'var(--deep-accent)', color: 'var(--deep-accent-ink)' }}
    >
      <span className="sm:hidden" aria-hidden>{state === 'idle' ? '↗' : '✓'}</span>
      <span className="hidden sm:inline">{label}</span>
    </button>
  );
}
