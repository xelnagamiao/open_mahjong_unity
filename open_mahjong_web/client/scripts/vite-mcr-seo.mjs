import { readFileSync, existsSync } from 'node:fs'
import { resolve } from 'node:path'
import { SITE } from '../src/seo.js'
import { STATIC_PAGES, staticPageForPath } from '../src/seo/pages.js'
import { renderPageDocument } from '../src/seo/page-render.js'
import { renderSitemap } from '../src/seo/sitemap.js'
import { originalPageContent } from './original-seo-content.mjs'

// Match only this feature's URLs; game, auth, API and all existing pages pass through.
const paths = new Set(STATIC_PAGES.map((entry) => entry.path))
const namespacePattern = /^\/(en|fr|zh|de|nl|es|it)(?:\/|$)/

function seoMiddleware(server, preview = false) {
  return async (req, res, next) => {
    if (!['GET', 'HEAD'].includes(req.method)) return next()
    const url = new URL(req.url, 'http://localhost')
    if (!preview && url.pathname === '/sitemap.xml') {
      res.setHeader('Content-Type', 'application/xml; charset=utf-8')
      res.end(req.method === 'HEAD' ? '' : renderSitemap())
      return
    }
    const path = url.pathname.replace(/\/(?:index\.html)?$/, '')
    if (!paths.has(path)) {
      if (!namespacePattern.test(url.pathname)) return next()
      res.statusCode = 404
      res.setHeader('Content-Type', 'text/plain; charset=utf-8')
      res.end(req.method === 'HEAD' ? '' : 'This language page is not available.')
      return
    }
    if (path !== url.pathname) {
      res.statusCode = 301
      res.setHeader('Location', path + url.search)
      res.end()
      return
    }
    const page = staticPageForPath(path)
    const builtPath = resolve(server.config.root, server.config.build.outDir, path.slice(1), 'index.html')
    if (!page || (preview && !existsSync(builtPath))) {
      res.statusCode = 404
      res.setHeader('Content-Type', 'text/plain; charset=utf-8')
      res.end(req.method === 'HEAD' ? '' : 'This language page is not available yet.')
      return
    }
    let html
    try {
      html = preview ? readFileSync(builtPath, 'utf8')
        : renderPageDocument(page, ['mcr-landing.css', 'content-pages.css'].map((file) => readFileSync(resolve(server.config.root, 'src/seo', file), 'utf8')).join('\n'), SITE.domain, await originalPageContent(page))
    } catch (error) { return next(error) }
    res.setHeader('Content-Type', 'text/html; charset=utf-8')
    res.end(req.method === 'HEAD' ? '' : html)
  }
}

export function mcrSeoPlugin() {
  return {
    name: 'mcr-static-seo',
    configureServer(server) { server.middlewares.use(seoMiddleware(server)) },
    configurePreviewServer(server) { server.middlewares.use(seoMiddleware(server, true)) },
  }
}
