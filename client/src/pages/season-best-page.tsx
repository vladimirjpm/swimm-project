import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import SeasonBestProject from '../projects/season-best-project/season-best-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import RecordsHelper from '../utils/helpers/records-helper';

// Справочник нормативов нужен каждой странице САМОЙ: страницы — отдельные сборки, и прогрев
// в `index.tsx` сюда не доедет. Без него дуга уровня в строке заплыва молча показывает «—».
RecordsHelper.warmUp();

const container = document.getElementById('season-best-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <SeasonBestProject />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
