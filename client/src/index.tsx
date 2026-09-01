import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from './store/store';
import HomePage from './projects/home-project/home';
import AboutPage from './projects/about-project/about';
//import DolphineTraining from './projects/results-main/results-main';
import ResultsMain from './projects/results-main-project/results-main-project';
import reportWebVitals from './reportWebVitals';
import RecordsHelper from './utils/helpers/records-helper';
import './index.css';

// Прогрев рекордов/нормативов с сервера: до первого ответа справочники пустые
// («нет данных»), глобальных window.normative_* больше нет — статику снесли в фазе 2.7.
RecordsHelper.warmUp();

const container = document.getElementById('root')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <ResultsMain />
    </Provider>
  </React.StrictMode>
);

// If you want to start measuring performance in your app, pass a function
// to log results (for example: reportWebVitals(console.log))
// or send to an analytics endpoint. Learn more: https://bit.ly/CRA-vitals
reportWebVitals();
