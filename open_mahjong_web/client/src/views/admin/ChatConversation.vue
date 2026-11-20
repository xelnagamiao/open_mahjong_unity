<template>
  <el-dialog :model-value="true" :title="title" width="min(680px, 94vw)" top="7vh"
    @close="$emit('close')" @opened="centerSelection">
    <div class="conversation">
      <el-alert v-if="error" type="error" :closable="false">
        {{ error }} <el-button link type="primary" @click="load(retryDirection)">重试</el-button>
      </el-alert>
      <el-button v-if="hasOlder" :disabled="loading" @click="load('older')">查看更早的消息</el-button>
      <div ref="timeline" v-loading="loading" class="timeline">
        <p v-if="!loading && !error && !messages.length">此会话暂无消息</p>
        <article v-for="item in messages" :key="item.message_id" :data-selected="item.message_id === message.message_id">
          <header>
            <span>{{ item.sender_name }} <small>UID {{ item.sender_user_id }}</small></span>
            <time :datetime="item.created_at">{{ new Date(item.created_at).toLocaleString('zh-CN', { hour12: false }) }}</time>
          </header>
          <p>{{ item.content }}</p>
        </article>
      </div>
      <el-button v-if="hasNewer" :disabled="loading" @click="load('newer')">查看更新的消息</el-button>
    </div>
  </el-dialog>
</template>

<script setup>
import { nextTick, onMounted, ref } from 'vue'
import adminApi from '@/api/adminClient'

const props = defineProps({ message: { type: Object, required: true }, title: { type: String, required: true } })
defineEmits(['close'])
const timeline = ref(null)
const messages = ref([])
const hasOlder = ref(false)
const hasNewer = ref(false)
const loading = ref(false)
const error = ref('')
const retryDirection = ref('around')

function centerSelection() {
  const panel = timeline.value
  const target = panel?.querySelector('[data-selected="true"]')
  if (target) panel.scrollTop = target.offsetTop - (panel.clientHeight - target.offsetHeight) / 2
}

async function load(direction) {
  if (loading.value) return
  loading.value = true
  error.value = ''
  retryDirection.value = direction
  let anchor = props.message.message_id
  if (direction === 'older') anchor = messages.value[0].message_id
  if (direction === 'newer') anchor = messages.value[messages.value.length - 1].message_id
  try {
    const { data } = await adminApi.get('/chat-history/' + encodeURIComponent(props.message.conversation_key), {
      params: { direction, anchor },
    })
    const page = data.data
    const panel = timeline.value
    const height = panel?.scrollHeight || 0
    if (direction === 'older') messages.value.unshift(...page.items)
    else if (direction === 'newer') messages.value.push(...page.items)
    else messages.value = page.items
    if (direction !== 'newer') hasOlder.value = page.has_older
    if (direction !== 'older') hasNewer.value = page.has_newer
    await nextTick()
    if (direction === 'around') centerSelection()
    if (direction === 'older' && panel) panel.scrollTop += panel.scrollHeight - height
  } catch (e) {
    error.value = e.response?.data?.message || '读取会话失败'
  } finally {
    loading.value = false
  }
}

onMounted(() => load('around'))
</script>

<style scoped>
.conversation { display: grid; gap: 10px; font-synthesis: none; }
.conversation :deep(.el-button) { justify-self: start; font-weight: 400; }
.timeline { position: relative; max-height: 58vh; min-height: 72px; overflow-y: auto; overflow-anchor: none; }
article { padding: 12px; border-bottom: 1px solid var(--el-border-color-lighter); }
article[data-selected="true"] { background: var(--el-color-primary-light-9); border-inline-start: 3px solid var(--el-color-primary); }
header { display: flex; flex-wrap: wrap; align-items: baseline; justify-content: space-between; gap: 4px 12px; }
small, time { color: var(--el-text-color-secondary); font-size: 12px; }
p { margin: 6px 0 0; white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
