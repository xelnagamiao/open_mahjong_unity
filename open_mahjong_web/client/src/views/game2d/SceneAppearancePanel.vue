<template>
  <div class="scene-appearance-panel">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">外观</h3>
      <button type="button" class="scene-appearance-panel__ghost-button" @click="$emit('reset')">重置</button>
    </div>

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

    <div class="scene-appearance-panel__field">
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

    <div class="scene-appearance-panel__section">
      <div class="scene-appearance-panel__section-header">
        <span class="scene-appearance-panel__label">牌背覆盖色</span>
        <button
          type="button"
          class="scene-appearance-panel__ghost-button"
          :disabled="appearance.tileCoverColors.length >= 8"
          @click="$emit('add-cover-color')"
        >
          添加颜色
        </button>
      </div>
      <label class="scene-appearance-panel__field">
        <span class="scene-appearance-panel__label">牌背轮换</span>
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
      <p class="scene-appearance-panel__hint">最多 8 种颜色；按局切换牌背覆盖色。</p>
      <div class="scene-appearance-panel__swatch-list">
        <div v-for="(color, index) in appearance.tileCoverColors" :key="index" class="scene-appearance-panel__swatch-row">
          <input type="color" :value="color" @input="emitCoverColor(index, $event)">
          <span class="scene-appearance-panel__swatch-code">{{ color.toUpperCase() }}</span>
          <button
            type="button"
            class="scene-appearance-panel__ghost-button"
            :disabled="appearance.tileCoverColors.length <= 1"
            @click="$emit('remove-cover-color', index)"
          >
            删除
          </button>
        </div>
      </div>
    </div>

    <label class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">摸切快捷</span>
      <select
        class="scene-appearance-panel__select"
        :value="appearance.moqieShortcutMode"
        @change="$emit('moqie-shortcut', Number($event.target.value))"
      >
        <option :value="0">双击摸切</option>
        <option :value="1">右键摸切</option>
        <option :value="2">无快捷键</option>
      </select>
    </label>

    <label class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">过牌快捷</span>
      <select
        class="scene-appearance-panel__select"
        :value="appearance.passShortcutMode"
        @change="$emit('pass-shortcut', Number($event.target.value))"
      >
        <option :value="0">右键取消</option>
        <option :value="1">双击取消</option>
        <option :value="2">无快捷键</option>
      </select>
    </label>

    <label class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">补花区底框</span>
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
      <span class="scene-appearance-panel__label">补花区颜色</span>
      <input
        type="color"
        :value="appearance.flowerAreaColor"
        @input="$emit('flower-area-color', $event.target.value)"
      >
    </label>

    <div class="scene-appearance-panel__field">
      <span class="scene-appearance-panel__label">补花区透明度</span>
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
        <option value="flat">当前平面花牌</option>
        <option value="unity">Unity 原版花牌</option>
      </select>
    </label>

    <div class="scene-appearance-panel__field">
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
  </div>
</template>

<script setup>
import { ref } from 'vue'

defineProps({
  appearance: { type: Object, required: true },
  backgroundImageName: { type: String, default: null },
  backgroundImageLoading: { type: Boolean, default: false },
  volume: { type: Number, required: true },
})

const emit = defineEmits([
  'reset', 'table-color', 'outside-color', 'image-enabled', 'image-alpha',
  'image-selected', 'image-cleared', 'cover-color', 'add-cover-color',
  'remove-cover-color', 'cover-rotate-mode', 'moqie-shortcut', 'pass-shortcut',
  'flower-area-display', 'flower-area-color',
  'flower-area-alpha', 'tile-face-theme', 'flower-face-theme', 'volume',
])
const fileInput = ref(null)

function emitColor(name, event) {
  emit(name, event.target.value)
}

function emitCoverColor(index, event) {
  emit('cover-color', index, event.target.value)
}

function selectImage(event) {
  const file = event.target.files?.[0]
  if (file) emit('image-selected', file)
  event.target.value = ''
}
</script>
