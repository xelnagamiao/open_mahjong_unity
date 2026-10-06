<template>
  <div class="home">
    <header class="welcome">
      <h1>欢迎来到salasasa.cn</h1>
    </header>

    <section class="plat">
      <div class="hd">
        <div class="hd-rules">
          <button
            v-for="r in RULE_DEFS"
            :key="r.value"
            type="button"
            :class="{ on: activeRule === r.value }"
            @click="selectRule(r.value)"
          >{{ r.label }}</button>
        </div>
        <label class="rule-picker">
          <span>全部规则</span>
          <select :value="activeRule" aria-label="选择全部规则" @change="selectRule($event.target.value)">
            <option v-for="rule in allRuleOptions" :key="rule.value" :value="rule.value">{{ rule.label }}</option>
          </select>
        </label>
      </div>
      <div class="bd">
        <div class="stats-side">
          <div class="filter-row">
            <div class="tier-tabs">
              <button
                v-for="s in sceneOptions"
                :key="s.value"
                type="button"
                :class="{ on: activeScene === s.value }"
                @click="activeScene = s.value"
              >{{ s.label }}</button>
            </div>
            <span class="filter-sep" aria-hidden="true" />
            <div class="tier-tabs sub">
              <button
                type="button"
                :class="{ on: activeMode === '' }"
                @click="activeMode = ''"
              >全部局制</button>
              <button
                v-for="m in GAME_TYPE_OPTIONS"
                :key="m.value"
                type="button"
                :class="{ on: activeMode === m.value }"
                @click="activeMode = m.value"
              >{{ m.label }}</button>
            </div>
          </div>
          <div v-if="loading" class="tip">加载中…</div>
          <div v-else class="stats-grid">
            <div v-for="row in statsRows" :key="row.label" class="stats-cell">
              <span class="stats-label" :title="row.tip">{{ row.label }}</span>
              <span class="stats-value">{{ row.value }}</span>
            </div>
          </div>
        </div>
        <aside class="query-side">
          <h3>数据站</h3>
          <p>可查询平台/玩家的对局数据，进行筛选或分析</p>
          <router-link class="btn" to="/player-data">进入数据站</router-link>
        </aside>
      </div>
    </section>

    <section class="sec">
      <div class="sec-h">■ 对战、账户设置、赛事申请/管理</div>
      <div class="grid g3 account-events">
        <div class="panel platform-card">
          <router-link class="platform-card-main" to="/game-unity">
            <h3>进入平台</h3>
            <p>
              Salasasa平台是open_mahjong_unity项目的示例服务器，目前支持国标/立直/青雀/川麻/长麻以及一些子规则
            </p>
          </router-link>
          <router-link class="platform-2d-link" to="/2d">
            进入2D模式<span v-if="locale !== 'zh-CN'">（完整支持繁体中文、英语、日语）</span>
          </router-link>
        </div>

        <div class="panel recent">
          <h3>近期赛事</h3>
          <div v-if="eventsLoading" class="tip">加载中…</div>
          <div v-else-if="!recentEvents.length" class="tip">暂无赛事</div>
          <div v-else class="mini-list">
            <router-link
              v-for="ev in recentEvents"
              :key="ev.event_id"
              class="mini-item link"
              :to="`/events/${ev.event_id}`"
            >
              <span class="ev-name">{{ ev.name }}</span>
              <span :class="['st', ev.status]">{{ eventStatusLabel(ev.status) }}</span>
            </router-link>
          </div>
          <router-link class="panel-btn ghost" to="/events">全部比赛</router-link>
        </div>

        <div class="panel">
          <h3>账户设置与赛事申请/管理</h3>
          <template v-if="!auth.loaded">
            <p>加载中…</p>
          </template>
          <template v-else-if="auth.isLoggedIn">
            <p>绑定邮箱、修改密码、申请、管理赛事</p>
            <router-link class="panel-btn" to="/account">进入管理面板</router-link>
          </template>
          <template v-else>
            <p>网站账户与游戏内账户互通，注册后可直接登录对战平台。</p>
            <div class="panel-actions">
              <router-link class="panel-btn" to="/register?redirect=/">注册</router-link>
              <router-link class="panel-btn ghost" to="/login?redirect=/">登录</router-link>
              <router-link class="panel-btn ghost" to="/forgot-password">找回密码</router-link>
            </div>
          </template>
        </div>
      </div>
    </section>

    <section class="sec">
      <div class="sec-h">■ 其他入口</div>
      <div class="grid g4">
        <template v-for="item in battleLinks" :key="item.title">
          <div
            v-if="item.footerHref"
            class="card card-with-footer"
            :style="{ background: item.color }"
          >
            <a
              class="card-main"
              :href="item.href"
              target="_blank"
              rel="noopener noreferrer"
            >
              <h3>{{ item.title }}</h3>
              <p>{{ item.description }}</p>
            </a>
            <DiscordEntry :url="item.footerHref" />
          </div>
          <a
            v-else-if="item.href"
            class="card"
            :style="{ background: item.color }"
            :href="item.href"
            target="_blank"
            rel="noopener noreferrer"
          >
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </a>
          <router-link
            v-else
            class="card"
            :style="{ background: item.color }"
            :to="item.to"
          >
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </router-link>
        </template>
      </div>
    </section>

    <section class="sec">
      <div class="sec-h">■ 牌理、计算器与猜番</div>
      <div class="grid g3">
        <router-link
          v-for="item in calcLinks"
          :key="item.to"
          class="card"
          :style="{ background: item.color }"
          :to="item.to"
        >
          <h3>{{ item.title }}</h3>
          <p>{{ item.description }}</p>
        </router-link>
      </div>
    </section>

    <section class="sec">
      <div class="sec-h">■ 工具与文档</div>
      <div class="grid g4">
        <div class="ad-slot">广告位招租</div>
        <template v-for="item in toolLinks" :key="item.title">
          <div
            v-if="item.placeholder"
            class="card card-placeholder"
            :style="{ background: item.color }"
          >
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </div>
          <a
            v-else-if="item.href"
            class="card"
            :style="{ background: item.color }"
            :href="item.href"
            target="_blank"
            rel="noopener noreferrer"
          >
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </a>
          <router-link
            v-else
            class="card"
            :style="{ background: item.color }"
            :to="item.to"
          >
            <h3>{{ item.title }}</h3>
            <p>{{ item.description }}</p>
          </router-link>
        </template>
      </div>
    </section>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import axios from 'axios'
import { HOME_RULE_DEFS, homeRuleOptions, homeSceneOptions, homeDefaultScene, selectHomeStats, buildHomeStatsRows } from '@/utils/homeStats'
import { eventStatusLabel } from '@/utils/eventMeta'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { locale } from '@/i18n'
import DiscordEntry from '@/components/DiscordEntry.vue'

const auth = usePlayerAuthStore()

const recentEvents = ref([])
const eventsLoading = ref(false)

async function loadRecentEvents() {
  eventsLoading.value = true
  try {
    const res = await axios.get('/api/player/events')
    recentEvents.value = (res.data?.data?.items || []).slice(0, 8)
  } catch {
    recentEvents.value = []
  } finally {
    eventsLoading.value = false
  }
}

const RULE_DEFS = HOME_RULE_DEFS

const GAME_TYPE_OPTIONS = [
  { value: 'quanzhuang', label: '全庄战' },
  { value: 'xifeng', label: '东西战' },
  { value: 'banzhuang', label: '半庄战' },
  { value: 'dongfeng', label: '东风战' },
]

const loading = ref(false)
const allRows = ref([])
const allRuleOptions = computed(() => homeRuleOptions(allRows.value))

const activeRule = ref('guobiao')
const activeScene = ref(homeDefaultScene(activeRule.value))
const activeMode = ref('') // game_type: quanzhuang / xifeng / ...

const sceneOptions = computed(() => homeSceneOptions(activeRule.value))

const selectRule = (rule) => {
  activeRule.value = rule
  activeScene.value = homeDefaultScene(rule)
  activeMode.value = ''
}

const selectedStats = computed(() => selectHomeStats(allRows.value, activeRule.value, activeScene.value, activeMode.value || null))
const statsRows = computed(() => buildHomeStatsRows(selectedStats.value))

watch([activeRule, activeScene], () => {
  if (activeMode.value && !GAME_TYPE_OPTIONS.some((m) => m.value === activeMode.value)) {
    activeMode.value = ''
  }
})

const battleLinks = [
  {
    href: 'https://store.steampowered.com/app/4565740/Salasasa/',
    title: 'Steam商店',
    description: '转至平台steam商店页面',
    color: '#1b2838',
  },
  { to: '/mobile-download', title: '手机版下载', description: 'Android APK', color: '#67c23a' },
  {
    href: 'https://qm.qq.com/q/MGGZV58hOO',
    title: '加入QQ群',
    description: '加入平台交流群，与群友约桌与反馈',
    color: '#12b7f5',
    footerHref: 'https://discord.gg/RqvxDDgdFH',
  },
  { to: '/github', title: 'Github项目', description: '转至github项目页面', color: '#6699cc' },
]

const calcLinks = [
  { to: '/paili', title: '牌理', description: '分析手牌是否听牌，以及听牌的向听数或待牌。', color: '#9b59b6' },
  { to: '/calc/chinese', title: '麻雀计算器', description: '根据手牌、副露、花牌、和牌方式计算番种、得分与全部和牌拆解形态。', color: '#45B7D1' },
  { to: '/guess-fan', title: '猜番对抗', description: '单人训练或开房对战', color: '#c0392b' },
]

const toolLinks = [
  { to: '/duplicate', title: '创建复式牌墙', description: '通过手动构建、随机种子复现、新建随机种子设置不同类型的复式密钥，供开设对局使用。', color: '#17756a' },
  { to: '/rulebook', title: '规则书', description: '查询国标/立直/青雀/古典等规则的PDF说明书。', color: '#a78bfa' },
  { to: '/seed-verify', title: '随机种子验证', description: '输入对局公布的主种子与盐值，在本地复现随机到的座位与每局配牌，验证服务端未替换随机种子。', color: '#e6a23c' },
  { to: '/docs', title: '开发手册', description: '查看开发文档，设计自定义的麻将规则。', color: '#00b300' },
  {
    to: '/guide',
    title: '使用说明',
    description: '平台简介、公平机制、随机种子与自定义规则等说明。',
    color: '#5470c6',
  },
  { to: '/tools/record-convert', title: '牌谱格式转换', description: '在Salasasa、Botzone、雀渣 与 MJAI 牌谱格式之间转换。', color: '#409eff' },
  { to: '/library', title: '麻雀图书馆', description: '查阅各类麻将规则的说明书、牌例或文档。', color: '#f97316' },
  { to: '/tiles', title: '牌面', description: '下载已审核的牌面与牌面背景。', color: '#0891b2' },
  {
    href: 'https://mcr.pinkst.cn/#/replay',
    title: 'ai牌谱分析',
    description: '基于kdens3的简易牌谱分析，由群友新手求教提供~',
    color: '#6d28d9',
  },
]

const loadStats = async () => {
  loading.value = true
  try {
    const res = await axios.get('/api/platform/home-stats')
    allRows.value = res.data?.data?.rows || []
  } catch {
    allRows.value = []
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  loadStats()
  loadRecentEvents()
  if (!auth.loaded) auth.fetchMe()
})
</script>

<style scoped>
.home {
  color: #333;
}

.welcome {
  margin-bottom: 18px;
  padding: 22px 20px;
  background: #1a1a1a;
  color: #fff;
  text-align: center;
}

.welcome h1 {
  margin: 0;
  font-size: 1.55rem;
  font-weight: 700;
  letter-spacing: 0.02em;
}

.plat {
  background: #fff;
  border: 1px solid #e0e0e0;
  margin-bottom: 18px;
}

.hd {
  background: #1a1a1a;
  color: #ddd;
  padding: 0;
  font-size: 14px;
  display: flex;
  align-items: stretch;
  flex-wrap: wrap;
  min-height: 48px;
}

.hd-rules {
  display: flex;
  flex-wrap: wrap;
  align-items: stretch;
  width: 50%;
  min-width: 0;
  gap: 0;
}

.hd-rules button {
  flex: 1;
  min-width: max-content;
  padding: 14px 8px;
  font-size: 14px;
  color: #ccc;
  background: transparent;
  border: 0;
  cursor: pointer;
  font-family: inherit;
  text-align: center;
  white-space: nowrap;
}

.rule-picker {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
}

.rule-picker select {
  width: 200px;
  max-width: 100%;
  color: #eee;
  background: #2a2a2a;
  border: 1px solid #555;
  padding: 6px;
  font: inherit;
}

.hd-rules button:hover {
  color: #fff;
  background: #2a2a2a;
}

.hd-rules button.on {
  color: #fff;
  font-weight: 700;
  background: #409eff;
}

.bd {
  display: grid;
  grid-template-columns: 1fr 200px;
  min-height: 200px;
}

.stats-side {
  padding: 12px 14px;
}

.filter-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0;
  margin-bottom: 12px;
  border-bottom: 1px solid #eee;
}

.filter-sep {
  width: 1px;
  height: 18px;
  background: #e0e0e0;
  margin: 0 8px;
  flex-shrink: 0;
}

.tier-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 0;
  margin-bottom: 0;
  border-bottom: 0;
}

.tier-tabs.sub {
  border-bottom: 0;
}

.tier-tabs button {
  padding: 6px 12px;
  font-size: 13px;
  color: #666;
  background: transparent;
  border: 0;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  cursor: pointer;
  font-family: inherit;
}

.tier-tabs.sub button {
  font-size: 13px;
  padding: 6px 10px;
}

.tier-tabs button.on {
  color: #222;
  font-weight: 600;
  border-bottom-color: #409eff;
}

.tier-tabs button:disabled {
  opacity: 0.35;
  cursor: not-allowed;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 8px 12px;
}

.stats-cell {
  background: #fafafa;
  border: 1px solid #eee;
  padding: 8px 10px;
}

.stats-label {
  display: block;
  font-size: 12px;
  color: #888;
  margin-bottom: 2px;
}

.stats-value {
  font-size: 15px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: #222;
}

.tip {
  font-size: 12px;
  color: #999;
}

.query-side {
  background: #ff0000;
  color: #fff;
  padding: 18px 16px;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  gap: 12px;
}

.query-side h3 {
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0;
}

.query-side p {
  font-size: 12px;
  opacity: 0.92;
  line-height: 1.45;
  margin: 0;
}

.query-side .btn {
  display: inline-block;
  background: #fff;
  color: #ff0000;
  font-weight: 700;
  padding: 8px 16px;
  font-size: 13px;
  text-decoration: none;
}

.query-side .btn:hover {
  background: #ffe8e8;
}

.sec {
  margin-bottom: 18px;
}

.account-events .panel {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 16px 14px;
  min-height: 180px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  color: #333;
  box-shadow: none;
  text-decoration: none;
  box-sizing: border-box;
  min-width: 0;
}

.account-events .panel.platform-card {
  background: #007bff;
  border-color: #007bff;
  color: #fff;
  padding: 0;
  gap: 0;
  overflow: hidden;
}

.account-events .panel.platform-card .platform-card-main {
  display: flex;
  flex-direction: column;
  gap: 10px;
  flex: 1;
  padding: 16px 16px 10px;
  color: inherit;
  text-decoration: none;
  cursor: pointer;
  min-height: 0;
}

.account-events .panel.platform-card h3,
.account-events .panel.platform-card p {
  color: #fff;
}

.account-events .panel.platform-card p {
  opacity: 0.95;
  flex: 1;
}

.account-events .panel.platform-card .platform-2d-link {
  display: block;
  padding: 9px 16px;
  background: #f59e0b;
  color: #fff;
  font-size: 12px;
  line-height: 1.4;
  text-decoration: none;
  letter-spacing: 0.02em;
}

.account-events .panel.platform-card .platform-2d-link:hover {
  background: #d97706;
}

.account-events .panel h3 {
  margin: 0;
  font-size: 1.05rem;
}

.account-events .panel p {
  font-size: 13px;
  color: #666;
  margin: 0;
  opacity: 1;
  line-height: 1.5;
}

.panel-user {
  font-weight: 700;
  color: #222 !important;
}

.uid {
  font-weight: 400;
  color: #888;
  font-size: 12px;
}

.panel-btn {
  display: inline-block;
  align-self: flex-start;
  background: #409eff;
  color: #fff;
  padding: 7px 12px;
  font-size: 13px;
  font-weight: 700;
  text-decoration: none;
  border: 0;
  cursor: pointer;
  margin-top: auto;
}

.panel-btn.ghost {
  background: #fff;
  color: #409eff;
  border: 1px solid #409eff;
}

.panel-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: auto;
}

.panel-actions .panel-btn {
  margin-top: 0;
}

.apply-form {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.apply-form input,
.apply-form textarea {
  width: 100%;
  box-sizing: border-box;
  border: 1px solid #ddd;
  padding: 6px 8px;
  font: inherit;
  font-size: 13px;
}

.apply-form button {
  background: #ff0000;
  color: #fff;
  border: 0;
  padding: 7px 10px;
  font-weight: 700;
  cursor: pointer;
}

.apply-form button:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.mini-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.mini-item {
  display: flex;
  justify-content: space-between;
  gap: 8px;
  font-size: 12px;
  padding: 4px 0;
  border-bottom: 1px solid #f0f0f0;
  color: #444;
}

.mini-item.link {
  text-decoration: none;
}

.mini-item.link:hover .ev-name {
  color: #409eff;
}

.ev-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.mini-item .st {
  flex-shrink: 0;
  color: #888;
}

.mini-item .st.active {
  color: #067a3a;
}

.mini-item .st.registered {
  color: #606266;
}

.mini-item .st.closed {
  color: #c45656;
}

.err { color: #c00; font-size: 12px; margin: 0; }
.ok { color: #067a3a; font-size: 12px; margin: 0; }

.sec-h {
  background: rgba(0, 0, 0, 0.75);
  color: #fff;
  padding: 6px 12px;
  font-size: 13px;
  margin-bottom: 12px;
}

.grid {
  display: grid;
  gap: 16px;
}

.g3 {
  grid-template-columns: repeat(3, 1fr);
}

.g2 {
  grid-template-columns: repeat(2, 1fr);
}

.g4 {
  grid-template-columns: repeat(4, 1fr);
}

.card {
  display: block;
  padding: 22px 18px;
  min-height: 140px;
  color: #fff;
  text-decoration: none;
  box-shadow: 0 4px 14px rgba(0, 0, 0, 0.12);
}

.card:hover {
  filter: brightness(1.05);
}

.card.card-with-footer {
  display: flex;
  flex-direction: column;
  padding: 0;
  overflow: hidden;
}

.card.card-with-footer:hover {
  filter: none;
}

.card-with-footer .card-main {
  display: flex;
  flex: 1;
  flex-direction: column;
  padding: 22px 18px 14px;
  color: inherit;
  text-decoration: none;
}

.card-with-footer .card-main:hover {
  filter: brightness(1.05);
}

.card.card-placeholder {
  cursor: default;
  opacity: 0.92;
}

.card.card-placeholder:hover {
  filter: none;
}

.ad-slot {
  grid-column: span 2;
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 140px;
  border: 2px dashed #bdbdbd;
  background: #fff;
  color: #9a9a9a;
  font-size: 1.05rem;
  font-weight: 700;
  letter-spacing: 0.35em;
  text-indent: 0.35em;
  cursor: default;
  user-select: none;
}

.card h3 {
  font-size: 1.2rem;
  margin-bottom: 8px;
  font-weight: 700;
}

.card p {
  font-size: 13px;
  opacity: 0.95;
  line-height: 1.5;
}

@media (max-width: 900px) {
  .hd-rules {
    width: 100%;
  }

  .bd {
    grid-template-columns: 1fr;
  }

  .filter-sep {
    display: none;
  }

  .stats-grid {
    grid-template-columns: repeat(3, 1fr);
  }

  .g3,
  .g4,
  .g2 {
    grid-template-columns: 1fr 1fr;
  }
}

@media (max-width: 560px) {
  .welcome h1 {
    font-size: 1.25rem;
  }

  .hd-rules button {
    padding: 12px 6px;
    font-size: 13px;
  }

  .stats-grid {
    grid-template-columns: 1fr 1fr;
  }

  .g3,
  .g4,
  .g2 {
    grid-template-columns: 1fr 1fr;
  }

  .tier-tabs button {
    padding: 6px 8px;
  }
}
</style>
