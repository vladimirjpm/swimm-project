import React from 'react';

/**
 * Текст тренера на листе печати: слова с дефисом («1-1V», «Warm-up», «catch-up») не рвутся по
 * дефису. Иначе в ивритской строке «1-4V, 1-1V» распадалось на «1-4V, 1-» и «1V» строкой ниже.
 * Слово, которое не помещается целиком, подгонка видит как переполнение по ширине и берёт шрифт
 * меньше (`FIT_X_ATTR` у подписи) — переносов посреди слова на листе нет.
 */
export const PrintText: React.FC<{ text: string }> = ({ text }) => (
  <>
    {text.split(/(\s+)/).map((part, i) =>
      part.includes('-') ? <span key={i} className="whitespace-nowrap">{part}</span> : part,
    )}
  </>
);
