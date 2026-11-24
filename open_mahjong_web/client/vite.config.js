import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import send from 'send'
import { mcrSeoPlugin } from './scripts/vite-mcr-seo.mjs'
import { cloudflareAnalyticsPlugin } from './scripts/cloudflare-analytics.mjs'
import { rulePublicationPlugin } from './scripts/rule-publication.mjs'
import { resolve, dirname, join, relative, isAbsolute } from 'path'
import { fileURLToPath } from 'url'
import {
  existsSync,
  readFileSync,
  cpSync,
  mkdirSync,
  readdirSync,
  rmSync
} from 'fs'

const __dirname = dirname(fileURLToPath(import.meta.url))
const deployConfigPath = resolve(__dirname, '../deploy.config.json')
const deployConfig = JSON.parse(readFileSync(deployConfigPath, 'utf-8'))
const manualGamePackages = new Set(deployConfig.manualGamePackages || [])
const preserveStaticDirs = new Set([
  ...manualGamePackages,
  ...(deployConfig.preserveStaticDirs || [])
])

function localWebGLPreview() {
  const buildDir = resolve(__dirname, '../../open_mahjong_unity/build/buildweb')
  return {
    name: 'local-webgl-preview',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use('/unity-game', (req, res, next) => {
        if (!existsSync(join(buildDir, 'index.html'))) return next()
        if (req.method !== 'GET' && req.method !== 'HEAD') {
          res.writeHead(405, { Allow: 'GET, HEAD' }).end()
          return
        }
        const path = req.url.split('?')[0]
        const file = send(req, path, { root: buildDir, index: 'index.html' })
        file.on('headers', (response, filename) => {
          if (filename.endsWith('.html')) response.setHeader('Cache-Control', 'no-store')
          const compressed = filename.match(/\.(wasm|js|data)\.(gz|br)$/)
          if (!compressed) return
          response.setHeader('Content-Encoding', compressed[2] === 'gz' ? 'gzip' : 'br')
          response.setHeader('Content-Type', {
            wasm: 'application/wasm', js: 'application/javascript', data: 'application/octet-stream'
          }[compressed[1]])
        })
        file.on('error', error => {
          if (!res.headersSent) res.writeHead(error.statusCode || 500)
          res.end()
        })
        file.pipe(res)
      })
    }
  }
}

/**
 * 读取 open_mahjong_web/deploy.config.json：
 * - 不把 public/ 下的手动游戏包拷进 dist
 * - 清空 dist 时保留这些目录（人工安放，勿覆盖）
 */
function skipManualGamePackages() {
  let publicDir
  let distDir

  return {
    name: 'skip-manual-game-packages',
    apply: 'build',
    config() {
      // 关闭默认整目录清空，改由本插件按白名单清理
      return { build: { emptyOutDir: false, copyPublicDir: false } }
    },
    configResolved(config) {
      publicDir = config.publicDir
      distDir = resolve(config.root, config.build.outDir)
      // Refuse an output directory containing source files before any cleanup.
      for (const sourceDir of [config.root, publicDir].filter(Boolean)) {
        const sourceRelative = relative(distDir, sourceDir)
        if (!sourceRelative || (!sourceRelative.startsWith('..') && !isAbsolute(sourceRelative))) {
          throw new Error(`构建输出目录不能包含源文件: ${distDir}`)
        }
      }
    },
    buildStart() {
      if (existsSync(distDir)) {
        for (const name of readdirSync(distDir)) {
          if (preserveStaticDirs.has(name)) continue
          rmSync(join(distDir, name), { recursive: true, force: true })
        }
      }
    },
    writeBundle() {
      // Copy ordinary public assets without moving live Unity packages or uploads.
      if (!publicDir || !existsSync(publicDir)) return
      mkdirSync(distDir, { recursive: true })
      for (const name of readdirSync(publicDir)) {
        if (preserveStaticDirs.has(name)) continue
        if (/^\..+\.buildskip$/.test(name)) {
          throw new Error(`buildskip 残留: ${join(publicDir, name)}，请先恢复后再构建`)
        }
        cpSync(join(publicDir, name), join(distDir, name), { recursive: true })
      }
    }
  }
}

export default defineConfig({
  plugins: [vue(), localWebGLPreview(), skipManualGamePackages(), rulePublicationPlugin(), mcrSeoPlugin(), cloudflareAnalyticsPlugin()],
  resolve: {
    alias: {
      '@': resolve(__dirname, 'src')
    }
  },
  server: {
    port: 5173,
    watch: {
      ignored: ['**/data/activity-assets/**', '**/data/activities/**', '**/data/user-content/**']
    },
    proxy: {
      '/activity-assets': {
        target: 'http://localhost:3000',
        changeOrigin: true,
      },
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
      },
      '/verifier-api': {
        target: 'http://127.0.0.1:8099',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/verifier-api/, ''),
      }
    }
  },
  build: {
    outDir: 'dist',
    assetsDir: 'assets',
    emptyOutDir: false
  }
})
