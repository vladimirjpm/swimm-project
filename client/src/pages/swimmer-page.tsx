import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import SwimmerProject from '../projects/swimmer-project/swimmer-project';
import RecordsHelper from '../utils/helpers/records-helper';

// Прогрев рекордов/нормативов (как на других страницах) — на случай если карточка
// начнёт показывать уровни/нормативы; безвредно, если нет.
RecordsHelper.warmUp();

const container = document.getElementById('swimmer-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <SwimmerProject />
    </Provider>
  </React.StrictMode>
);

export {};
