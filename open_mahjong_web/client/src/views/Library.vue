<!-- 麻雀图书馆 · 独立馆页索引 -->
<template>
  <div class="lib">
    <div class="lib-bg" aria-hidden="true">
      <div class="grain" />
      <div class="rail" />
    </div>

    <header class="hero">
      <p class="eyebrow">OPEN ARCHIVE</p>
      <h1 class="brand">麻雀图书馆</h1>
      <p class="lede">规则书、牌例与讨论。按来源分列，点标签进入条目。</p>
      <nav class="hero-nav">
        <a
          v-for="sec in sections"
          :key="sec.key"
          class="jump"
          :href="`#sec-${sec.key}`"
          @click.prevent="scrollTo(sec.key)"
        >{{ sec.nav }}</a>
      </nav>
    </header>

    <main class="catalog">
      <section
        v-for="(sec, idx) in sections"
        :id="`sec-${sec.key}`"
        :key="sec.key"
        class="shelf"
        :style="{ '--delay': `${idx * 60}ms` }"
      >
        <div class="shelf-meta">
          <span class="idx">{{ String(idx + 1).padStart(2, '0') }}</span>
          <div>
            <h2>{{ sec.title }}</h2>
            <p>{{ sec.hint }}</p>
          </div>
        </div>
        <div class="tags">
          <router-link
            v-for="rule in rulesForSection(sec.key)"
            :key="`${sec.key}-${rule.key}`"
            class="tag"
            :to="`/library/${rule.key}`"
            :style="{ '--ink': rule.accent }"
          >
            <span class="tag-mark" />
            <span class="tag-text">
              <strong>{{ rule.label }}</strong>
              <em v-if="rule.short !== rule.label">{{ rule.short }}</em>
            </span>
            <span class="tag-go" aria-hidden="true">→</span>
          </router-link>
        </div>
      </section>
    </main>

    <footer class="foot">
      <span>Salasasa · Mahjong Library</span>
      <router-link to="/rule-research">规则搜集归档</router-link>
      <router-link to="/">返回主站</router-link>
    </footer>
  </div>
</template>

<script setup>
import { LIBRARY_SECTIONS, rulesForSection } from '@/constants/libraryRules'

const sections = LIBRARY_SECTIONS.map((s) => ({
  ...s,
  nav: ({
    platform: '平台',
    mil: 'MIL',
    local: '地方',
    custom: '自制',
  })[s.key] || s.title,
}))

function scrollTo(key) {
  const el = document.getElementById(`sec-${key}`)
  if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=Noto+Serif+SC:wght@600;700&display=swap');

.lib {
  --paper: #dfeae4;
  --paper-deep: #d2e0d8;
  --ink: #102820;
  --ink-soft: #3d5a4c;
  --line: rgba(16, 40, 32, 0.14);
  --accent: #1f6b52;
  --mark: #c45c3a;
  position: relative;
  min-height: 100vh;
  color: var(--ink);
  background: var(--paper);
  font-family: 'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', sans-serif;
  overflow-x: hidden;
}

.lib-bg {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background:
    radial-gradient(ellipse 80% 50% at 10% -10%, rgba(31, 107, 82, 0.18), transparent 55%),
    radial-gradient(ellipse 60% 40% at 100% 20%, rgba(196, 92, 58, 0.1), transparent 50%),
    linear-gradient(180deg, #e8f1eb 0%, var(--paper) 35%, #d8e6de 100%);
}

.grain {
  position: absolute;
  inset: 0;
  opacity: 0.35;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='0.45'/%3E%3C/svg%3E");
  mix-blend-mode: multiply;
}

.rail {
  position: absolute;
  top: 0;
  bottom: 0;
  left: clamp(12px, 4vw, 40px);
  width: 1px;
  background: linear-gradient(180deg, transparent, var(--line) 12%, var(--line) 88%, transparent);
}

.hero,
.catalog,
.foot {
  position: relative;
  z-index: 1;
  max-width: 920px;
  margin: 0 auto;
  padding-left: clamp(28px, 6vw, 56px);
  padding-right: clamp(18px, 4vw, 32px);
}

.hero {
  padding-top: clamp(48px, 10vh, 96px);
  padding-bottom: 28px;
  animation: rise 0.7s ease both;
}

.eyebrow {
  margin: 0 0 10px;
  font-size: 11px;
  letter-spacing: 0.28em;
  text-transform: uppercase;
  color: var(--accent);
  font-weight: 600;
}

.brand {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: clamp(2.4rem, 7vw, 3.6rem);
  font-weight: 700;
  line-height: 1.05;
  letter-spacing: 0.04em;
  color: var(--ink);
}

.lede {
  margin: 14px 0 0;
  max-width: 28em;
  font-size: 15px;
  line-height: 1.65;
  color: var(--ink-soft);
}

.hero-nav {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 22px;
}

.jump {
  display: inline-flex;
  align-items: center;
  padding: 6px 12px;
  border: 1px solid var(--line);
  background: rgba(255, 255, 255, 0.35);
  color: var(--ink);
  text-decoration: none;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.06em;
  transition: background 0.15s ease, border-color 0.15s ease, transform 0.15s ease;
}

.jump:hover {
  background: rgba(255, 255, 255, 0.7);
  border-color: var(--accent);
  transform: translateY(-1px);
}

.catalog {
  padding-bottom: 48px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.shelf {
  display: grid;
  grid-template-columns: minmax(140px, 200px) 1fr;
  gap: 14px 20px;
  padding: 16px 0 18px;
  border-top: 1px solid var(--line);
  animation: rise 0.65s ease both;
  animation-delay: var(--delay);
}

.shelf-meta {
  display: flex;
  gap: 10px;
  align-items: flex-start;
}

.idx {
  font-family: 'IBM Plex Sans', monospace;
  font-size: 12px;
  font-weight: 600;
  color: var(--mark);
  letter-spacing: 0.08em;
  padding-top: 3px;
}

.shelf-meta h2 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: 0.06em;
}

.shelf-meta p {
  margin: 4px 0 0;
  font-size: 12px;
  line-height: 1.45;
  color: var(--ink-soft);
}

.tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  align-content: flex-start;
}

.tag {
  --ink: var(--accent);
  display: inline-flex;
  align-items: center;
  gap: 8px;
  min-height: 38px;
  padding: 6px 10px 6px 8px;
  text-decoration: none;
  color: inherit;
  background: rgba(255, 255, 255, 0.55);
  border: 1px solid var(--line);
  border-left: 3px solid var(--ink);
  transition: transform 0.15s ease, background 0.15s ease, box-shadow 0.15s ease;
}

.tag:hover {
  background: #fff;
  transform: translateY(-2px);
  box-shadow: 0 8px 18px rgba(16, 40, 32, 0.08);
}

.tag-mark {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  background: var(--ink);
  flex-shrink: 0;
}

.tag-text {
  display: flex;
  flex-direction: column;
  gap: 1px;
  min-width: 0;
}

.tag-text strong {
  font-size: 13px;
  font-weight: 600;
  line-height: 1.2;
}

.tag-text em {
  font-style: normal;
  font-size: 10px;
  color: var(--ink-soft);
  letter-spacing: 0.04em;
}

.tag-go {
  margin-left: 4px;
  font-size: 12px;
  color: var(--ink-soft);
  opacity: 0;
  transform: translateX(-4px);
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.tag:hover .tag-go {
  opacity: 1;
  transform: translateX(0);
}

.foot {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding-top: 18px;
  padding-bottom: 28px;
  border-top: 1px solid var(--line);
  font-size: 12px;
  color: var(--ink-soft);
}

.foot a {
  color: var(--accent);
  text-decoration: none;
  font-weight: 600;
}

.foot a:hover {
  text-decoration: underline;
}

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(12px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@media (max-width: 720px) {
  .shelf {
    grid-template-columns: 1fr;
    gap: 10px;
  }

  .rail {
    left: 14px;
  }
}
</style>
