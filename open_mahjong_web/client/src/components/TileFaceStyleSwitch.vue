<template>
  <div class="face-style-switch" role="group" aria-label="牌面风格">
    <button
      type="button"
      class="face-btn"
      :class="{ on: tileFaceStyle === TILE_FACE_STYLE.helper }"
      @click="setTileFaceStyle(TILE_FACE_STYLE.helper)"
    >
      小助手
    </button>
    <button
      type="button"
      class="face-btn"
      :class="{ on: tileFaceStyle === TILE_FACE_STYLE.oblique }"
      @click="setTileFaceStyle(TILE_FACE_STYLE.oblique)"
    >
      立体
    </button>
  </div>
</template>

<script setup>
import { useTileFaceStyle, TILE_FACE_STYLE } from '@/composables/useTileFaceStyle'

const { tileFaceStyle, setTileFaceStyle } = useTileFaceStyle()
</script>

<style scoped>
.face-style-switch {
  display: inline-flex;
  border: 1px solid var(--omu-border, #dcdfe6);
  border-radius: 6px;
  overflow: hidden;
  background: #fff;
}

.face-btn {
  border: none;
  background: transparent;
  padding: 2px 8px;
  font-size: 12px;
  line-height: 1.6;
  color: var(--omu-text-soft, #606266);
  cursor: pointer;
}

.face-btn + .face-btn {
  border-left: 1px solid var(--omu-border, #dcdfe6);
}

.face-btn.on {
  background: #ecf5ff;
  color: #409eff;
  font-weight: 600;
}

.face-btn:hover:not(.on) {
  background: #f5f7fa;
}
</style>
