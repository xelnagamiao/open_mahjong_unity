<!-- 麻雀图书馆 · 规则条目（资源 + 讨论） -->
<template>
  <div v-if="rule" class="rule" :style="{ '--ink': rule.accent }">
    <div class="rule-bg" aria-hidden="true">
      <div class="grain" />
    </div>

    <header class="top">
      <router-link class="back" to="/library">← 馆藏目录</router-link>
      <div class="title-block">
        <p class="eyebrow">RULE ENTRY</p>
        <h1>{{ rule.label }}</h1>
        <p class="desc">{{ rule.description }}</p>
      </div>
    </header>

    <main class="body">
      <section class="block resources">
        <div class="block-head">
          <h2>资源存放区</h2>
          <span>{{ rule.resources.length }} 份</span>
        </div>
        <div v-if="rule.resources.length" class="res-grid">
          <article v-for="doc in rule.resources" :key="doc.url" class="res-card">
            <h3>{{ doc.title }}</h3>
            <p v-if="doc.desc">{{ doc.desc }}</p>
            <div class="res-actions">
              <button type="button" class="btn primary" @click="openInNewTab(doc.url)">阅读</button>
              <button type="button" class="btn" @click="downloadDoc(doc.url, doc.filename)">下载</button>
            </div>
          </article>
        </div>
        <p v-else class="empty">暂无 PDF 资源，可在讨论区补充说明。</p>
      </section>

      <section class="block forum">
        <div class="block-head">
          <h2>讨论区</h2>
          <span>最新更新优先</span>
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
          后可发帖与回复
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
                <h3>{{ post.title }}</h3>
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
      </section>
    </main>
  </div>

  <div v-else class="missing">
    <p>未找到该条目。</p>
    <router-link to="/library">返回馆藏目录</router-link>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { ElMessage } from 'element-plus'
import axios from 'axios'
import { storeToRefs } from 'pinia'
import { getLibraryRule } from '@/constants/libraryRules'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { getPlayerToken } from '@/api/playerClient'

const route = useRoute()
const auth = usePlayerAuthStore()
const { loaded } = storeToRefs(auth)
const isLoggedIn = computed(() => auth.isLoggedIn)

const ruleKey = computed(() => String(route.params.rule || ''))
const rule = computed(() => getLibraryRule(ruleKey.value))

watch(
  rule,
  (r) => {
    if (r) document.title = `${r.label} · 麻雀图书馆`
  },
  { immediate: true }
)

const loginRedirect = computed(
  () => `/login?redirect=${encodeURIComponent(`/library/${ruleKey.value}`)}`
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

function openInNewTab(url) {
  window.open(url, '_blank')
}

function downloadDoc(url, filename) {
  const a = document.createElement('a')
  a.href = url
  a.download = filename || ''
  a.target = '_blank'
  a.rel = 'noopener'
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
}

function authHeaders() {
  const token = getPlayerToken()
  return token ? { Authorization: `Bearer ${token}` } : {}
}

async function loadPosts() {
  if (!rule.value) return
  loadingPosts.value = true
  try {
    const res = await axios.get(`/api/library/rules/${rule.value.key}/posts`)
    posts.value = res.data?.data?.items || []
  } catch {
    posts.value = []
    ElMessage.error('加载讨论区失败')
  } finally {
    loadingPosts.value = false
  }
}

async function submitPost() {
  if (!canSubmitPost.value || !rule.value) return
  posting.value = true
  try {
    await axios.post(
      `/api/library/rules/${rule.value.key}/posts`,
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
  } catch {
    ElMessage.error('加载回复失败')
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

watch(ruleKey, () => {
  expandedId.value = null
  replies.value = []
  loadPosts()
})

onMounted(() => {
  if (!loaded.value) auth.fetchMe()
  loadPosts()
})
</script>

<style scoped>
@import url('https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600&family=Noto+Serif+SC:wght@600;700&display=swap');

.rule {
  --paper: #dfeae4;
  --ink-base: #102820;
  --ink-soft: #3d5a4c;
  --line: rgba(16, 40, 32, 0.14);
  --ink: #1f6b52;
  position: relative;
  min-height: 100vh;
  color: var(--ink-base);
  background: var(--paper);
  font-family: 'IBM Plex Sans', 'PingFang SC', 'Microsoft YaHei', sans-serif;
}

.rule-bg {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 0;
  background:
    radial-gradient(ellipse 70% 45% at 90% 0%, color-mix(in srgb, var(--ink) 22%, transparent), transparent 55%),
    linear-gradient(180deg, #e8f1eb 0%, var(--paper) 40%, #d8e6de 100%);
}

.grain {
  position: absolute;
  inset: 0;
  opacity: 0.3;
  background-image: url("data:image/svg+xml,%3Csvg viewBox='0 0 200 200' xmlns='http://www.w3.org/2000/svg'%3E%3Cfilter id='n'%3E%3CfeTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/%3E%3C/filter%3E%3Crect width='100%25' height='100%25' filter='url(%23n)' opacity='0.45'/%3E%3C/svg%3E");
  mix-blend-mode: multiply;
}

.top,
.body {
  position: relative;
  z-index: 1;
  max-width: 860px;
  margin: 0 auto;
  padding-left: clamp(18px, 4vw, 32px);
  padding-right: clamp(18px, 4vw, 32px);
}

.top {
  padding-top: 28px;
  padding-bottom: 8px;
  animation: rise 0.55s ease both;
}

.back {
  display: inline-block;
  color: var(--ink-soft);
  text-decoration: none;
  font-size: 12px;
  font-weight: 600;
  letter-spacing: 0.04em;
  margin-bottom: 18px;
}

.back:hover {
  color: var(--ink);
}

.eyebrow {
  margin: 0 0 8px;
  font-size: 11px;
  letter-spacing: 0.24em;
  color: var(--ink);
  font-weight: 600;
}

.title-block h1 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: clamp(1.8rem, 5vw, 2.5rem);
  font-weight: 700;
  letter-spacing: 0.04em;
  line-height: 1.15;
}

.desc {
  margin: 12px 0 0;
  max-width: 42em;
  font-size: 14px;
  line-height: 1.65;
  color: var(--ink-soft);
}

.body {
  padding-top: 22px;
  padding-bottom: 48px;
  display: flex;
  flex-direction: column;
  gap: 22px;
}

.block {
  animation: rise 0.6s ease both;
}

.block-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 12px;
  padding-bottom: 8px;
  border-bottom: 1px solid var(--line);
}

.block-head h2 {
  margin: 0;
  font-family: 'Noto Serif SC', 'Songti SC', serif;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: 0.08em;
}

.block-head span {
  font-size: 11px;
  color: var(--ink-soft);
  letter-spacing: 0.06em;
}

.res-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 10px;
}

.res-card {
  background: rgba(255, 255, 255, 0.58);
  border: 1px solid var(--line);
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  transition: transform 0.15s ease, background 0.15s ease;
}

.res-card:hover {
  background: #fff;
  transform: translateY(-2px);
}

.res-card h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 650;
}

.res-card p {
  margin: 0;
  flex: 1;
  font-size: 12px;
  line-height: 1.5;
  color: var(--ink-soft);
}

.res-actions {
  display: flex;
  gap: 8px;
}

.btn {
  appearance: none;
  border: 1px solid var(--line);
  background: rgba(255, 255, 255, 0.7);
  color: var(--ink-base);
  font: inherit;
  font-size: 12px;
  font-weight: 600;
  padding: 6px 12px;
  cursor: pointer;
  transition: background 0.15s ease, border-color 0.15s ease, color 0.15s ease;
}

.btn:hover:not(:disabled) {
  border-color: var(--ink);
  color: var(--ink);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn.primary {
  background: var(--ink);
  border-color: var(--ink);
  color: #fff;
}

.btn.primary:hover:not(:disabled) {
  filter: brightness(1.08);
  color: #fff;
}

.empty {
  margin: 0;
  font-size: 13px;
  color: var(--ink-soft);
}

.empty.soft {
  margin-bottom: 8px;
}

.login-tip {
  margin: 0 0 14px;
  font-size: 13px;
  color: var(--ink-soft);
}

.login-tip a {
  color: var(--ink);
  font-weight: 600;
}

.composer {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-bottom: 16px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.5);
  border: 1px solid var(--line);
}

.field {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid var(--line);
  background: rgba(255, 255, 255, 0.85);
  color: var(--ink-base);
  font: inherit;
  font-size: 13px;
  padding: 8px 10px;
  outline: none;
}

.field:focus {
  border-color: var(--ink);
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

.posts {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.post {
  background: rgba(255, 255, 255, 0.48);
  border: 1px solid var(--line);
  transition: border-color 0.15s ease;
}

.post.open {
  border-color: color-mix(in srgb, var(--ink) 45%, var(--line));
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

.post-title h3 {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 650;
  line-height: 1.35;
}

.badge {
  flex-shrink: 0;
  font-size: 11px;
  color: var(--ink-soft);
  background: rgba(16, 40, 32, 0.06);
  padding: 2px 7px;
}

.post-meta {
  display: flex;
  flex-wrap: wrap;
  gap: 10px;
  font-size: 12px;
  color: var(--ink-soft);
}

.post-panel {
  padding: 0 14px 14px;
  border-top: 1px solid var(--line);
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
  border-top: 1px dashed var(--line);
}

.reply-meta {
  display: flex;
  gap: 10px;
  font-size: 12px;
  color: var(--ink-soft);
}

.reply-meta strong {
  color: var(--ink-base);
}

.reply-box {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 8px;
  margin-top: 10px;
  padding-top: 10px;
  border-top: 1px solid var(--line);
}

.missing {
  min-height: 100vh;
  display: grid;
  place-content: center;
  gap: 10px;
  text-align: center;
  background: #dfeae4;
  font-family: 'IBM Plex Sans', sans-serif;
  color: #102820;
}

.missing a {
  color: #1f6b52;
  font-weight: 600;
}

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

@supports not (background: color-mix(in srgb, red 10%, white)) {
  .rule-bg {
    background: linear-gradient(180deg, #e8f1eb 0%, #dfeae4 100%);
  }
  .post.open {
    border-color: #7aa892;
  }
}
</style>
