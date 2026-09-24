import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import MyFavorites from '../projects/my-favorites-project/my-favorites';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';

const container = document.getElementById('my-favorites-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <MyFavorites />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
