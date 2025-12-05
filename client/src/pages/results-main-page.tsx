import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import ResultsMain from '../projects/results-main-project/results-main-project';

const container = document.getElementById('root')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <ResultsMain />
    </Provider>
  </React.StrictMode>
);

export {};