import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import SetupProfile from '../projects/setup-profile-project/setup-profile';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';

const container = document.getElementById('setup-profile-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <SetupProfile />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
