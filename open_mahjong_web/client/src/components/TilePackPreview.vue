<template>
  <img v-if="src" :src="src" alt="一万、一筒、一索、东风" class="tile-pack-preview" width="402" height="136" loading="lazy" />
</template>

<script setup>
import { ref, watch } from 'vue'
import playerApi from '@/api/playerClient'

const props = defineProps({ row: { type: Object, required: true } })
const src = ref('')

watch(() => [props.row.submission_id, props.row.status, props.row.updated_at], async ([id, status], _, onCleanup) => {
  const path = `/tile-content/${id}/preview`
  if (!status || status === 'approved') {
    src.value = '/api/player' + path
    return
  }
  // Unpublished packs require the owner's bearer token.
  const controller = new AbortController()
  let url
  onCleanup(() => { controller.abort(); if (url) URL.revokeObjectURL(url) })
  src.value = ''
  try {
    const { data } = await playerApi.get(path, { responseType: 'blob', signal: controller.signal })
    if (!controller.signal.aborted) src.value = url = URL.createObjectURL(data)
  } catch { /* The submission's status and download controls remain available. */ }
}, { immediate: true })
</script>

<style scoped>
.tile-pack-preview {
  display: block;
  width: min(100%, 240px);
  height: auto;
  margin-block: 6px;
}
</style>
