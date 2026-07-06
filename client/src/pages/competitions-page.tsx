import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Competitions from '../projects/competitions-project/competitions';
import '../index.css';
import '../projects/home-project/home.css';

const container = document.getElementById('competitions-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <Competitions />
    </Provider>
  </React.StrictMode>
);

export {};
