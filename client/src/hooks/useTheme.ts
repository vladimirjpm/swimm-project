import { useEffect } from 'react';
import { useAppSelector } from '../store/store';

/**
 * Хук для автоматического переключения темы на основе filter_date_training_competition.
 * Устанавливает data-theme атрибут на <html> элемент.
 * 
 * Использование: вызвать один раз в корневом компоненте страницы.
 * 
 * Темы (задаются в index.css):
 * - Training: training, training-dashboard, training-nexaverse, training-ocean
 * - Competition: competition, competition-emerald, competition-dark
 * 
 * Кастомная тема для страницы:
 * - Добавьте data-training-theme="nexaverse" или data-competition-theme="dark" на <body>
 * - Хук автоматически применит полное имя темы (training-nexaverse, competition-dark)
 */
export const useTheme = () => {
  const filterType = useAppSelector(
    (state) => state.filterSelected.filter_date_training_competition
  );

  useEffect(() => {
    const baseTheme = filterType || 'training';
    
    // Проверяем кастомную тему для страницы из body атрибутов
    // Можно задать полное имя (training-nexaverse) или короткое (nexaverse)
    let customTheme: string | null = null;
    if (typeof document !== 'undefined') {
      if (baseTheme === 'training') {
        const trainingVariant = document.body.getAttribute('data-training-theme');
        if (trainingVariant) {
          // Поддержка полного имени (training-nexaverse) или короткого (nexaverse)
          customTheme = trainingVariant.startsWith('training-') 
            ? trainingVariant 
            : `training-${trainingVariant}`;
        }
      } else if (baseTheme === 'competition') {
        const competitionVariant = document.body.getAttribute('data-competition-theme');
        if (competitionVariant) {
          // Поддержка полного имени (competition-dark) или короткого (dark)
          customTheme = competitionVariant.startsWith('competition-') 
            ? competitionVariant 
            : `competition-${competitionVariant}`;
        }
      }
    }
    
    const finalTheme = customTheme || baseTheme;
    document.documentElement.dataset.theme = finalTheme;
    
    return () => {
      // Cleanup при размонтировании
    };
  }, [filterType]);

  return filterType || 'training';
};

export default useTheme;
