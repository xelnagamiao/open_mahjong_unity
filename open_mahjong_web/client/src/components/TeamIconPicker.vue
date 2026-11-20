<template>
  <div class="team-icon-picker">
    <img v-if="modelValue" :src="modelValue" alt="队徽预览" />
    <input ref="input" type="file" accept="image/png,image/jpeg" :disabled="disabled || busy" @change="pick" />
    <el-button v-if="modelValue" :disabled="disabled || busy" @click="emit('update:modelValue', '')">移除队徽</el-button>
    <span>PNG/JPEG，自动缩小至 256 像素。</span>
  </div>
</template>
<script setup>
import { ref, onBeforeUnmount } from 'vue'
import { ElMessage } from 'element-plus'
const props = defineProps({ modelValue: { type: String, default: '' }, disabled: Boolean })
const emit = defineEmits(['update:modelValue', 'busy'])
const busy = ref(false)
let mounted = true
onBeforeUnmount(() => { mounted = false; emit('busy', false) })
async function pick(event) {
  const file = event.target.files?.[0]
  event.target.value = ''
  if (!file || props.disabled) return
  if (!['image/png', 'image/jpeg'].includes(file.type) || file.size > 8 * 1024 * 1024) return ElMessage.warning('请选择 8MB 以内的 PNG/JPEG 图片')
  busy.value = true
  emit('busy', true)
  let bitmap
  try {
    bitmap = await createImageBitmap(file)
    const scale = Math.min(1, 256 / Math.max(bitmap.width, bitmap.height))
    const canvas = document.createElement('canvas')
    canvas.width = Math.max(1, Math.round(bitmap.width * scale)); canvas.height = Math.max(1, Math.round(bitmap.height * scale))
    canvas.getContext('2d').drawImage(bitmap, 0, 0, canvas.width, canvas.height)
    let value = canvas.toDataURL('image/png')
    if (value.length > 87000) {
      const smaller = document.createElement('canvas'); smaller.width = Math.max(1, canvas.width >> 1); smaller.height = Math.max(1, canvas.height >> 1)
      smaller.getContext('2d').drawImage(canvas, 0, 0, smaller.width, smaller.height)
      value = smaller.toDataURL('image/png')
    }
    if (value.length > 87000) throw new Error('图片过于复杂，请选择较简单的队徽')
    if (mounted && !props.disabled) emit('update:modelValue', value)
  } catch (e) { ElMessage.error(e.message || '图片无法读取') }
  finally { bitmap?.close(); busy.value = false; emit('busy', false) }
}
</script>
<style scoped>
.team-icon-picker { display:flex; align-items:center; flex-wrap:wrap; gap:12px; }
.team-icon-picker img { width:88px; height:88px; object-fit:contain; border:1px solid #dfe3e9; padding:6px; background:white; }
.team-icon-picker span { width:100%; color:#697586; font-size:13px; font-weight:400; }
</style>
