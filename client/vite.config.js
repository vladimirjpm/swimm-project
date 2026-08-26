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

// Dev-паритет к серверному rewrite (server/Swimm.API/Program.cs): чистые URL → html.
// Держать в синхроне с ним и с client/src/utils/routes.ts.
const cleanUrlRewrite = () => ({
  name: 'clean-url-rewrite',
  configureServer(server) {
    const resolve = (pathname) => {
      const seg = pathname.split('/').filter(Boolean);
      if (seg.length === 1) {
        return { results: '/results_main.html', competitions: '/competitions.html',
          groups: '/groups.html', clubs: '/results_main.html', 'my-media': '/media.html', about: '/about.html',
          'season-best': '/season-best.html' }[seg[0]] ?? null;
      }
      if (seg.length >= 2) {
        if (seg[0] === 'competitions') return '/results_main.html';
        if (seg[0] === 'swimmers') return '/swimmer.html';
        if (seg[0] === 'clubs') return '/club.html';
        if (seg[0] === 'groups') return seg[2] === 'results' ? '/results_main.html' : '/groups.html';
      }
      return null;
    };
    server.middlewares.use((req, _res, next) => {
      const url = req.url;
      if (typeof url === 'string') {
        const qIdx = url.indexOf('?');
        const pathname = qIdx === -1 ? url : url.slice(0, qIdx);
        const query = qIdx === -1 ? '' : url.slice(qIdx);
        // Только «чистые» пути без расширения в последнем сегменте.
        const lastSeg = pathname.slice(pathname.lastIndexOf('/') + 1);
        if (!lastSeg.includes('.') && !pathname.startsWith('/@') && !pathname.startsWith('/api') && !pathname.startsWith('/auth')) {
          const html = resolve(pathname);
          if (html) req.url = html + query;
        }
      }
      next();
    });
  },
});

export default defineConfig(({ command }) => ({
  // In dev we serve at '/', but we also accept '/swimm-project/*' via middleware.
  // In production builds we use relative paths so the same dist works on Azure and GH Pages.
  base: command === 'serve' ? '/' : './',
  plugins: [react(), tailwindcss(), swimmProjectPrefixRewrite(), cleanUrlRewrite()],
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
        swimmer: resolve(__dirname, 'swimmer.html'),
        club: resolve(__dirname, 'club.html'),
        season_best: resolve(__dirname, 'season-best.html'),
      },
      output: {
        entryFileNames: '[name].js',
      },
    },
  },
}))
