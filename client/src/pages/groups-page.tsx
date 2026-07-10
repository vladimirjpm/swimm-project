import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Groups from '../projects/hub-groups-project/groups';
import '../index.css';

const container = document.getElementById('groups-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <Groups />
    </Provider>
  </React.StrictMode>
);

export {};
