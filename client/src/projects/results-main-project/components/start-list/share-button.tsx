import React, { useState } from 'react';

/**
 * Кнопка «поделиться ссылкой» — ОДНА на все экраны таба (карточка плана, программа, заплыв,
 * карточка пловца). Раньше жила внутри `plan-card.tsx`, и поделиться можно было только из
 * плана; вынесена 31.08.2026, когда кнопка понадобилась и на программе.
 *
 * На телефоне отдаём системному «Поделиться» (ссылку шлют в родительский чат — это и есть
 * основной сценарий), на десктопе кладём в буфер.
 *
 * Вид (решение Влада 22.10.2026): круглая светлая кнопка с иконкой share (три узла) на ВСЕХ
 * ширинах — вместо синей пилюли «Share link ↗» на десктопе и стрелки на мобайле. Подпись
 * осталась в `title`/`aria-label`; после копирования иконка на 2 с меняется на галочку.
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

  const label = state === 'copied' ? 'Link copied' : state === 'failed' ? 'Copy failed' : 'Share link';

  return (
    <button
      type="button"
      onClick={share}
      aria-label={label}
      title={label}
      className="flex h-[42px] w-[42px] flex-none items-center justify-center rounded-full border"
      style={{
        background: 'var(--deep-accent-chip)',
        borderColor: 'var(--deep-accent-border)',
        color: 'var(--deep-accent)',
      }}
    >
      <svg
        width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
        strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" aria-hidden
      >
        {state === 'copied' ? (
          <path d="M5 12.5l4.5 4.5L19 7.5" />
        ) : (
          <>
            {/* Три узла: справа сверху, слева посередине, справа снизу — и две связи. */}
            <circle cx="17.5" cy="5.5" r="2.5" />
            <circle cx="6.5" cy="12" r="2.5" />
            <circle cx="17.5" cy="18.5" r="2.5" />
            <path d="M8.7 10.7l6.6-3.9M8.7 13.3l6.6 3.9" />
          </>
        )}
      </svg>
    </button>
  );
}
