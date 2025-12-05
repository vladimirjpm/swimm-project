import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = dirname(fileURLToPath(import.meta.url))

export default defineConfig({
  base: '/swimm-project/',
  plugins: [react(), tailwindcss()],
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
})
