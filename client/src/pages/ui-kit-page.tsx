// Точка входа dev-песочницы UI (client/ui-kit.html). Не входит в прод-сборку: страницы нет
// в `rollupOptions.input` — см. комментарий в ui-kit.html.
import React from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import store from '../store/store';
import '../index.css';
import UiKitApp from '../dev/ui-kit/ui-kit-app';

// Provider обязателен: компоненты витрины (например UI_SwimmStyleIcon) дёргают
// useAppDispatch и без стора падают на первом же рендере.
const container = document.getElementById('ui-kit-page')!;
createRoot(container).render(
  <React.StrictMode>
    <Provider store={store}>
      <UiKitApp />
    </Provider>
  </React.StrictMode>
);

export {};
