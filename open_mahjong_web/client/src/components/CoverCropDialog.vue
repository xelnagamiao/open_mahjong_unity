<template>
  <el-dialog
    :model-value="modelValue"
    title="裁切标题图片"
    width="760px"
    :close-on-click-modal="false"
    @close="onCancel"
  >
    <p class="crop-hint">拖动图片调整位置，滚轮或滑块缩放。裁切比例与侧栏封面一致（约 2.2:1）。</p>
    <div
      ref="stageRef"
      class="crop-stage"
      @pointerdown="onDown"
      @pointermove="onMove"
      @pointerup="onUp"
      @pointerleave="onUp"
      @wheel.prevent="onWheel"
    >
      <img
        v-if="src"
        class="crop-image"
        :src="src"
        alt=""
        draggable="false"
        :style="imageStyle"
      />
      <div class="crop-window" :style="windowStyle" />
    </div>
    <div class="crop-zoom">
      <span>缩放</span>
      <el-slider v-model="zoom" :min="1" :max="4" :step="0.02" />
    </div>
    <template #footer>
      <el-button @click="onCancel">取消</el-button>
      <el-button type="primary" :loading="busy" @click="confirm">确认裁切并上传</el-button>
    </template>
  </el-dialog>
</template>

<script setup>
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'

const ASPECT = 324 / 148
const OUT_W = 648
const OUT_H = 296
const STAGE_W = 720
const STAGE_H = 400

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  file: { type: File, default: null },
})

const emit = defineEmits(['update:modelValue', 'confirm'])

const src = ref('')
const natural = ref({ w: 1, h: 1 })
const zoom = ref(1)
const imgX = ref(0)
const imgY = ref(0)
const dragging = ref(false)
const last = ref({ x: 0, y: 0 })
const busy = ref(false)
const stageRef = ref(null)

const stageW = ref(STAGE_W)

const cropBox = computed(() => {
  const stageWidth = stageW.value || STAGE_W
  let width = stageWidth * 0.86
  let height = width / ASPECT
  if (height > STAGE_H * 0.86) {
    height = STAGE_H * 0.86
    width = height * ASPECT
  }
  return {
    w: width,
    h: height,
    x: (stageWidth - width) / 2,
    y: (STAGE_H - height) / 2,
  }
})

const baseScale = computed(() => {
  const box = cropBox.value
  const { w, h } = natural.value
  return Math.max(box.w / w, box.h / h)
})

const drawScale = computed(() => baseScale.value * zoom.value)

const imageStyle = computed(() => ({
  width: `${natural.value.w * drawScale.value}px`,
  height: `${natural.value.h * drawScale.value}px`,
  transform: `translate(${imgX.value}px, ${imgY.value}px)`,
}))

const windowStyle = computed(() => {
  const box = cropBox.value
  return {
    width: `${box.w}px`,
    height: `${box.h}px`,
    left: `${box.x}px`,
    top: `${box.y}px`,
  }
})

function clampOffset() {
  const box = cropBox.value
  const dw = natural.value.w * drawScale.value
  const dh = natural.value.h * drawScale.value
  const minX = box.x + box.w - dw
  const minY = box.y + box.h - dh
  const maxX = box.x
  const maxY = box.y
  imgX.value = Math.min(maxX, Math.max(minX, imgX.value))
  imgY.value = Math.min(maxY, Math.max(minY, imgY.value))
}

function fitCover() {
  zoom.value = 1
  const box = cropBox.value
  const dw = natural.value.w * baseScale.value
  const dh = natural.value.h * baseScale.value
  imgX.value = box.x - (dw - box.w) / 2
  imgY.value = box.y - (dh - box.h) / 2
  clampOffset()
}

watch(
  () => props.file,
  (file) => {
    if (src.value) URL.revokeObjectURL(src.value)
    src.value = ''
    if (!file) return
    const url = URL.createObjectURL(file)
    src.value = url
    const image = new Image()
    image.onload = () => {
      natural.value = { w: image.naturalWidth || 1, h: image.naturalHeight || 1 }
      fitCover()
    }
    image.src = url
  },
  { immediate: true }
)

watch(
  () => props.modelValue,
  async (open) => {
    if (!open) return
    await nextTick()
    if (stageRef.value?.clientWidth) stageW.value = stageRef.value.clientWidth
    fitCover()
  }
)

watch(zoom, () => clampOffset())

function onDown(event) {
  dragging.value = true
  last.value = { x: event.clientX, y: event.clientY }
  event.currentTarget.setPointerCapture?.(event.pointerId)
}

function onMove(event) {
  if (!dragging.value) return
  imgX.value += event.clientX - last.value.x
  imgY.value += event.clientY - last.value.y
  last.value = { x: event.clientX, y: event.clientY }
  clampOffset()
}

function onUp() {
  dragging.value = false
}

function onWheel(event) {
  const next = Math.min(4, Math.max(1, zoom.value + (event.deltaY > 0 ? -0.08 : 0.08)))
  const box = cropBox.value
  const cx = box.x + box.w / 2
  const cy = box.y + box.h / 2
  const before = drawScale.value
  zoom.value = next
  const after = baseScale.value * zoom.value
  if (before > 0) {
    imgX.value = cx - ((cx - imgX.value) * after) / before
    imgY.value = cy - ((cy - imgY.value) * after) / before
  }
  clampOffset()
}

function onCancel() {
  emit('update:modelValue', false)
}

async function confirm() {
  if (!src.value) return
  busy.value = true
  try {
    const box = cropBox.value
    const scale = drawScale.value
    const sx = (box.x - imgX.value) / scale
    const sy = (box.y - imgY.value) / scale
    const sw = box.w / scale
    const sh = box.h / scale
    const canvas = document.createElement('canvas')
    canvas.width = OUT_W
    canvas.height = OUT_H
    const ctx = canvas.getContext('2d')
    const image = new Image()
    await new Promise((resolve, reject) => {
      image.onload = resolve
      image.onerror = reject
      image.src = src.value
    })
    ctx.drawImage(image, sx, sy, sw, sh, 0, 0, OUT_W, OUT_H)
    const blob = await new Promise((resolve) => {
      canvas.toBlob((out) => resolve(out), 'image/jpeg', 0.92)
    })
    if (!blob) throw new Error('裁切失败')
    emit('confirm', new File([blob], 'cover.jpg', { type: 'image/jpeg' }))
    emit('update:modelValue', false)
  } catch (err) {
    ElMessage.error(err.message || '裁切失败')
  } finally {
    busy.value = false
  }
}

onBeforeUnmount(() => {
  if (src.value) URL.revokeObjectURL(src.value)
})
</script>

<style scoped>
.crop-hint {
  margin: 0 0 10px;
  color: #909399;
  font-size: 13px;
}
.crop-stage {
  position: relative;
  width: min(720px, 100%);
  height: 400px;
  margin: 0 auto;
  overflow: hidden;
  background: #111;
  border-radius: 8px;
  touch-action: none;
  user-select: none;
  cursor: grab;
}
.crop-image {
  position: absolute;
  left: 0;
  top: 0;
  max-width: none;
  pointer-events: none;
}
.crop-window {
  position: absolute;
  box-sizing: border-box;
  border: 2px solid #fde68a;
  box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.55);
  pointer-events: none;
}
.crop-zoom {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-top: 12px;
  padding: 0 8px;
}
.crop-zoom span {
  color: #606266;
  font-size: 13px;
  width: 36px;
}
.crop-zoom :deep(.el-slider) {
  flex: 1;
}
</style>
