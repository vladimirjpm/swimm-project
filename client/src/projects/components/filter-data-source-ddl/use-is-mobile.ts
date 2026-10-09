import React from 'react';

// Брейкпоинт дропдаун ↔ bottom sheet: <640px (Tailwind sm)
export const useIsMobile = () => {
  const [mobile, setMobile] = React.useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 639px)').matches,
  );
  React.useEffect(() => {
    const mq = window.matchMedia('(max-width: 639px)');
    const onChange = () => setMobile(mq.matches);
    // Синхронизируемся сразу: между вычислением начального состояния и подпиской
    // ширина могла измениться, и тогда состояние осталось бы навсегда неверным.
    onChange();
    mq.addEventListener('change', onChange);
    // Подписка на resize — страховка: при программной смене метрик вьюпорта (devtools,
    // эмуляция устройства) событие change у matchMedia приходит не всегда, и селектор
    // рисовал мобильный sheet на десктопе (эталон — useIsSmUp в gender-age-table).
    window.addEventListener('resize', onChange);
    return () => {
      mq.removeEventListener('change', onChange);
      window.removeEventListener('resize', onChange);
    };
  }, []);
  return mobile;
};
