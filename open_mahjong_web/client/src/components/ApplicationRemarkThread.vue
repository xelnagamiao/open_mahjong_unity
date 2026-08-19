<template>
  <div class="remark-thread">
    <div v-if="!items.length" class="empty">{{ tr('暂无双方备注往来') }}</div>
    <div
      v-for="(item, idx) in items"
      :key="idx"
      class="item"
      :class="item.role === 'admin' ? 'is-admin' : 'is-applicant'"
    >
      <div class="meta">
        <span class="who">{{ roleLabel(item.role) }}</span>
        <span class="act">{{ actionLabel(item.action) }}</span>
        <span v-if="formatTime(item.at)" class="time">{{ formatTime(item.at) }}</span>
      </div>
      <div class="body">{{ item.text }}</div>
    </div>
  </div>
</template>

<script setup>
import { tr } from '@/i18n'

defineProps({
  items: { type: Array, default: () => [] },
})

function roleLabel(role) {
  return role === 'admin' ? tr('审核员') : tr('申请人')
}

function actionLabel(action) {
  return tr(
    ({
      submit: '提交申请',
      save: '保存修改',
      resubmit: '重新提交',
      approve: '审核通过',
      reject: '打回',
      note: '备注',
    })[action] || '备注'
  )
}

function formatTime(value) {
  if (!value) return ''
  const d = new Date(value)
  if (Number.isNaN(d.getTime())) return String(value)
  return d.toLocaleString('zh-CN', { hour12: false })
}
</script>

<style scoped>
.remark-thread {
  display: flex;
  flex-direction: column;
  gap: 10px;
  width: 100%;
  max-height: 320px;
  overflow-y: auto;
  padding: 10px 12px;
  background: #f8fafc;
  border: 1px solid #e5e7eb;
  border-radius: 6px;
}
.empty {
  color: #909399;
  font-size: 13px;
}
.item {
  padding: 8px 10px;
  border-radius: 6px;
  border: 1px solid #ebeef5;
  background: #fff;
}
.item.is-admin {
  border-color: #f5dab1;
  background: #fdf6ec;
}
.item.is-applicant {
  border-color: #c6e2ff;
  background: #ecf5ff;
}
.meta {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 4px;
  font-size: 12px;
  color: #909399;
}
.who {
  font-weight: 600;
  color: #303133;
}
.body {
  white-space: pre-wrap;
  word-break: break-word;
  line-height: 1.65;
  color: #303133;
  font-size: 13px;
}
</style>
