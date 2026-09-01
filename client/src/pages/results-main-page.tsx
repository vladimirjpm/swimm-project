import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import ResultsMain from '../projects/results-main-project/results-main-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import { applyResultsCategory } from '../utils/constants/results-categories';
import RecordsHelper from '../utils/helpers/records-helper';

const pageParams = new URLSearchParams(window.location.search);
applyResultsCategory(pageParams.get('category') ?? pageParams.get('cat'));

// Прогрев рекордов/нормативов с сервера: до первого ответа справочники пустые
// («нет данных»), глобальных window.normative_* больше нет — статику снесли в фазе 2.7.
RecordsHelper.warmUp();

const container = document.getElementById('root')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <ResultsMain />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};