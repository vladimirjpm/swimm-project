import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Home from '../projects/home-project/home';
import '../index.css';
import '../projects/home-project/home.css';

const container = document.getElementById('home-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <Home />
    </Provider>
  </React.StrictMode>
);

export {};