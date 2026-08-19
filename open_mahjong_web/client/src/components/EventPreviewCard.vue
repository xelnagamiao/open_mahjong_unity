<template>
  <article class="event-preview-card">
    <div class="event-preview-top">
      <h2>{{ event.name || (event.kind === 'base' ? '未填写基地名称' : '未填写赛事名称') }}</h2>
      <el-tag v-if="event.kind" :type="event.kind === 'base' ? 'warning' : 'primary'" size="small" effect="plain">
        {{ event.kind === 'base' ? '基地' : '赛事' }}
      </el-tag>
      <el-tag type="warning" size="small">{{ statusLabel }}</el-tag>
    </div>
    <p class="event-preview-description">{{ event.description?.trim() || (event.kind === 'base' ? '暂无基地介绍' : '暂无赛事介绍') }}</p>
    <dl class="event-preview-meta">
      <div v-if="event.planned_start_at || event.planned_end_at">
        <dt>拟定时间</dt>
        <dd>{{ plannedRange }}</dd>
      </div>
      <div v-if="event.applicant_username || event.requester_username">
        <dt>申请人</dt>
        <dd>{{ event.applicant_username || event.requester_username }}</dd>
      </div>
      <div v-if="organizerText">
        <dt>{{ event.kind === 'base' ? '基地负责人' : '赛事负责人' }}</dt>
        <dd>{{ organizerText }}</dd>
      </div>
      <div v-if="remarkItems.length">
        <dt>双方备注往来</dt>
        <dd>
          <ApplicationRemarkThread :items="remarkItems" />
        </dd>
      </div>
    </dl>
  </article>
</template>

<script setup>
import { computed } from 'vue'
import ApplicationRemarkThread from '@/components/ApplicationRemarkThread.vue'

const props = defineProps({
  event: { type: Object, required: true },
  statusLabel: { type: String, default: '申请中的赛事页面预览' },
})

function formatDay(value) {
  if (!value) return ''
  const text = String(value)
  return /^\d{4}-\d{2}-\d{2}/.test(text) ? text.slice(0, 10) : text
}

const plannedRange = computed(() => {
  const start = formatDay(props.event.planned_start_at)
  const end = formatDay(props.event.planned_end_at)
  if (start && end) return `${start} 至 ${end}`
  if (start) return `${start} 起`
  return `至 ${end}`
})

const organizerText = computed(() => {
  const name = String(props.event.organizer_name || '').trim()
  const phone = String(props.event.organizer_phone || '').trim()
  if (name && phone) return `${name} ${phone}`
  return name || phone
})

const remarkItems = computed(() => {
  const history = props.event.remark_history
  if (Array.isArray(history) && history.length) return history
  const items = []
  const remark = String(props.event.remark || '').trim()
  if (remark) {
    items.push({ at: props.event.created_at || null, role: 'applicant', action: 'submit', text: remark })
  }
  const review = String(props.event.review_note || '').trim()
  if (review) {
    items.push({
      at: props.event.reviewed_at || props.event.updated_at || null,
      role: 'admin',
      action: props.event.status === 'approved' ? 'approve' : 'reject',
      text: review,
    })
  }
  return items
})
</script>

<style scoped>
.event-preview-card { padding: 4px 0; }
.event-preview-top { display: flex; align-items: baseline; gap: 10px; }
.event-preview-top h2 { margin: 0; font-size: 22px; color: #1f2937; }
.event-preview-description { margin: 16px 0; color: #444; line-height: 1.7; white-space: pre-wrap; }
.event-preview-meta { display: grid; gap: 12px; margin: 0; padding-top: 14px; border-top: 1px dashed #dcdfe6; }
.event-preview-meta dt { margin-bottom: 3px; color: #909399; font-size: 12px; }
.event-preview-meta dd { margin: 0; color: #303133; line-height: 1.6; }
</style>
