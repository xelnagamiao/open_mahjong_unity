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
      <div v-if="event.remark">
        <dt>给管理员的备注</dt>
        <dd class="pre-wrap">{{ event.remark }}</dd>
      </div>
    </dl>
  </article>
</template>

<script setup>
import { computed } from 'vue'

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
</script>

<style scoped>
.event-preview-card { padding: 4px 0; }
.event-preview-top { display: flex; align-items: baseline; gap: 10px; }
.event-preview-top h2 { margin: 0; font-size: 22px; color: #1f2937; }
.event-preview-description { margin: 16px 0; color: #444; line-height: 1.7; white-space: pre-wrap; }
.event-preview-meta { display: grid; gap: 12px; margin: 0; padding-top: 14px; border-top: 1px dashed #dcdfe6; }
.event-preview-meta dt { margin-bottom: 3px; color: #909399; font-size: 12px; }
.event-preview-meta dd { margin: 0; color: #303133; line-height: 1.6; }
.pre-wrap { white-space: pre-wrap; }
</style>
