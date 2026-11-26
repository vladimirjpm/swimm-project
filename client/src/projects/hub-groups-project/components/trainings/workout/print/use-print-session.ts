// React-обёртка над сеансом печати (`print-mode.ts`): сеанс живёт, пока смонтирован вызывающий
// (окно предпросмотра), и закрывается при размонтировании — `@page` и класс на `<html>` не
// переживают окно (§9 плана: забытый `@page` ломает печать всего сайта).
//
// Пользоваться так (Ш4):
//   const { root, print } = usePrintSession('portrait');
//   return root && createPortal(
//     <div className="…окно…">
//       <div className="wprint-noprint">…Print / Close…</div>
//       <div className="wprint-stage" style={{ transform: `scale(${k})` }}>
//         <div className="wprint-sheet">…лист…</div>
//       </div>
//     </div>, root);
// Что из этого прячется и сбрасывается при печати — шапка `workout-print.css`.

import { useCallback, useLayoutEffect, useRef, useState } from 'react';
import { openPrintSession, type PrintOrientation, type PrintSession } from './print-mode';
import './workout-print.css';

export interface PrintSessionHandle {
  /** Корень портала (прямой ребёнок `<body>`); `null` до первого эффекта. */
  root: HTMLElement | null;
  /** Печать листа; без открытого сеанса — ничего. */
  print: () => void;
}

export function usePrintSession(orientation: PrintOrientation = 'portrait'): PrintSessionHandle {
  const session = useRef<PrintSession | null>(null);
  const [root, setRoot] = useState<HTMLElement | null>(null);

  useLayoutEffect(() => {
    const s = openPrintSession(window, orientation);
    session.current = s;
    setRoot(s.root);
    return () => {
      s.close();
      if (session.current === s) session.current = null;
      setRoot(null);
    };
  }, [orientation]);

  const print = useCallback(() => session.current?.print(), []);
  return { root, print };
}
