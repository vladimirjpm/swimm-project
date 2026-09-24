import React from 'react';
import { createRoot } from 'react-dom/client';
import PersonaSwitcher from './persona-switcher';

// Точка входа, которую плагин devPersonaSwitcher (vite.config.js) вставляет в каждую страницу
// ТОЛЬКО в dev. Свой корень в <body>: плашка не зависит от дерева страницы и её Redux.
const host = document.createElement('div');
host.id = 'dev-persona-switcher-root';
document.body.appendChild(host);
createRoot(host).render(<PersonaSwitcher />);

export {};
