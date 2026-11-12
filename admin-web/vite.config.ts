import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

// Dev: AdminWeb chạy ở port 5175 và proxy /api/v1 sang backend.
// Backend KHÔNG bật CORS, nên bắt buộc dùng proxy (không gọi cross-origin thẳng).
// Đặt HERO_CHESS_API_URL=http://localhost:5012 để test với BE local.
const backend = process.env.HERO_CHESS_API_URL || 'http://localhost:5012';

export default defineConfig(({ command }) => ({
  // Chỉ khi build mới đặt base /admin/ (để serve từ wwwroot/admin). Dev chạy ở gốc.
  base: command === 'build' ? '/admin/' : '/',
  plugins: [react(), tailwindcss()],
  build: { outDir: 'dist', emptyOutDir: true },
  server: {
    port: 5175,
    proxy: {
      '/api/v1': { target: backend, changeOrigin: true }
    }
  }
}));
