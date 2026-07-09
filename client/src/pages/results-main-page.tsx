import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import ResultsMain from '../projects/results-main-project/results-main-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { applyResultsCategory } from '../utils/constants/results-categories';
import RecordsHelper from '../utils/helpers/records-helper';

const pageParams = new URLSearchParams(window.location.search);
applyResultsCategory(pageParams.get('category') ?? pageParams.get('cat'));

// Прогрев рекордов/нормативов с сервера (fallback на window.normative_* пока не загрузится)
RecordsHelper.warmUp();

const container = document.getElementById('root')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <FavoritesProvider>
        <ResultsMain />
      </FavoritesProvider>
    </Provider>
  </React.StrictMode>
);

export {};