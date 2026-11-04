import { defineConfig } from 'vite';

// Dev mặc định trỏ Azure. Đặt HERO_CHESS_API_URL=http://localhost:5012 để test BE local.
const backend = process.env.HERO_CHESS_API_URL || 'https://herochessbe.eastasia.cloudapp.azure.com';

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
