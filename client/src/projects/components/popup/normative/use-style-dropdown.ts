import { useEffect, useRef, useState } from 'react';

/** Дропдаун «Swimming Style» (стиль · дистанция): открыт/закрыт + закрытие по клику снаружи. */
export const useStyleDropdown = () => {
  const [styleOpen, setStyleOpen] = useState(false);
  const ddRef = useRef<HTMLDivElement>(null);

  // Закрытие дропдауна по клику снаружи
  useEffect(() => {
    if (!styleOpen) return;
    const onDown = (e: MouseEvent) => {
      if (ddRef.current && !ddRef.current.contains(e.target as Node)) setStyleOpen(false);
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [styleOpen]);

  return { styleOpen, setStyleOpen, ddRef };
};
