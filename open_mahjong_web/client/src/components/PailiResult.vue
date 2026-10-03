<template>
        <template v-if="result && result.mode === 'shanten'">
          <div class="meta-line nowrap">
            <span>当前向听 <strong>{{ formatShanten(result.shanten) }}</strong></span>
          </div>
          <div class="banner success nowrap">
            <strong>{{ result.is_tingpai ? '听牌' : formatShanten(result.shanten) }}</strong>
            <span class="banner-sub">进张 {{ result.total_accept }} 张 · {{ result.accept.length }} 种</span>
          </div>
          <div class="paili-block">
            <h4>进张</h4>
            <div class="accept-list nowrap-scroll">
              <button v-for="a in result.accept" :key="'a-' + a.tile" type="button" class="accept-inline tile-action" :aria-label="`摸${TILE_NAME[a.tile]}`" :title="`摸${TILE_NAME[a.tile]}`" @click="$emit('draw', a.tile)">
                <TileMiniGlyph :tile-id="a.tile" /><span class="accept-count">{{ a.remaining }}</span>
              </button>
              <span v-if="result.accept.length === 0" class="hint">已和牌或无进张</span>
            </div>
          </div>
        </template>
        <template v-else-if="result && result.mode === 'discard'">
          <div class="meta-line nowrap">
            <span>最佳向听 <strong>{{ formatShanten(result.best_shanten) }}</strong></span>
          </div>
          <div class="discard-table">
            <div class="discard-row discard-head nowrap">
              <span class="col-tile">切</span>
              <span class="col-shanten">向听</span>
              <span class="col-total">进张</span>
              <span class="col-accept-h">摸</span>
            </div>
            <div
              v-for="d in result.discards"
              :key="'d-' + d.discard"
              class="discard-row nowrap"
              :class="{ 'is-best': d.shanten === result.best_shanten }"
            >
              <span class="col-tile"><button type="button" class="tile-action" :aria-label="`切${TILE_NAME[d.discard]}`" :title="`切${TILE_NAME[d.discard]}`" @click="$emit('discard', d.discard)"><TileMiniGlyph :tile-id="d.discard" /></button></span>
              <span class="col-shanten">{{ formatShanten(d.shanten) }}</span>
              <span class="col-total">
                <strong>{{ d.total_accept }}</strong><span class="hint">/{{ d.accept.length }}</span>
              </span>
              <span class="col-accept">
                <span v-if="d.accept.length === 0" class="hint">无</span>
                <span v-else class="accept-inline-row nowrap-scroll">
                  <button v-for="a in d.accept" :key="'da-' + d.discard + '-' + a.tile" type="button" class="accept-inline tile-action" :aria-label="`切${TILE_NAME[d.discard]}，摸${TILE_NAME[a.tile]}`" :title="`切${TILE_NAME[d.discard]}，摸${TILE_NAME[a.tile]}`" @click="$emit('draw', a.tile, d.discard)">
                    <TileMiniGlyph :tile-id="a.tile" /><span class="accept-count-mini">{{ a.remaining }}</span>
                  </button>
                </span>
              </span>
            </div>
          </div>
        </template>
</template>
<script setup>
import TileMiniGlyph from './TileMiniGlyph.vue'
import { TILE_NAME } from '../composables/useMahjongTiles.js'
defineProps({ result: { type: Object, required: true } })
defineEmits(['draw', 'discard'])
const formatShanten = s => s === -1 ? '和牌' : s === 0 ? '听牌' : `${s} 向听`
</script>
<style scoped>
.hint {
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
}
.hint.warn { color: var(--omu-warning, #d97706); }

.result-embed {
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: #fafbfc;
  min-height: 48px;
}

.result-embed .empty {
  padding: 10px 12px;
  text-align: center;
  color: var(--omu-text-muted, #94a3b8);
  font-size: 12.5px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  min-height: 44px;
}

.meta-line {
  padding: 8px 12px 0;
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
}
.meta-line strong { color: var(--omu-accent, #409eff); margin-left: 4px; }

.actions {
  padding: 8px 12px;
  border-top: 1px solid var(--omu-border, #ebeef5);
  background: var(--omu-surface-soft, #f5f7fa);
  text-align: right;
}

.banner {
  margin: 8px 12px;
  padding: 10px 12px;
  border-radius: 8px;
  display: flex;
  align-items: baseline;
  gap: 10px;
  border: 1px solid;
  background: #ecfdf5;
  border-color: #6ee7b7;
  color: #065f46;
}
.banner strong { font-size: 0.95rem; }
.banner-sub { font-size: 12px; opacity: 0.85; }

.paili-block { padding: 0 12px 10px; }
.paili-block h4 {
  margin: 6px 0;
  font-size: 12.5px;
  color: var(--omu-text-soft, #475569);
  letter-spacing: 0.5px;
}

.accept-list {
  padding: 4px 6px;
  background: var(--omu-surface-soft, #f5f7fa);
  border-radius: 6px;
  border: 1px dashed var(--omu-border, #ebeef5);
}

.nowrap { white-space: nowrap; }
.nowrap-scroll {
  white-space: nowrap;
  overflow-x: auto;
  max-width: 100%;
  padding-bottom: 2px;
}

.accept-inline {
  display: inline-flex;
  align-items: baseline;
  gap: 1px;
  margin-right: 6px;
}
.accept-inline .accept-count {
  font-size: 10px;
  color: var(--omu-text-soft, #475569);
  font-family: var(--omu-mono, 'Consolas', monospace);
}

.accept-count-mini {
  font-size: 9px;
  color: var(--omu-text-muted, #94a3b8);
  font-family: var(--omu-mono, 'Consolas', monospace);
  margin-right: 4px;
}

.discard-table {
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
  font-size: 12px;
}

.discard-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 10px;
  border-bottom: 1px solid var(--omu-border, #ebeef5);
  min-height: 28px;
}

.discard-row:last-child { border-bottom: none; }

.discard-head {
  background: var(--omu-surface-soft, #f5f7fa);
  font-size: 11px;
  letter-spacing: 0.5px;
  color: var(--omu-text-soft, #475569);
  font-weight: 600;
}

.discard-row.is-best {
  background: #f0f9ff;
}
.discard-row.is-best .col-shanten { color: var(--omu-accent, #409eff); font-weight: 600; }

.col-tile { flex: 0 0 auto; }
.col-shanten {
  flex: 0 0 64px;
  font-family: var(--omu-mono, 'Consolas', monospace);
  color: var(--omu-text, #1f2933);
}
.col-total {
  flex: 0 0 68px;
  display: inline-flex;
  align-items: baseline;
  gap: 2px;
  font-family: var(--omu-mono, 'Consolas', monospace);
}
.col-total strong { color: var(--omu-text, #1f2933); font-size: 0.9rem; }

.col-accept-h { flex: 1 1 0; min-width: 0; }
.col-accept {
  flex: 1 1 0;
  min-width: 0;
  overflow: hidden;
}

.accept-inline-row {
  display: inline-block;
  vertical-align: middle;
}

.tile-action { appearance: none; background: transparent; border: 0; padding: 0; color: inherit; font: inherit; cursor: pointer; }
.tile-action:focus-visible { outline: 2px solid var(--omu-accent, #409eff); outline-offset: 2px; }
</style>
