<template>
  <section class="profile-panel account-feature" aria-label="个人资料" v-loading="loading">
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon><el-button link @click="load">重新加载</el-button></el-alert>
    <template v-if="profile">
      <header class="profile-hero">
        <div class="profile-identity">
          <div><h1 data-no-translate>{{ profile.username }}</h1><p>UID {{ profile.user_id }} <span>·</span> {{ profile.joined_on }} 注册</p></div>
        </div>
        <div class="hero-actions"><el-button class="data-station-button" size="small" type="primary" @click="openData">数据站</el-button></div>
      </header>
      <div class="profile-metrics">
        <article><span>累计对局</span><div>{{ number(profile.total_games) }} <small>场</small></div></article>
        <article><span>活跃天数</span><div>{{ number(profile.active_days) }} <small>天</small></div></article>
      </div>
      <article class="activity-card">
        <header class="section-heading"><h2>对局活动</h2>
          <el-select v-model="year" size="small" aria-label="对局统计年份" class="year-select" @change="load"><el-option v-for="item in years" :key="item" :value="item" :label="`${item} 年`" /></el-select>
        </header>
        <div class="heatmap-scroll" tabindex="0" aria-label="按周排列的年度对局活跃图，可横向滚动">
          <div class="heatmap" :style="{ '--week-count': weeks.length }">
            <span v-for="(label, row) in ['一', '二', '三', '四', '五', '六', '日']" :key="`weekday-${row}`" class="day-label" :style="{ gridColumn: 1, gridRow: row + 2 }" aria-hidden="true">{{ label }}</span>
            <template v-for="(week, index) in weeks" :key="index">
              <span v-if="monthLabel(week)" class="month-label" :style="{ gridColumn: `${index + 2} / span ${Math.min(4, weeks.length - index)}`, gridRow: 1 }" aria-hidden="true">{{ monthLabel(week) }}</span>
              <button v-for="(day, row) in week" :key="day.date" type="button" class="day-cell" :class="[`level-${day.level}`, { outside: day.outside, future: day.future, selected: selected?.date === day.date }]"
                :style="{ gridColumn: index + 2, gridRow: row + 2 }"
                :disabled="day.outside || day.future" :aria-label="`${day.date}，${day.games} 场对局`" :title="`${day.date} · ${day.games} 场对局`" @click="selected = day" />
            </template>
          </div>
        </div>
        <div class="heatmap-footer"><p aria-live="polite">{{ selected ? `${selected.date} · ${selected.games} 场对局` : '' }}</p><div class="heatmap-legend">少 <i v-for="level in 5" :key="level" class="day-cell" :class="`level-${level - 1}`"></i> 多</div></div>
      </article>
      <article class="rules-card"><header class="section-heading"><h2>游玩最多的规则</h2></header>
        <div v-if="profile.favorite_rules.length" class="rules-list"><div v-for="(rule, i) in profile.favorite_rules" :key="rule.rule" class="rule-row"><span class="rule-rank">{{ String(i + 1).padStart(2, '0') }}</span><div class="rule-info"><div><span>{{ rule.label }}</span><span>{{ number(rule.games) }} 场</span></div><div class="rule-track"><div :style="{ width: `${rule.games / mostPlayedRule.games * 100}%` }"></div></div></div></div></div>
        <p v-else class="empty-note">暂无对局</p>
      </article>
    </template>
  </section>
</template>
<script setup>
import '@/styles/account-theme.css'
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import playerApi from '@/api/playerClient'
import { activityGrid } from '@/utils/accountActivity'
const router = useRouter(), auth = usePlayerAuthStore()
const profile = ref(null), loading = ref(false), error = ref(''), selected = ref(null), year = ref(new Date().getFullYear())
let epoch = 0
const number = value => Number(value || 0).toLocaleString('zh-CN')
const mostPlayedRule = computed(() => profile.value?.favorite_rules[0])
const weeks = computed(() => profile.value ? activityGrid(profile.value.year, profile.value.activity.days, profile.value.today) : [])
const years = computed(() => { const current = Number(profile.value?.today?.slice(0, 4)) || new Date().getFullYear(); const first = Math.max(2000, Math.min(current, Number(profile.value?.first_game_on?.slice(0, 4) || profile.value?.joined_on?.slice(0, 4)) || current)); return Array.from({ length: current - first + 1 }, (_, i) => current - i) })
function monthLabel(week) { const day = week.find(day => !day.outside && day.date.endsWith('-01')); return day ? `${Number(day.date.slice(5, 7))}月` : '' }
function openData() { router.push({ path: '/player-data', query: { q: String(auth.userId) } }) }
async function load() {
  if (!auth.isLoggedIn) return
  const revision = ++epoch, owner = auth.userId
  loading.value = true; error.value = ''; selected.value = null
  try { const { data } = await playerApi.get('/account/profile', { params: { year: year.value } }); if (revision === epoch && owner === auth.userId) profile.value = data.data }
  catch (e) { if (revision === epoch && owner === auth.userId) error.value = e.response?.data?.message || '个人资料暂时无法加载' }
  finally { if (revision === epoch) loading.value = false }
}
watch(() => auth.userId, () => { profile.value = null; epoch++; load() }, { immediate: true })
onBeforeUnmount(() => { epoch++ })
</script>
<style scoped>
.profile-panel h1,.profile-panel h2 { font-weight:400; }
.profile-hero { display:flex; justify-content:space-between; align-items:center; gap:16px; padding:0 0 16px; }
.profile-identity { min-width:0; }.profile-hero h1 { margin:0 0 6px; font-size:26px; line-height:1.3; letter-spacing:-.4px; overflow-wrap:anywhere; }.profile-hero p { margin:0; color:var(--account-muted); font-size:12px; }.profile-hero p span { margin:0 8px; color:#9ba5af; }
.hero-actions { display:flex; gap:8px; flex-shrink:0; flex-wrap:wrap; }
.data-station-button { --el-button-bg-color:#dc2626; --el-button-border-color:#dc2626; --el-button-hover-bg-color:#b91c1c; --el-button-hover-border-color:#b91c1c; --el-button-active-bg-color:#991b1b; --el-button-active-border-color:#991b1b; }
.profile-metrics { display:flex; flex-wrap:wrap; width:fit-content; max-width:100%; background:var(--account-surface); border:1px solid var(--account-divider); border-radius:8px; margin-bottom:16px; }
.profile-metrics article { display:flex; flex-wrap:wrap; align-items:baseline; gap:4px 12px; padding:12px 18px; min-width:0; }.profile-metrics article+article { border-left:1px solid var(--account-divider); }.profile-metrics article>span { color:var(--account-muted); font-size:12px; white-space:nowrap; }
.profile-metrics article>div { font-size:24px; line-height:1.2; font-variant-numeric:tabular-nums; letter-spacing:-.5px; white-space:nowrap; }.profile-metrics small { font-size:12px; color:var(--account-muted); letter-spacing:0; }
.activity-card { border:1px solid var(--account-border); border-radius:8px; padding:12px 18px; background:#fff; }
.section-heading { display:flex; justify-content:space-between; align-items:center; gap:12px; margin-bottom:14px; }.section-heading h2 { margin:0; font-size:16px; line-height:1.4; }.year-select { width:104px; flex-shrink:0; }
.heatmap-scroll { overflow-x:auto; padding:2px 2px 5px; }
/* Shared tracks keep month labels, weekdays and square date cells aligned. */
.heatmap { display:grid; grid-template-columns:20px repeat(var(--week-count),minmax(12px,1fr)); grid-template-rows:16px repeat(7,auto); gap:3px; width:100%; min-width:calc(20px + var(--week-count) * 15px); align-items:center; }
.month-label,.day-label { min-width:0; font-size:10px; line-height:12px; color:var(--account-muted); white-space:nowrap; }.day-label { display:flex; justify-content:center; align-items:center; height:12px; }
.day-cell { display:block; box-sizing:border-box; width:12px; height:12px; min-width:0; min-height:0; border:0; border-radius:2px; margin:0; padding:0; appearance:none; line-height:1; cursor:pointer; box-shadow:inset 0 0 0 1px rgba(31,35,40,.04); }
.heatmap .day-cell { width:100%; height:auto; aspect-ratio:1; }.day-cell.selected { outline:2px solid var(--account-blue); outline-offset:1px; }
.level-0 { background:#eff2f6; }.level-1 { background:#c6e2ff; }.level-2 { background:#80bfff; }.level-3 { background:#3b8ff3; }.level-4 { background:#0969da; }.day-cell.outside { visibility:hidden; }.day-cell.future { opacity:.45; cursor:default; }
.heatmap-footer { min-height:17px; display:flex; align-items:center; justify-content:space-between; flex-wrap:wrap; gap:8px; margin-top:6px; font-size:11px; color:var(--account-muted); }.heatmap-footer p { margin:0; }.heatmap-legend { display:flex; align-items:center; gap:3px; white-space:nowrap; }.heatmap-legend i { cursor:default; }
.rules-card { margin-top:16px; }.rules-card .section-heading { margin-bottom:10px; }
.rules-list { display:grid; grid-template-columns:1fr 1fr; gap:12px 32px; }.rule-row { display:flex; align-items:center; gap:10px; }.rule-rank { font-size:11px; color:var(--account-subtle); font-variant-numeric:tabular-nums; }.rule-info { flex:1; min-width:0; }.rule-info>div:first-child { display:flex; justify-content:space-between; gap:10px; font-size:12px; line-height:1.4; }.rule-info>div:first-child span:last-child { color:var(--account-muted); white-space:nowrap; font-variant-numeric:tabular-nums; }.rule-track { height:3px; background:#edf2f8; border-radius:2px; overflow:hidden; margin-top:5px; }.rule-track>div { height:100%; background:#54aeff; border-radius:2px; }.empty-note { font-size:13px; color:var(--account-muted); padding:12px 0; }
@media(max-width:600px) {
 .profile-hero { align-items:flex-start; flex-direction:column; gap:12px; padding-bottom:16px; }.profile-hero h1 { font-size:23px; }.profile-metrics article { padding:10px 12px; gap:4px 8px; }.activity-card { padding:12px; }.rules-list { grid-template-columns:1fr; gap:10px; }
}
</style>
