import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { resolve, dirname, join } from 'path'
import { fileURLToPath } from 'url'
import {
  existsSync,
  readFileSync,
  renameSync,
  readdirSync,
  rmSync
} from 'fs'

const __dirname = dirname(fileURLToPath(import.meta.url))
const deployConfigPath = resolve(__dirname, '../deploy.config.json')
const deployConfig = JSON.parse(readFileSync(deployConfigPath, 'utf-8'))
const manualGamePackages = new Set(deployConfig.manualGamePackages || [])

/**
 * 读取 open_mahjong_web/deploy.config.json：
 * - 不把 public/ 下的手动游戏包拷进 dist
 * - 清空 dist 时保留这些目录（人工安放，勿覆盖）
 */
function skipManualGamePackages() {
  const publicDir = resolve(__dirname, 'public')
  const distDir = resolve(__dirname, 'dist')
  const publicMoved = []

  const restorePublic = () => {
    for (const { from, to } of publicMoved.splice(0)) {
      if (existsSync(to) && !existsSync(from)) {
        renameSync(to, from)
      }
    }
  }

  return {
    name: 'skip-manual-game-packages',
    apply: 'build',
    config() {
      // 关闭默认整目录清空，改由本插件按白名单清理
      return { build: { emptyOutDir: false } }
    },
    buildStart() {
      if (existsSync(distDir)) {
        for (const name of readdirSync(distDir)) {
          if (manualGamePackages.has(name)) continue
          rmSync(join(distDir, name), { recursive: true, force: true })
        }
      }
      for (const name of manualGamePackages) {
        const from = join(publicDir, name)
        const to = join(publicDir, `.${name}.buildskip`)
        if (existsSync(from)) {
          if (existsSync(to)) {
            throw new Error(`buildskip 残留: ${to}，请先恢复或删除后再构建`)
          }
          renameSync(from, to)
          publicMoved.push({ from, to })
        }
      }
    },
    buildEnd: restorePublic,
    closeBundle: restorePublic
  }
}

export default defineConfig({
  plugins: [vue(), skipManualGamePackages()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src')
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:3000',
        changeOrigin: true,
        ws: true,
      },
      '/2d/api': {
        target: 'http://localhost:3000',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/2d\/api/, '/api')
      },
      '/2d/ws': {
        target: 'ws://localhost:8081',
        changeOrigin: true,
        ws: true,
        rewrite: (path) => path.replace(/^\/2d\/ws/, '/game')
      }
    }
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    emptyOutDir: false
  }
})
