import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import Groups from '../projects/hub-groups-project/groups';
import { LoginModalProvider } from '../projects/components/login-modal/login-modal-context';
import '../index.css';

const container = document.getElementById('groups-page')!;
const root = createRoot(container);

// LoginModalProvider — ради гостевой подсказки «создайте свою группу» в панели «My groups»:
// её кнопка открывает вход, не уводя со страницы (так же, как заглушка /my-media).
root.render(
  <React.StrictMode>
    <Provider store={store}>
      <LoginModalProvider>
        <Groups />
      </LoginModalProvider>
    </Provider>
  </React.StrictMode>
);

export {};
