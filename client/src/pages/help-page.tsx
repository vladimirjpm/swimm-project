import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Help from '../projects/help-project/help';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';

const container = document.getElementById('help-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <Help />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
