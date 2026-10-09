import { useEffect, useState } from 'react';

// Мобильный compare-вид включается ниже ~760px (breakpoint прототипа)
export const useIsMobile = () => {
  const [mobile, setMobile] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(max-width: 759px)').matches,
  );
  useEffect(() => {
    const mq = window.matchMedia('(max-width: 759px)');
    const onChange = (e: MediaQueryListEvent) => setMobile(e.matches);
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);
  return mobile;
};
