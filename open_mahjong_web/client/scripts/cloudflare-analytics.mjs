import { loadEnv } from 'vite'

// This is the public Web Analytics site token, never a Cloudflare API key.
export function analyticsToken(root, mode = 'production') {
  return process.env.VITE_CLOUDFLARE_ANALYTICS_TOKEN || loadEnv(mode, root, 'VITE_').VITE_CLOUDFLARE_ANALYTICS_TOKEN || ''
}

export function cloudflareAnalyticsMarkup(rawToken) {
  const token = String(rawToken || '').trim();
  if (!token) return ''
  if (!/^[a-f0-9]{32}$/i.test(token)) throw new Error('VITE_CLOUDFLARE_ANALYTICS_TOKEN must be a 32-character Web Analytics site token')
  // No local/preview or admin traffic. Cloudflare's beacon handles SPA navigation itself.
  return `<script data-salasasa-analytics>(function(){if(!/^(www\\.)?salasasa\\.cn$/.test(location.hostname)||/^\\/(admin|event-admin)(\\/|$)/.test(location.pathname)||document.querySelector('script[data-cf-beacon]'))return;var s=document.createElement('script');s.type='module';s.src='https://static.cloudflareinsights.com/beacon.min.js';s.defer=true;s.setAttribute('data-cf-beacon','{"token":"${token}"}');document.body.appendChild(s)})();</script>`
}

export function cloudflareAnalyticsPlugin() {
  let markup = ''
  return {
    name: 'salasasa-cloudflare-analytics', apply: 'build',
    configResolved(config) { markup = cloudflareAnalyticsMarkup(analyticsToken(config.root, config.mode)) },
    transformIndexHtml(html) { return html.replace('</body>', `${markup}\n</body>`) },
  }
}
