<template>
  <div class="event-detail-page">
    <div class="sec-h">■ {{ event?.kind === 'base' ? '基地详情' : '比赛详情' }}</div>
    <div v-if="loading" class="tip">加载中…</div>
    <div v-else-if="!event" class="tip">赛事不存在</div>
    <template v-else>
      <article class="hero">
        <div class="hero-top">
          <h1>{{ event.name }}</h1>
          <span :class="['st', event.status]">{{ eventStatusLabel(event.status) }}</span>
          <span class="kind">{{ venueKindLabel(event.kind) }}</span>
        </div>
        <p class="desc">{{ event.description?.trim() || (event.kind === 'base' ? '暂无基地介绍' : '暂无赛事介绍') }}</p>
        <dl class="meta">
          <div>
            <dt>{{ event.kind === 'base' ? '基地主管理员' : '赛事主管理员' }}</dt>
            <dd>{{ event.owner_username || '—' }}</dd>
          </div>
          <div>
            <dt>对局数</dt>
            <dd>{{ event.game_count ?? 0 }}</dd>
          </div>
          <div>
            <dt>创建时间</dt>
            <dd>{{ formatDate(event.created_at) }}</dd>
          </div>
          <div v-if="event.closed_at">
            <dt>关闭时间</dt>
            <dd>{{ formatDate(event.closed_at) }}</dd>
          </div>
          <div>
            <dt>{{ event.kind === 'base' ? '基地 ID' : '赛事 ID' }}</dt>
            <dd class="mono">{{ event.event_id }}</dd>
          </div>
        </dl>
        <div class="actions">
          <router-link class="btn" to="/player-data">前往数据站</router-link>
          <router-link class="btn ghost" to="/events">返回列表</router-link>
        </div>
      </article>

      <section class="announcements">
        <div class="ann-head">
          <h2>{{ event.kind === 'base' ? '基地公告' : '比赛公告' }}</h2>
          <span class="ann-count">{{ announcements.length === 1 ? tr('1 条公告') : tr('公告数量({count})', { count: announcements.length }) }}</span>
        </div>
        <div v-if="!announcements.length" class="ann-empty">暂无公告</div>
        <ul v-else class="ann-list">
          <li v-for="item in announcements" :key="item.announcement_id" class="ann-item">
            <div class="ann-item-top">
              <h3>{{ item.title }}</h3>
              <time>{{ formatDate(item.created_at) }}</time>
            </div>
            <p class="ann-body">{{ item.body }}</p>
            <div class="ann-foot">
              {{ item.author_username || `用户 ${item.created_by}` }}
            </div>
          </li>
        </ul>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import axios from 'axios'
import { eventStatusLabel, venueKindLabel } from '@/utils/eventMeta'
import { locale, tr } from '@/i18n'

const route = useRoute()
const event = ref(null)
const loading = ref(true)

const announcements = computed(() => event.value?.announcements || [])

function formatDate(v) {
  if (!v) return ''
  const d = new Date(v)
  if (Number.isNaN(d.getTime())) return String(v)
  return d.toLocaleString(locale.value, { hour12: false })
}

async function load() {
  loading.value = true
  event.value = null
  try {
    const res = await axios.get(`/api/player/events/${encodeURIComponent(route.params.eventId)}`)
    event.value = res.data?.data || null
    if (event.value?.name) {
      document.title = `${tr(event.value.name)} - salasasa.cn`
    }
  } catch {
    event.value = null
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(() => route.params.eventId, load)
</script>

<style scoped>
.sec-h {
  background: rgba(0, 0, 0, 0.75);
  color: #fff;
  padding: 6px 12px;
  font-size: 13px;
  margin-bottom: 12px;
}
.tip { color: #999; font-size: 13px; }

.hero {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 24px 22px 20px;
  margin-bottom: 16px;
}
.hero-top {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  gap: 12px;
  margin-bottom: 14px;
}
.kind {
  font-size: 13px;
  color: #888;
}
.hero h1 {
  margin: 0;
  font-size: 1.65rem;
  font-weight: 700;
  line-height: 1.3;
  color: #1a1a1a;
}
.desc {
  margin: 0 0 18px;
  color: #444;
  font-size: 15px;
  line-height: 1.75;
  white-space: pre-wrap;
}
.meta {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(160px, 1fr));
  gap: 12px 20px;
  margin: 0;
  padding-top: 14px;
  border-top: 1px dashed #e8e8e8;
}
.meta div { min-width: 0; }
.meta dt {
  margin: 0 0 4px;
  font-size: 12px;
  color: #999;
}
.meta dd {
  margin: 0;
  font-size: 14px;
  color: #333;
}
.mono {
  font-family: ui-monospace, Consolas, monospace;
  font-size: 13px;
  word-break: break-all;
}
.st {
  font-size: 13px;
  font-weight: 600;
}
.st.active { color: #067a3a; }
.st.registered { color: #606266; }
.st.closed { color: #c45656; }
.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 20px;
}
.btn {
  display: inline-block;
  background: #ff0000;
  color: #fff;
  padding: 8px 14px;
  font-size: 13px;
  font-weight: 700;
  text-decoration: none;
}
.btn.ghost {
  background: #fff;
  color: #333;
  border: 1px solid #ddd;
}

.announcements {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 18px 20px 12px;
}
.ann-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  gap: 12px;
  margin-bottom: 14px;
}
.ann-head h2 {
  margin: 0;
  font-size: 1.1rem;
}
.ann-count {
  font-size: 12px;
  color: #999;
}
.ann-empty {
  color: #999;
  font-size: 13px;
  padding: 12px 0 18px;
}
.ann-list {
  list-style: none;
  margin: 0;
  padding: 0;
}
.ann-item {
  padding: 14px 0;
  border-top: 1px solid #f0f0f0;
}
.ann-item:first-child {
  border-top: none;
  padding-top: 0;
}
.ann-item-top {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 8px;
  margin-bottom: 8px;
}
.ann-item-top h3 {
  margin: 0;
  font-size: 15px;
  font-weight: 700;
  color: #222;
}
.ann-item-top time {
  font-size: 12px;
  color: #999;
}
.ann-body {
  margin: 0;
  font-size: 14px;
  line-height: 1.7;
  color: #444;
  white-space: pre-wrap;
}
.ann-foot {
  margin-top: 8px;
  font-size: 12px;
  color: #999;
}
</style>
