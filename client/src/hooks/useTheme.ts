import { useEffect } from 'react';
import { useAppSelector } from '../store/store';

// Цвета кнопок для каждой темы
const themeButtonColors: Record<string, { bg: string; text: string }> = {
  // Training themes
  'training': { bg: '#5b5fc7', text: '#ffffff' },
  'training-dashboard': { bg: '#5b5fc7', text: '#ffffff' },
  'training-nexaverse': { bg: '#1a5f7a', text: '#ffffff' },
  'training-ocean': { bg: '#0077b6', text: '#ffffff' },
  // Competition themes
  'competition': { bg: '#10b981', text: '#ffffff' },
  'competition-emerald': { bg: '#10b981', text: '#ffffff' },
  'competition-blue': { bg: '#0466c8', text: '#ffffff' },
  'competition-warm': { bg: '#7fb685', text: '#ffffff' },
  'competition-dark': { bg: '#34d399', text: '#022c22' },
};

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
 * 
 * Кнопки Training/Competition:
 * - Кнопка Training всегда берёт цвет из data-training-theme
 * - Кнопка Competition всегда берёт цвет из data-competition-theme
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
    
    // Устанавливаем фиксированные цвета кнопок из соответствующих тем
    // Кнопка Training всегда берёт цвет из data-training-theme
    // Кнопка Competition всегда берёт цвет из data-competition-theme
    const trainingThemeAttr = document.body.getAttribute('data-training-theme');
    const competitionThemeAttr = document.body.getAttribute('data-competition-theme');
    
    // Определяем полные имена тем
    const trainingThemeName = trainingThemeAttr 
      ? (trainingThemeAttr.startsWith('training-') ? trainingThemeAttr : `training-${trainingThemeAttr}`)
      : 'training-dashboard';
    const competitionThemeName = competitionThemeAttr
      ? (competitionThemeAttr.startsWith('competition-') ? competitionThemeAttr : `competition-${competitionThemeAttr}`)
      : 'competition-emerald';
    
    // Получаем цвета кнопок из тем
    const trainingColors = themeButtonColors[trainingThemeName] || themeButtonColors['training-dashboard'];
    const competitionColors = themeButtonColors[competitionThemeName] || themeButtonColors['competition-emerald'];
    
    // Устанавливаем CSS переменные на :root
    document.documentElement.style.setProperty('--btn-training-fixed-bg', trainingColors.bg);
    document.documentElement.style.setProperty('--btn-training-fixed-text', trainingColors.text);
    document.documentElement.style.setProperty('--btn-competition-fixed-bg', competitionColors.bg);
    document.documentElement.style.setProperty('--btn-competition-fixed-text', competitionColors.text);
    
    return () => {
      // Cleanup при размонтировании
    };
  }, [filterType]);

  return filterType || 'training';
};

export default useTheme;
