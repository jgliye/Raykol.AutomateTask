import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';

export default defineConfig({
  plugins: [vue()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: process.env.BACKEND_URL || 'http://127.0.0.1:18558',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: '../wwwroot/Web',
    emptyOutDir: true,
    sourcemap: false,
    rolldownOptions: {
      output: {
        strictExecutionOrder: true,
        codeSplitting: {
          groups: [{ name: 'vendor', test: /node_modules/, maxSize: 1000000 }],
        },
      },
    },
  },
});
