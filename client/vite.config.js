import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))

const swimmProjectPrefixRewrite = () => ({
  name: 'swimm-project-prefix-rewrite',
  configureServer(server) {
    server.middlewares.use((req, _res, next) => {
      const url = req.url;
      if (typeof url === 'string' && url.startsWith('/swimm-project/')) {
        req.url = url.replace('/swimm-project', '');
      }
      next();
    });
  },
});

export default defineConfig(({ command }) => ({
  // In dev we serve at '/', but we also accept '/swimm-project/*' via middleware.
  // In production builds we use relative paths so the same dist works on Azure and GH Pages.
  base: command === 'serve' ? '/' : './',
  plugins: [react(), tailwindcss(), swimmProjectPrefixRewrite()],
  // Dev-прокси на API (Swimm.API, http://localhost:5078): относительные запросы клиента
  // (/api/*, /auth/*) уходят на бэкенд как same-origin — куки и antiforgery работают без CORS.
  // В проде клиент раздаётся самим API (wwwroot), поэтому те же относительные пути валидны.
  // SWIMM_API_TARGET позволяет указать другой инстанс API (например, второй на :5079),
  // не трогая конфиг — удобно, когда :5078 занят отладчиком Visual Studio.
  server: {
    proxy: {
      '/api': { target: process.env.SWIMM_API_TARGET ?? 'http://localhost:5078', changeOrigin: true },
      '/auth': { target: process.env.SWIMM_API_TARGET ?? 'http://localhost:5078', changeOrigin: true },
    },
  },
  build: {
    outDir: 'dist',
    rollupOptions: {
      input: {
        index: resolve(__dirname, 'index.html'),
        home: resolve(__dirname, 'home.html'),
        results_main: resolve(__dirname, 'results_main.html'),
        about: resolve(__dirname, 'about.html'),
        competitions: resolve(__dirname, 'competitions.html'),
        groups: resolve(__dirname, 'groups.html'),
        media: resolve(__dirname, 'media.html'),
      },
      output: {
        entryFileNames: '[name].js',
      },
    },
  },
}))
