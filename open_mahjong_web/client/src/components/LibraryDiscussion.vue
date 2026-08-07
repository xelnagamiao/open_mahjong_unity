<!-- 图书馆讨论区（规则条目 / 图书馆板块共用） -->
<template>
  <div class="library-discussion">
    <div class="disc-head">
      <h3>{{ heading }}</h3>
    </div>

    <div v-if="isLoggedIn" class="composer">
      <input v-model="newTitle" class="field" maxlength="200" placeholder="标题" />
      <textarea
        v-model="newBody"
        class="field area"
        rows="3"
        maxlength="10000"
        placeholder="问题、牌例或规则讨论…"
      />
      <div class="composer-bar">
        <button
          type="button"
          class="btn primary"
          :disabled="!canSubmitPost || posting"
          @click="submitPost"
        >
          {{ posting ? '发布中…' : '发帖' }}
        </button>
      </div>
    </div>
    <p v-else class="login-tip">
      <router-link :to="loginRedirect">登录</router-link>
      后可发帖与回复。
    </p>

    <div v-if="loadingPosts" class="empty">加载中…</div>
    <div v-else-if="!posts.length" class="empty">还没有帖子。</div>
    <div v-else class="posts">
      <article
        v-for="post in posts"
        :key="post.post_id"
        class="post"
        :class="{ open: expandedId === post.post_id }"
      >
        <button type="button" class="post-head" @click="togglePost(post)">
          <div class="post-title">
            <h4>{{ post.title }}</h4>
            <span class="badge">{{ post.reply_count }} 回复</span>
          </div>
          <div class="post-meta">
            <span>{{ displayAuthor(post) }}</span>
            <span>更新 {{ formatDate(post.updated_at) }}</span>
          </div>
        </button>

        <div v-if="expandedId === post.post_id" class="post-panel">
          <p class="body-text">{{ post.body }}</p>

          <div v-if="detailLoading && detailPostId === post.post_id" class="empty">加载回复…</div>
          <div v-else-if="detailPostId === post.post_id" class="replies">
            <div v-if="!replies.length" class="empty soft">暂无回复</div>
            <div v-for="reply in replies" :key="reply.reply_id" class="reply">
              <div class="reply-meta">
                <strong>{{ displayAuthor(reply) }}</strong>
                <span>{{ formatDate(reply.created_at) }}</span>
              </div>
              <p class="body-text">{{ reply.body }}</p>
            </div>

            <div v-if="isLoggedIn" class="reply-box">
              <textarea
                v-model="replyBody"
                class="field area"
                rows="2"
                maxlength="5000"
                placeholder="写下回复…"
              />
              <button
                type="button"
                class="btn primary"
                :disabled="!replyBody.trim() || replying"
                @click="submitReply(post)"
              >
                {{ replying ? '提交中…' : '回复' }}
              </button>
            </div>
          </div>
        </div>
      </article>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import axios from 'axios'
import { storeToRefs } from 'pinia'
import { libraryTopicLabel } from '@/constants/libraryRules'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { getPlayerToken } from '@/api/playerClient'

const props = defineProps({
  topicKey: {
    type: String,
    required: true,
  },
  title: {
    type: String,
    default: '',
  },
})

const route = useRoute()
const auth = usePlayerAuthStore()
const { loaded } = storeToRefs(auth)
const isLoggedIn = computed(() => auth.isLoggedIn)

const heading = computed(() => props.title || libraryTopicLabel(props.topicKey))
const loginRedirect = computed(
  () => `/login?redirect=${encodeURIComponent(route.fullPath)}`
)

const posts = ref([])
const loadingPosts = ref(false)
const posting = ref(false)
const newTitle = ref('')
const newBody = ref('')

const expandedId = ref(null)
const detailPostId = ref(null)
const detailLoading = ref(false)
const replies = ref([])
const replyBody = ref('')
const replying = ref(false)

const canSubmitPost = computed(
  () => newTitle.value.trim().length > 0 && newBody.value.trim().length > 0
)

function displayAuthor(row) {
  return row.author_username || (row.author_user_id != null ? `用户${row.author_user_id}` : '匿名')
}

function formatDate(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  return d.toLocaleString('zh-CN', { hour12: false })
}

function authHeaders() {
  const token = getPlayerToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function loadPosts() {
  loadingPosts.value = true
  try {
    const res = await axios.get(`/api/library/rules/${props.topicKey}/posts`)
    posts.value = res.data?.data?.items || []
    maybeFocusPost()
  } catch {
    posts.value = []
    ElMessage.error('加载讨论区失败')
  } finally {
    loadingPosts.value = false
  }
}

async function submitPost() {
  if (!canSubmitPost.value) return
  posting.value = true
  try {
    await axios.post(
      `/api/library/rules/${props.topicKey}/posts`,
      { title: newTitle.value.trim(), body: newBody.value.trim() },
      { headers: authHeaders() }
    )
    newTitle.value = ''
    newBody.value = ''
    ElMessage.success('发帖成功')
    await loadPosts()
  } catch (err) {
    ElMessage.error(err.response?.data?.message || '发帖失败')
  } finally {
    posting.value = false
  }
}

async function togglePost(post) {
  if (expandedId.value === post.post_id) {
    expandedId.value = null
    return
  }
  expandedId.value = post.post_id
  replyBody.value = ''
  await loadPostDetail(post.post_id)
}

async function loadPostDetail(postId) {
  detailPostId.value = postId
  detailLoading.value = true
  replies.value = []
  try {
    const res = await axios.get(`/api/library/posts/${postId}`)
    replies.value = res.data?.data?.replies || []
    const fresh = res.data?.data?.post
    if (fresh) {
      const idx = posts.value.findIndex((p) => p.post_id === postId)
      if (idx >= 0) posts.value[idx] = { ...posts.value[idx], ...fresh }
    }
    return fresh || null
  } catch {
    ElMessage.error('加载回复失败')
    return null
  } finally {
    detailLoading.value = false
  }
}

async function submitReply(post) {
  const text = replyBody.value.trim()
  if (!text) return
  replying.value = true
  try {
    await axios.post(
      `/api/library/posts/${post.post_id}/replies`,
      { body: text },
      { headers: authHeaders() }
    )
    replyBody.value = ''
    ElMessage.success('回复成功')
    await loadPosts()
    expandedId.value = post.post_id
    await loadPostDetail(post.post_id)
  } catch (err) {
    ElMessage.error(err.response?.data?.message || '回复失败')
  } finally {
    replying.value = false
  }
}

// 从“最近讨论”等入口直达：?topic=<key>&post=<id> 时自动展开对应帖子
function maybeFocusPost() {
  const q = route.query
  if (!q.post) return
  if (q.topic && q.topic !== props.topicKey) return
  const postId = Number(q.post)
  if (!Number.isFinite(postId) || postId <= 0) return

  const found = posts.value.find((p) => p.post_id === postId)
  if (found) {
    expandedId.value = postId
    loadPostDetail(postId)
    return
  }
  loadPostDetail(postId).then((fresh) => {
    if (fresh && !posts.value.some((p) => p.post_id === postId)) {
      posts.value = [fresh, ...posts.value]
    }
    expandedId.value = postId
  })
}

watch(
  () => props.topicKey,
  () => {
    expandedId.value = null
    replies.value = []
    newTitle.value = ''
    newBody.value = ''
    loadPosts()
  }
)

watch(
  () => route.query,
  () => {
    if (!loadingPosts.value) maybeFocusPost()
  },
  { deep: true }
)

onMounted(() => {
  if (!loaded.value) auth.fetchMe()
  loadPosts()
})
</script>

<style scoped>
.library-discussion {
  --disc-line: rgba(16, 40, 32, 0.14);
  --disc-ink: #102820;
  --disc-soft: #3d5a4c;
  color: var(--disc-ink);
}

.disc-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--disc-line);
}

.disc-head h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  letter-spacing: 0.06em;
}

.disc-head span {
  font-size: 11px;
  color: var(--disc-soft);
  letter-spacing: 0.06em;
}

.composer {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 14px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.5);
  border: 1px solid var(--disc-line);
}

.field {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid var(--disc-line);
  background: rgba(255, 255, 255, 0.85);
  color: var(--disc-ink);
  font: inherit;
  font-size: 13px;
  padding: 8px 10px;
  outline: none;
}

.field:focus {
  border-color: var(--disc-ink);
}

.field.area {
  resize: vertical;
  min-height: 72px;
  line-height: 1.5;
}

.composer-bar {
  display: flex;
  justify-content: flex-end;
}

.btn {
  appearance: none;
  border: 1px solid var(--disc-line);
  background: rgba(255, 255, 255, 0.7);
  color: var(--disc-ink);
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  padding: 6px 12px;
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.btn:hover:not(:disabled) {
  border-color: var(--disc-ink);
  color: var(--disc-ink);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: var(--disc-ink);
  border-color: var(--disc-ink);
  color: #fff;
}

.btn.primary:hover:not(:disabled) {
  filter: brightness(1.08);
  color: #fff;
}

.login-tip {
  margin: 0 0 14px;
  font-size: 13px;
  color: var(--disc-soft);
}

.login-tip a {
  color: var(--disc-ink);
  font-weight: 600;
}

.empty {
  margin: 0;
  font-size: 13px;
  color: var(--disc-soft);
}

.empty.soft {
  margin-bottom: 8px;
}

.posts {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.post {
  background: rgba(255, 255, 255, 0.48);
  border: 1px solid var(--disc-line);
  transition: border-color 0.15s ease;
}

.post.open {
  border-color: rgba(31, 107, 82, 0.45);
  background: rgba(255, 255, 255, 0.72);
}

.post-head {
  width: 100%;
  text-align: left;
  border: 0;
  background: transparent;
  padding: 12px 14px;
  cursor: pointer;
  font: inherit;
  color: inherit;
}

.post-head:hover {
  background: rgba(255, 255, 255, 0.35);
}

.post-title {
  display: flex;
  justify-content: space-between;
  gap: 10px;
  align-items: flex-start;
  margin-bottom: 4px;
}

.post-title h4 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 650;
  line-height: 1.35;
}

.badge {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--disc-soft);
  background: rgba(16, 40, 32, 0.06);
  padding: 2px 7px;
}

.post-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  font-size: 12px;
  color: var(--disc-soft);
}

.post-panel {
  padding: 0 14px 14px;
  border-top: 1px solid var(--disc-line);
}

.body-text {
  margin: 10px 0;
  font-size: 13px;
  line-height: 1.65;
  white-space: pre-wrap;
  word-break: break-word;
}

.reply {
  padding: 8px 0;
  border-top: 1px dashed var(--disc-line);
}

.reply-meta {
  display: flex;
  gap: 10px;
  font-size: 12px;
  color: var(--disc-soft);
}

.reply-meta strong {
  color: var(--disc-ink);
}

.reply-box {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--disc-line);
}
</style>
