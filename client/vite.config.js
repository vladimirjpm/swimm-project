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
  build: {
    outDir: 'dist',
    rollupOptions: {
      input: {
        home: resolve(__dirname, 'home.html'),
        results_main: resolve(__dirname, 'results_main.html'),
        'dolphin-masters': resolve(__dirname, 'dolphin-masters.html'),
        about: resolve(__dirname, 'about.html'),
      },
      output: {
        entryFileNames: '[name].js',
      },
    },
  },
}))
