import { defineConfig } from 'vite';

const backend = process.env.HERO_CHESS_API_URL || 'http://localhost:5012';

export default defineConfig({
  base: '/react/',
  build: { outDir: '../HeroChess.Api/wwwroot/react', emptyOutDir: true },
  server: {
    proxy: {
      '/api/v1': { target: backend, changeOrigin: true },
      '/ws/v1': {
        target: backend,
        changeOrigin: true,
        ws: true,
        configure(proxy) {
          proxy.on('proxyReqWs', request => request.setHeader('Origin', backend));
        }
      }
    }
  }
});
