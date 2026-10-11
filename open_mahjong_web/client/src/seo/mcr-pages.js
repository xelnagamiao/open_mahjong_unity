import { STATIC_PAGES } from './pages.js'

export const MCR_PAGES = STATIC_PAGES.filter(page => page.family === 'play-mcr')
export const MCR_LOCALES = MCR_PAGES.map(page => ({ ...page, published: true }))

export function mcrPageForPath(path) {
  return MCR_PAGES.find(page => page.path === String(path).replace(/\/+$/, '')) || null
}