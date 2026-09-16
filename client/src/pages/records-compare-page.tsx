import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import RecordsCompareProject from '../projects/records-project/records-compare-project';
import { FavoritesProvider } from '../hooks/favorites-context';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';

// RecordsHelper.warmUp() не нужен — как и на /records, страница ходит в API напрямую (11.2.3).

const container = document.getElementById('records-compare-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <FavoritesProvider>
          <RecordsCompareProject />
        </FavoritesProvider>
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
