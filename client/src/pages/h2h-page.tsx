import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import H2HProject from '../projects/h2h-project/h2h-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import RecordsHelper from '../utils/helpers/records-helper';

// Справочник нормативов нужен каждой странице САМОЙ: страницы — отдельные сборки, и прогрев
// в `index.tsx` сюда не доедет.
RecordsHelper.warmUp();

const container = document.getElementById('h2h-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <H2HProject />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
