import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import RecordsProject from '../projects/records-project/records-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';

// RecordsHelper.warmUp() здесь НЕ нужен: страница ходит в /api/records/ranking напрямую и
// легаси-дерево нормативов не читает (11.2.3). Прогревать справочник одной страны ради
// рейтинга двух сотен — лишний запрос на каждую загрузку.

const container = document.getElementById('records-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <RecordsProject />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
