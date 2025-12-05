import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import About from '../projects/about-project/about';
import '../index.css';
import '../projects/about-project/about.css';


const container = document.getElementById('about-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <About />
    </Provider>
  </React.StrictMode>
);

export {};