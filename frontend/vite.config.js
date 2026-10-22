import { defineConfig } from 'vite';

// Dev mặc định trỏ về API local (5012) để tính năng mới chưa deploy vẫn chạy được.
// Trỏ sang production bằng HERO_CHESS_API_URL=https://herochessbackend.onrender.com khi cần.
const backend = process.env.HERO_CHESS_API_URL || 'http://127.0.0.1:5012';

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
