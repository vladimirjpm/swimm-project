import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Reminders from '../projects/reminders-project/reminders';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';

const container = document.getElementById('reminders-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <Reminders />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
