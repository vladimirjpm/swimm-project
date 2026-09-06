import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import MyMedia from '../projects/my-media-project/my-media';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';
import RecordsHelper from '../utils/helpers/records-helper';

// Справочник рекордов нужен странице САМОЙ: страницы — отдельные сборки, прогрев из
// index.tsx сюда не доедет. Без него бейдж рекорда в строке молча не появится.
RecordsHelper.warmUp();

const container = document.getElementById('media-page')!;
const root = createRoot(container);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <MyMedia />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
