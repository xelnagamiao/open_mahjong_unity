import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  const apiTarget = env.SALASASA_2D_API_PROXY_TARGET || 'http://127.0.0.1:3000'
  const wsTarget = env.SALASASA_2D_WS_PROXY_TARGET || 'ws://127.0.0.1:8081'
  const proxy = {
    '/2d/api': {
      target: apiTarget,
      changeOrigin: true,
      secure: false,
      rewrite: (path: string) => path.replace(/^\/2d\/api/, '/api'),
    },
    '/2d/ws': {
      target: wsTarget,
      changeOrigin: true,
      secure: false,
      ws: true,
      rewrite: (path: string) => path.replace(/^\/2d\/ws/, '/game'),
    },
  }
  return {
    base: '/2d/',
    plugins: [react()],
    server: { proxy },
    preview: { proxy },
  }
})
