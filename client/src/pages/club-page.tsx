import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import ClubProject from '../projects/club-project/club-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';

const container = document.getElementById('club-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <ClubProject />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
