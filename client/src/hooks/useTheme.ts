import { useEffect } from 'react';
import { useAppSelector } from '../store/store';

/**
 * Хук для автоматического переключения темы на основе filter_date_training_competition.
 * Устанавливает data-theme атрибут на <html> элемент.
 * 
 * Использование: вызвать один раз в корневом компоненте страницы.
 * 
 * Темы:
 * - 'training' (по умолчанию) — синяя цветовая гамма
 * - 'competition' — зелёная цветовая гамма
 */
export const useTheme = () => {
  const filterType = useAppSelector(
    (state) => state.filterSelected.filter_date_training_competition
  );

  useEffect(() => {
    const theme = filterType || 'training';
    document.documentElement.dataset.theme = theme;
    
    // Cleanup: сбросить тему при размонтировании (опционально)
    return () => {
      // document.documentElement.dataset.theme = 'training';
    };
  }, [filterType]);

  return filterType || 'training';
};

export default useTheme;
