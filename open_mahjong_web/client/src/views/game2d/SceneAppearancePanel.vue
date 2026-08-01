<template>
  <div class="scene-appearance-panel">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">游戏设置</h3>
      <button type="button" class="scene-appearance-panel__ghost-button" @click="$emit('reset')">重置</button>
    </div>

    <section class="scene-appearance-panel__section">
      <h4 class="scene-appearance-panel__section-title">界面</h4>
      <label v-if="showInterfaceTheme" class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">侧边界面</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.interfaceTheme"
          @change="$emit('interface-theme', $event.target.value)"
        >
          <option value="light">浅色</option>
          <option value="dark">深色</option>
        </select>
      </label>
      <label v-if="locale !== 'en'" class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">局数显示</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.roundLabelFormat"
          @change="$emit('round-label-format', $event.target.value)"
        >
          <option value="wind-seat">东风东</option>
          <option value="round-number">东一局</option>
        </select>
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">中文字体</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.fontTheme"
          @change="$emit('font-theme', $event.target.value)"
        >
          <option value="arphic-ukai">AR PL</option>
          <option value="system-kaiti">系统楷体</option>
          <option value="source-serif">思源宋体</option>
          <option value="system-default">系统默认</option>
        </select>
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">英文字体</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.latinFontTheme"
          @change="$emit('latin-font-theme', $event.target.value)"
        >
          <option value="latin-modern">Latin Modern</option>
          <option value="noto-sans-latin">Noto Sans</option>
          <option value="noto-serif-latin">Noto Serif</option>
        </select>
      </label>

      <div class="scene-appearance-panel__field scene-appearance-panel__field--stacked">
        <span class="scene-appearance-panel__label">音量</span>
        <div class="scene-appearance-panel__range-row">
          <input
            class="scene-appearance-panel__range"
            type="range"
            min="0"
            max="100"
            :value="Math.round(volume * 100)"
            @input="$emit('volume', Number($event.target.value) / 100)"
          >
          <span class="scene-appearance-panel__value">{{ Math.round(volume * 100) }}%</span>
        </div>
      </div>
    </section>

    <section class="scene-appearance-panel__section">
      <h4 class="scene-appearance-panel__section-title">桌布</h4>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">牌桌内背景</span>
        <input type="color" :value="appearance.backgroundColorTable" @input="emitColor('table-color', $event)">
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">牌桌外背景</span>
        <input type="color" :value="appearance.backgroundColorOutside" @input="emitColor('outside-color', $event)">
      </label>
      <label class="scene-appearance-panel__field scene-appearance-panel__field--toggle">
        <span class="scene-appearance-panel__label">启用本地图像</span>
        <input
          type="checkbox"
          :checked="appearance.backgroundImageEnabled"
          @change="$emit('image-enabled', $event.target.checked)"
        >
      </label>
      <div class="scene-appearance-panel__field scene-appearance-panel__field--stacked">
        <span class="scene-appearance-panel__label">图像不透明度</span>
        <div class="scene-appearance-panel__range-row">
          <input
            class="scene-appearance-panel__range"
            type="range"
            min="0"
            max="100"
            :value="Math.round(appearance.backgroundImageAlpha * 100)"
            :disabled="!backgroundImageName"
            @input="$emit('image-alpha', Number($event.target.value) / 100)"
          >
          <span class="scene-appearance-panel__value">{{ Math.round(appearance.backgroundImageAlpha * 100) }}%</span>
        </div>
      </div>
      <div class="scene-appearance-panel__button-row">
        <button type="button" class="scene-appearance-panel__button" @click="fileInput?.click()">选择图片</button>
        <button
          type="button"
          class="scene-appearance-panel__button"
          :disabled="!backgroundImageName"
          @click="$emit('image-cleared')"
        >
          移除图片
        </button>
        <input ref="fileInput" type="file" accept="image/*" hidden @change="selectImage">
      </div>
      <div class="scene-appearance-panel__hint">
        {{ backgroundImageLoading ? '正在读取已保存图片…' : backgroundImageName ? `已保存图片：${backgroundImageName}` : '未选择背景图片' }}
      </div>
    </section>

    <section class="scene-appearance-panel__section">
      <h4 class="scene-appearance-panel__section-title">牌面</h4>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">牌面样式</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.tileFaceTheme"
          @change="$emit('tile-face-theme', $event.target.value)"
        >
          <option value="regular">标准白色</option>
          <option value="black">FluffyStuff 黑色</option>
        </select>
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">花牌样式</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.flowerFaceTheme"
          @change="$emit('flower-face-theme', $event.target.value)"
        >
          <option value="unity">Unity 样式</option>
          <option value="flat">平面文字样式</option>
        </select>
      </label>
    </section>

    <section class="scene-appearance-panel__section">
      <h4 class="scene-appearance-panel__section-title">牌背</h4>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">轮换方式</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.tileCoverRotateMode"
          @change="$emit('cover-rotate-mode', $event.target.value)"
        >
          <option value="cycle">循环</option>
          <option value="random">随机</option>
          <option value="random-no-repeat">随机（两局不重复）</option>
        </select>
      </label>

      <div class="tile-cover-sorter" aria-label="牌背排列框">
        <article
          v-for="(color, index) in appearance.tileCoverColors"
          :key="`${color}-${index}`"
          class="tile-cover-card"
          role="button"
          tabindex="0"
          draggable="true"
          :aria-label="`使用或拖动第 ${index + 1} 个牌背`"
          :aria-pressed="appearance.lastTileCoverIndex === index"
          :class="{
            'is-active': appearance.lastTileCoverIndex === index,
            'is-dragging': draggingCoverIndex === index,
            'is-drop-target': dropCoverIndex === index && draggingCoverIndex !== index,
          }"
          @click="$emit('select-cover-index', index)"
          @keydown.enter.prevent="$emit('select-cover-index', index)"
          @keydown.space.prevent="$emit('select-cover-index', index)"
          @dragstart="startCoverDrag(index, $event)"
          @dragend="finishCoverDrag"
          @dragover.prevent="dropCoverIndex = index"
          @dragleave="clearDropTarget(index)"
          @drop.prevent="dropCover(index)"
        >
          <span
            class="tile-cover-card__drag"
            aria-hidden="true"
          >
            ⠿
          </span>
          <label class="tile-cover-card__color">
            <input
              type="color"
              :value="color"
              :aria-label="`修改第 ${index + 1} 个牌背颜色`"
              @input="emitCoverColor(index, $event)"
            >
          </label>
          <button
            type="button"
            class="tile-cover-card__remove"
            :disabled="appearance.tileCoverColors.length <= 1"
            :aria-label="`删除第 ${index + 1} 个牌背`"
            @click.stop="$emit('remove-cover-color', index)"
          >
            ×
          </button>
        </article>

        <button
          type="button"
          class="tile-cover-card tile-cover-card--add"
          :disabled="appearance.tileCoverColors.length >= 8"
          aria-label="添加牌背"
          @click="$emit('add-cover-color')"
        >
          <span aria-hidden="true">＋</span>
          <span>添加牌背</span>
        </button>
      </div>
      <p class="scene-appearance-panel__hint">拖动牌背调整排列顺序，最多添加 8 个。</p>
    </section>

    <section class="scene-appearance-panel__section">
      <h4 class="scene-appearance-panel__section-title">补花区</h4>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">底框显示</span>
        <select
          class="scene-appearance-panel__select"
          :value="appearance.flowerAreaDisplay"
          @change="$emit('flower-area-display', $event.target.value)"
        >
          <option value="always">始终显示</option>
          <option value="when-present">有花牌时显示</option>
          <option value="never">固定不显示</option>
        </select>
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">底框颜色</span>
        <input type="color" :value="appearance.flowerAreaColor" @input="$emit('flower-area-color', $event.target.value)">
      </label>
      <div class="scene-appearance-panel__field scene-appearance-panel__field--stacked">
        <span class="scene-appearance-panel__label">底框透明度</span>
        <div class="scene-appearance-panel__range-row">
          <input
            class="scene-appearance-panel__range"
            type="range"
            min="0"
            max="100"
            :value="Math.round(appearance.flowerAreaAlpha * 100)"
            @input="$emit('flower-area-alpha', Number($event.target.value) / 100)"
          >
          <span class="scene-appearance-panel__value">{{ Math.round(appearance.flowerAreaAlpha * 100) }}%</span>
        </div>
      </div>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">文字颜色</span>
        <input type="color" :value="appearance.flowerAreaLabelColor" @input="$emit('flower-area-label-color', $event.target.value)">
      </label>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">数字强调色</span>
        <input type="color" :value="appearance.flowerAreaCountColor" @input="$emit('flower-area-count-color', $event.target.value)">
      </label>
      <div class="scene-appearance-panel__field scene-appearance-panel__field--stacked">
        <span class="scene-appearance-panel__label">玩家名大小</span>
        <div class="scene-appearance-panel__range-row">
          <input
            class="scene-appearance-panel__range"
            type="range"
            min="50"
            max="180"
            step="5"
            :value="Math.round(appearance.flowerAreaLabelScale * 100)"
            @input="$emit('flower-area-label-scale', Number($event.target.value) / 100)"
          >
          <span class="scene-appearance-panel__value">{{ Math.round(appearance.flowerAreaLabelScale * 100) }}%</span>
        </div>
      </div>
    </section>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { locale } from '@/i18n'

const props = defineProps({
  appearance: { type: Object, required: true },
  backgroundImageName: { type: String, default: null },
  backgroundImageLoading: { type: Boolean, default: false },
  volume: { type: Number, required: true },
  showInterfaceTheme: { type: Boolean, default: false },
})

const emit = defineEmits([
  'reset', 'table-color', 'outside-color', 'image-enabled', 'image-alpha',
  'image-selected', 'image-cleared', 'cover-color', 'add-cover-color',
  'remove-cover-color', 'reorder-cover-colors', 'select-cover-index', 'cover-rotate-mode',
  'flower-area-display', 'flower-area-color',
  'flower-area-alpha', 'flower-area-label-color', 'flower-area-count-color', 'flower-area-label-scale',
  'tile-face-theme', 'flower-face-theme', 'font-theme', 'latin-font-theme', 'interface-theme',
  'round-label-format', 'volume',
])

const fileInput = ref(null)
const draggingCoverIndex = ref(null)
const dropCoverIndex = ref(null)

function emitColor(name, event) {
  emit(name, event.target.value)
}

function emitCoverColor(index, event) {
  emit('cover-color', index, event.target.value)
}

function startCoverDrag(index, event) {
  draggingCoverIndex.value = index
  dropCoverIndex.value = index
  if (event.dataTransfer) {
    event.dataTransfer.effectAllowed = 'move'
    event.dataTransfer.setData('text/plain', String(index))
  }
}

function clearDropTarget(index) {
  if (dropCoverIndex.value === index) dropCoverIndex.value = null
}

function dropCover(targetIndex) {
  const sourceIndex = draggingCoverIndex.value
  finishCoverDrag()
  if (sourceIndex == null || sourceIndex === targetIndex) return
  const entries = props.appearance.tileCoverColors.map((color, originalIndex) => ({ color, originalIndex }))
  const [moved] = entries.splice(sourceIndex, 1)
  entries.splice(targetIndex, 0, moved)
  const activeIndex = entries.findIndex((entry) => entry.originalIndex === props.appearance.lastTileCoverIndex)
  emit('reorder-cover-colors', entries.map((entry) => entry.color), Math.max(0, activeIndex))
}

function finishCoverDrag() {
  draggingCoverIndex.value = null
  dropCoverIndex.value = null
}

function selectImage(event) {
  const file = event.target.files?.[0]
  if (file) emit('image-selected', file)
  event.target.value = ''
}
</script>
