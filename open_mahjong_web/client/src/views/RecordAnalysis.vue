<template>
  <div class="record-analysis">
    <div v-if="!authReady" class="ra-panel ra-status">正在确认登录状态…</div>
    <div v-else-if="!auth.isLoggedIn" class="ra-panel ra-status">
      <p>牌谱分析需要登录后使用。<br />下载的牌谱保存在本机，分析不消耗次数。</p>
      <el-button type="primary" size="small" @click="goLogin">前往登录</el-button>
    </div>
    <template v-else>
      <section class="ra-panel account-panel">
        <div class="account-meta">
          <div class="account-who">
            <span class="u-name">{{ auth.username || '本账号' }}</span>
            <span class="u-sep">·</span>
            <span class="u-id">ID {{ auth.userId }}</span>
          </div>
          <div class="quota-line">
            <span v-if="quota.unlimited">开发模式：下载不限局数</span>
            <span v-else>今日下载 {{ quota.used }} / {{ quota.max }} 局（凌晨 4 点刷新）</span>
            <div class="quota-meter" :title="quotaMeterTitle">
              <div class="quota-fill" :style="{ width: quotaPercent + '%' }" />
            </div>
            <el-tooltip content="赞助者每日可下载1000个牌谱" placement="top">
              <el-button
                class="quota-help-button"
                text
                circle
                :icon="QuestionFilled"
                aria-label="下载限额说明"
              />
            </el-tooltip>
          </div>
        </div>
        <div class="player-search-bar">
          <span class="search-label">玩家</span>
          <el-input
            v-model="searchKey"
            class="player-search-input"
            placeholder="输入 ID 或用户名，默认自己"
            clearable
            size="small"
            @keyup.enter="searchTargetPlayer"
          />
          <el-button type="primary" size="small" :loading="searchingPlayer" @click="searchTargetPlayer">查询</el-button>
          <el-button size="small" :disabled="!viewingOther" @click="selectTargetPlayer(auth.userId)">回到自己</el-button>
        </div>
        <div class="cached-players">
          <span class="cached-label">已下载牌谱的玩家</span>
          <div class="cached-chips">
            <button
              v-for="p in playerChips"
              :key="p.userId"
              class="chip"
              :class="{ selected: Number(p.userId) === Number(targetUserId) }"
              @click="selectTargetPlayer(p.userId)"
            >{{ p.isSelf ? '自己' : p.username }}<span class="chip-count">{{ p.count }}</span></button>
            <span v-if="playerChips.length <= 1 && !cachedPlayers.length" class="cached-empty">本机还没有缓存牌谱</span>
          </div>
        </div>
        <div class="target-meta">
          <span class="u-name">{{ targetName }}</span>
          <span class="u-sep">·</span>
          <span class="u-id">ID {{ targetUserId }}</span>
          <span class="u-sep">·</span>
          <span class="u-games">全部对局 {{ targetGames }} 局</span>
          <span v-if="viewingOther" class="other-note">下载计入本账号配额</span>
        </div>
        <div class="account-strip-label">{{ viewingOther ? `${targetName} 全部对局` : '本账号全部对局' }}</div>
        <RecordBarStrip :bars="targetBars" compact />
      </section>

      <section class="ra-panel filter-panel">
        <div class="filter-row">
          <button
            v-for="rule in availableRules"
            :key="rule.key"
            class="chip"
            :class="{ selected: currentRule === rule.key }"
            @click="switchRule(rule.key)"
          >{{ rule.label }}<span class="chip-count">{{ rule.count }}</span></button>
        </div>
        <div class="filter-row with-date scene-filter-row">
          <div class="tier-group">
            <button
              v-for="s in SCENE_OPTIONS"
              :key="s.value"
              class="chip"
              :class="{ selected: scene === s.value }"
              @click="selectScene(s.value)"
            >{{ s.label }}<span class="chip-count">{{ sceneCount(s.value) }}</span></button>
            <el-select
              v-if="scene === 'events'"
              v-model="selectedEventId"
              size="small"
              clearable
              filterable
              placeholder="全部赛事"
              class="event-select"
              @change="onFilterChange"
            >
              <el-option
                v-for="ev in eventOptions"
                :key="ev.event_id"
                :label="eventOptionLabel(ev)"
                :value="ev.event_id"
              />
            </el-select>
          </div>
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            size="small"
            range-separator="—"
            start-placeholder="开始日期"
            end-placeholder="结束日期"
            value-format="YYYY-MM-DD"
            class="filter-date"
            @change="onFilterChange"
          />
        </div>
        <div class="filter-row">
          <button class="chip" :class="{ selected: length === null }" @click="selectLength(null)">全部局制</button>
          <button
            v-for="l in LENGTH_OPTIONS"
            :key="l.value"
            class="chip"
            :class="{ selected: length === l.value }"
            @click="selectLength(l.value)"
          >{{ l.label }}</button>
        </div>

        <div class="bar-box">
          <div class="bar-box-head">
            <div class="bar-box-head-row">
              <span>当前筛选 {{ recordItems.length }} 局 · 已下载 {{ localCount }} 局</span>
              <div class="bar-box-actions">
                <el-button
                  type="primary"
                  size="small"
                  :loading="downloading"
                  :disabled="missingCount === 0 || downloading || zipping"
                  title="按当前筛选把尚未下载的牌谱全部拉到本机"
                  @click="downloadAllFiltered"
                >下载筛选剩余 {{ missingCount }} 局</el-button>
                <el-button
                  size="small"
                  :loading="zipping"
                  :disabled="localCount === 0 || downloading || zipping"
                  title="把当前筛选里已下载的牌谱打成 ZIP（本机打包，不占配额）"
                  @click="zipDownloadedFiltered"
                >打包已下载 ZIP</el-button>
              </div>
            </div>
            <span class="bar-legend">
              <span class="lg"><i class="lg-bar h" />100 局</span>
              <span class="lg"><i class="lg-bar t" />10 局</span>
              <span class="lg"><i class="lg-bar o" />1 局</span>
              <span class="lg-note">高度 100 / 75 / 50 · 色块从下往上为已下载比例</span>
            </span>
          </div>
          <div v-if="idsLoading" class="bar-empty">正在载入牌谱列表…</div>
          <div v-else-if="!recordItems.length" class="bar-empty">当前筛选下没有对局</div>
          <RecordBarStrip
            v-else
            :bars="filterBars"
            interactive
            :disabled="downloading"
            @select="onSelectBar"
          />
        </div>
      </section>

      <section class="ra-panel tools-panel">
        <div class="section-title">分析工具</div>
        <div class="tool-row">
          <div class="tool-card" :class="{ active: resultTab === 'standard' }">
            <div class="tool-name">标准分析</div>
            <p class="tool-desc">对当前筛选中已下载的牌谱计算顺位、和牌率、副露等常规统计。未下载的局不会计入。</p>
            <div class="tool-actions">
              <el-button
                type="primary"
                size="small"
                :loading="analyzingKind === 'standard'"
                :disabled="localCount === 0 || !!analyzingKind"
                @click="runStandardAnalysis"
              >分析当前筛选</el-button>
              <span class="tool-hint">将分析 {{ localCount }} / {{ recordItems.length }} 局</span>
            </div>
          </div>
          <div class="tool-card" :class="{ active: resultTab === 'advanced' }">
            <div class="tool-name">高级分析</div>
            <p class="tool-desc">平均首次听牌巡、和牌张/听牌张次数与频率、主番（4 番及以上）、凑番（全部 1–2 番且不含门断平）、门断平、和牌时明副露次数（暗杠不计）、点炮均番均巡。目前仅国标。</p>
            <div class="tool-actions">
              <el-button
                type="primary"
                size="small"
                :loading="analyzingKind === 'advanced'"
                :disabled="localCount === 0 || !!analyzingKind || !isGuobiao"
                @click="runAdvancedAnalysis"
              >分析当前筛选</el-button>
              <span class="tool-hint">{{ advancedHint }}</span>
            </div>
          </div>
          <div class="tool-card" :class="{ active: resultTab === 'fan' }">
            <div class="tool-name">按番查谱</div>
            <p class="tool-desc">选出一个番种（含凑番、门断平），列出和出该番的牌谱、小局、对局时间、巡目与 node，复制 2D 回放链接分享。目前仅国标。</p>
            <div class="tool-actions fan-actions">
              <el-select
                v-model="selectedFanKey"
                size="small"
                filterable
                placeholder="选择番种"
                class="fan-select"
                :disabled="!isGuobiao"
              >
                <el-option
                  v-for="opt in FAN_SEARCH_OPTIONS"
                  :key="opt.key"
                  :label="opt.label"
                  :value="opt.key"
                />
              </el-select>
              <el-button
                type="primary"
                size="small"
                :loading="analyzingKind === 'fan'"
                :disabled="localCount === 0 || !!analyzingKind || !isGuobiao || !selectedFanKey"
                @click="runFanSearch"
              >查询</el-button>
            </div>
          </div>
        </div>
      </section>

      <section v-if="hasAnyResult" class="ra-panel result-panel">
        <div class="result-tabs">
          <button
            v-if="analyzedStats"
            class="chip"
            :class="{ selected: resultTab === 'standard' }"
            @click="resultTab = 'standard'"
          >标准分析</button>
          <button
            v-if="advancedStats"
            class="chip"
            :class="{ selected: resultTab === 'advanced' }"
            @click="resultTab = 'advanced'"
          >高级分析</button>
          <button
            v-if="fanQueried"
            class="chip"
            :class="{ selected: resultTab === 'fan' }"
            @click="resultTab = 'fan'"
          >按番查谱</button>
        </div>

        <template v-if="resultTab === 'standard' && analyzedStats">
          <div class="section-title">分析结果</div>
          <div class="stats-table">
            <div class="stats-row" v-for="item in statsDisplay" :key="item.label">
              <span class="stats-label">{{ item.label }}</span>
              <span class="stats-value">{{ item.value }}</span>
            </div>
          </div>
          <div class="charts-panel">
            <div class="chart-box chart-pie">
              <div class="chart-title">顺位分布</div>
              <div class="pie-wrap">
                <svg viewBox="0 0 100 100" class="pie-svg">
                  <circle cx="50" cy="50" r="40" fill="none" stroke="#eef0f3" stroke-width="18" />
                  <circle
                    v-for="seg in pieSegments"
                    :key="seg.key"
                    cx="50" cy="50" r="40" fill="none"
                    :stroke="seg.color" stroke-width="18"
                    :stroke-dasharray="seg.dash"
                    :stroke-dashoffset="seg.offset"
                    transform="rotate(-90 50 50)"
                  />
                  <text x="50" y="54" class="pie-center" text-anchor="middle">{{ pieTotal }}</text>
                </svg>
                <div class="pie-legend">
                  <div v-for="seg in pieSegments" :key="seg.key" class="legend-item">
                    <span class="legend-dot" :style="{ background: seg.color }"></span>
                    <span class="legend-label">{{ seg.label }}（{{ seg.value }}）</span>
                    <span class="legend-pct">{{ seg.pct }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <el-collapse v-if="isGuobiao && standardFanEntries.length" class="fan-collapse">
            <el-collapse-item :title="`番种统计（${standardFanEntries.length}）`" name="fan">
              <div class="fan-grid">
                <div v-for="item in standardFanEntries" :key="item.key" class="fan-item">
                  <span class="fan-name">{{ item.name }}<span class="fan-pts">（{{ item.value }} 番）</span></span>
                  <span class="fan-value">{{ getStandardFanValue(item.key) }}</span>
                </div>
              </div>
            </el-collapse-item>
          </el-collapse>
        </template>

        <template v-else-if="resultTab === 'advanced' && advancedStats">
          <div class="section-title">高级分析</div>
          <p class="result-note">主番统计的是单番种 ≥ 4 番；全部由 1–2 番番种构成、且不是门断平的和牌记为凑番。门断平（门前清+断幺+平和）单独统计。明副露次数不含暗杠，加杠并入原碰。</p>
          <div class="stats-table">
            <div class="stats-row" v-for="item in advancedMetricRows" :key="item.label">
              <span class="stats-label">{{ item.label }}</span>
              <span class="stats-value">{{ item.value }}</span>
            </div>
          </div>
          <div class="adv-grid">
            <div class="chart-box">
              <div class="chart-title">点炮（放铳）</div>
              <div class="stats-table nested">
                <div class="stats-row" v-for="item in dealInRows" :key="item.label">
                  <span class="stats-label">{{ item.label }}</span>
                  <span class="stats-value">{{ item.value }}</span>
                </div>
              </div>
            </div>
            <div class="chart-box">
              <div class="chart-title">和牌时明副露次数</div>
              <div class="bar-list">
                <div
                  v-for="row in fuluWinRows"
                  :key="row.label"
                  class="bar-row"
                >
                  <span class="bar-lab">{{ row.label }}</span>
                  <span class="bar-track"><i :style="{ width: row.width }" /></span>
                  <span class="bar-num">{{ row.count }} · {{ row.pct }}</span>
                </div>
              </div>
            </div>
          </div>
          <div class="chart-box fan-box">
            <div class="chart-title">主番分布（点击可按该番查谱）</div>
            <div v-if="!mainFanRows.length" class="bar-empty">没有和牌记录</div>
            <div v-else class="bar-list">
              <button
                v-for="row in mainFanRows"
                :key="row.key"
                class="bar-row clickable"
                :class="{ coufan: row.isCoufan, menduanping: row.isMenduanping }"
                type="button"
                @click="openFanFromAdvanced(row)"
              >
                <span class="bar-lab">{{ row.label }}</span>
                <span class="bar-track"><i :style="{ width: row.width }" /></span>
                <span class="bar-num">{{ row.count }} · {{ row.pct }}</span>
              </button>
            </div>
          </div>
          <div class="adv-grid">
            <div class="chart-box">
              <div class="chart-title">和牌张 · 次数 / 占和牌</div>
              <div class="tile-freq">
                <div v-for="(row, ri) in huTileRows" :key="'hu'+ri" class="tile-freq-row">
                  <div v-for="cell in row" :key="cell.id" class="tile-freq-cell" :class="{ zero: !cell.count }">
                    <TileMiniGlyph :tile-id="cell.id" />
                    <span class="tile-n">{{ cell.count }}</span>
                    <span class="tile-p">{{ cell.pct }}</span>
                  </div>
                </div>
              </div>
            </div>
            <div class="chart-box">
              <div class="chart-title">听牌张 · 首次听牌时的待牌 / 占听牌局</div>
              <div class="tile-freq">
                <div v-for="(row, ri) in waitTileRows" :key="'wt'+ri" class="tile-freq-row">
                  <div v-for="cell in row" :key="cell.id" class="tile-freq-cell" :class="{ zero: !cell.count }">
                    <TileMiniGlyph :tile-id="cell.id" />
                    <span class="tile-n">{{ cell.count }}</span>
                    <span class="tile-p">{{ cell.pct }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </template>

        <template v-else-if="resultTab === 'fan' && fanQueried">
          <div class="section-title">按番查谱 · {{ fanQueryLabel }}</div>
          <p class="result-note">共 {{ fanHits.length }} 次和牌。分享链接格式为 /2d/record/牌谱ID?round=第几局&amp;node=节点（局从 1 计，node 从 0 计）。</p>
          <div v-if="!fanHits.length" class="bar-empty">当前筛选的已下载牌谱里没有这个番。</div>
          <div v-else class="fan-table-wrap">
            <table class="fan-table">
              <thead>
                <tr>
                  <th>对局时间</th>
                  <th>牌谱</th>
                  <th>小局</th>
                  <th>巡</th>
                  <th>node</th>
                  <th>番数</th>
                  <th>番种</th>
                  <th>和牌张</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(row, idx) in pagedFanHits" :key="row.game_id + '-' + row.round + '-' + row.node + '-' + idx">
                  <td class="mono">{{ formatWinTime(row.created_at) }}</td>
                  <td class="mono">{{ shortId(row.game_id) }}</td>
                  <td>第 {{ row.round }} 局</td>
                  <td>{{ row.xunmu }}</td>
                  <td class="mono">{{ row.node }}</td>
                  <td>{{ row.fanScore }}</td>
                  <td class="yaku-cell">{{ row.yakuText }}</td>
                  <td><TileMiniGlyph v-if="row.winTile" :tile-id="row.winTile" /></td>
                  <td class="fan-ops">
                    <a :href="sharePathForWin(row)" target="_blank" rel="noopener">打开</a>
                    <button type="button" class="linkish" @click="copyWinLink(row)">复制链接</button>
                  </td>
                </tr>
              </tbody>
            </table>
            <el-pagination
              v-if="fanHits.length > fanPageSize"
              class="fan-pager"
              small
              background
              layout="prev, pager, next, total"
              :total="fanHits.length"
              :page-size="fanPageSize"
              :current-page="fanPage"
              @current-change="fanPage = $event"
            />
          </div>
        </template>

        <div v-if="resultTab === 'standard' || resultTab === 'advanced'" class="records-section">
          <div class="records-head">
            <span class="section-title">对局记录</span>
            <span class="records-total">共 {{ recordsTotal }} 局</span>
          </div>
          <el-table
            :data="gameRecords"
            size="small"
            class="records-table"
            v-loading="recordsLoading"
            empty-text="暂无对局记录"
          >
            <el-table-column label="牌谱 ID" min-width="140">
              <template #default="{ row }">
                <span class="cell-game-id" :title="row.game_id">{{ row.game_id }}</span>
              </template>
            </el-table-column>
            <el-table-column label="时间" width="150">
              <template #default="{ row }">
                <span class="cell-time">{{ formatRecordDate(row.created_at) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="场次" min-width="120">
              <template #default="{ row }">
                <span class="cell-scene">{{ sceneLabel(row) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="局制" width="80">
              <template #default="{ row }">
                <span class="cell-mode">{{ gameTypeLabel(row.match_type) }}</span>
              </template>
            </el-table-column>
            <el-table-column label="顺位" width="64">
              <template #default="{ row }">
                <span class="rank-badge" :class="`rank-${myRank(row)}`">{{ myRank(row) || '-' }}</span>
              </template>
            </el-table-column>
            <el-table-column label="得分" width="80">
              <template #default="{ row }">
                <span class="cell-score" :class="scoreClass(myScore(row))">
                  {{ formatScore(myScore(row)) }}
                </span>
              </template>
            </el-table-column>
            <el-table-column label="同桌" min-width="180">
              <template #default="{ row }">
                <el-tooltip effect="dark" placement="top">
                  <template #content>
                    <div v-for="p in row.players" :key="p.user_id" class="tip-player">
                      <span class="rank-badge" :class="`rank-${p.rank}`">{{ p.rank }}</span>
                      {{ p.username }}
                      <span :class="p.score > 0 ? 'pos' : (p.score < 0 ? 'neg' : '')">{{ p.score > 0 ? '+' : '' }}{{ p.score }}</span>
                    </div>
                  </template>
                  <span class="cell-players">{{ playersSummary(row) }}</span>
                </el-tooltip>
              </template>
            </el-table-column>
            <el-table-column label="操作" width="168" fixed="right">
              <template #default="{ row }">
                <el-button
                  v-if="row.rule === 'guobiao'"
                  link
                  type="warning"
                  size="small"
                  tag="a"
                  :href="`/2d/record/${encodeURIComponent(row.game_id)}`"
                  target="_blank"
                  rel="noopener noreferrer"
                >2D</el-button>
                <el-button
                  link
                  type="success"
                  size="small"
                  tag="a"
                  :href="`/game-unity?recordId=${encodeURIComponent(row.game_id)}`"
                  target="_blank"
                  rel="noopener noreferrer"
                >3D</el-button>
                <el-button link type="primary" size="small" @click="downloadRecordJson(row.game_id)">JSON</el-button>
              </template>
            </el-table-column>
          </el-table>
          <div class="records-foot">
            <el-pagination
              v-model:current-page="recordsPage.current"
              v-model:page-size="recordsPage.size"
              :total="recordsTotal"
              :page-sizes="[20, 50]"
              layout="prev, pager, next, sizes, total"
              small
              background
              @current-change="loadResultRecords"
              @size-change="onRecordsSizeChange"
            />
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { QuestionFilled } from '@element-plus/icons-vue'
import axios from 'axios'
import playerApi from '@/api/playerClient'
import { usePlayerAuthStore } from '@/stores/playerAuth'
import { analyzeRecords } from '../utils/recordAnalyzer'
import {
  analyzeRecordsAdvanced,
  buildMainFanRows,
  buildTileFreqRows,
  COUFAN_KEY,
  MENDUANPING_KEY,
  FAN_SEARCH_OPTIONS,
  filterWinsByFan,
  sharePathForWin,
  sortWinsByTimeDesc,
} from '../utils/recordAdvancedAnalyzer'
import { buildPlayerStatsRows, rankRatePieLabel, rankedGames, ratio, avg } from '../utils/statsDisplay'
import { listGuobiaoFanEntries } from '../constants/guobiaoFanDict'
import { barsFromCount, barsFromItems } from '../utils/recordBarUnits'
import { getLocalRecordIdSet, getLocalRecords, putLocalRecords, listCachedPlayers } from '../utils/recordLocalStore'
import { zipStoreFiles } from '../utils/zipStore'
import RecordBarStrip from '../components/RecordBarStrip.vue'
import TileMiniGlyph from '../components/TileMiniGlyph.vue'

const route = useRoute()
const router = useRouter()
const auth = usePlayerAuthStore()
const authReady = ref(false)

const RULE_DEFS = [
  { key: 'guobiao', label: '国标', statsField: 'guobiao_stats' },
  { key: 'riichi', label: '立直', statsField: 'riichi_stats' },
  { key: 'qingque', label: '青雀', statsField: 'qingque_stats' },
  { key: 'classical', label: '古典', statsField: 'classical_stats' },
  { key: 'sichuan', label: '川麻', statsField: 'sichuan_stats' },
  { key: 'changsha', label: '长沙', statsField: 'changsha_stats' },
]
const SCENE_OPTIONS = [
  { value: 'rank', label: '全部天梯' },
  { value: 'beginner', label: '初级' },
  { value: 'intermediate', label: '中级' },
  { value: 'advanced', label: '高级' },
  { value: 'mcrpl', label: 'mcrpl' },
  { value: 'custom', label: '自定义' },
  { value: 'events', label: '比赛场' },
]
const LENGTH_OPTIONS = [
  { value: '4', label: '全庄战' },
  { value: '3', label: '东西战' },
  { value: '2', label: '半庄战' },
  { value: '1', label: '东风战' },
]
const LENGTH_TO_GAME_TYPE = { '4': 'quanzhuang', '3': 'xifeng', '2': 'banzhuang', '1': 'dongfeng' }
const MODE_LABELS = { '4/4': '全庄战', '3/4': '东西战', '2/4': '半庄战', '1/4': '东风战' }
const TIER_LABELS = {
  beginner: '初级',
  intermediate: '中级',
  advanced: '高级',
  mcrpl: 'mcrpl',
}

const quota = ref({ used: 0, max: 200, remaining: 200, unlimited: false, account_games: 0 })
const playerInfo = ref(null)
const searchKey = ref('')
const searchingPlayer = ref(false)
const cachedPlayers = ref([])
const currentRule = ref('guobiao')
const scene = ref('rank')
const length = ref(null)
const selectedEventId = ref(null)
const eventOptions = ref([])
const dateRange = ref(null)
const scopeCountsFromApi = ref(null)
const recordItems = ref([])
const localIds = ref(new Set())
const idsLoading = ref(false)
const downloading = ref(false)
const zipping = ref(false)
const analyzingKind = ref('')
const analyzingProgress = ref('')
const analyzedStats = ref(null)
const analyzedFilterKey = ref('')
const advancedStats = ref(null)
const advancedFilterKey = ref('')
const advancedHasTenpai = ref(false)
const winEvents = ref([])
const winEventsKey = ref('')
const selectedFanKey = ref(COUFAN_KEY)
const fanHits = ref([])
const fanQueried = ref(false)
const fanPage = ref(1)
const resultTab = ref('')
const fanPageSize = 30
const gameRecords = ref([])
const recordsTotal = ref(0)
const recordsLoading = ref(false)
const recordsLoadedKey = ref('')
const recordsPage = reactive({ current: 1, size: 20 })
let idsSeq = 0
let recordsSeq = 0
let autoDownloadStarted = false

const queryStr = (key) => {
  const v = route.query[key]
  return Array.isArray(v) ? v[0] : v
}

const targetUserId = computed(() => {
  const q = queryStr('q') || queryStr('player')
  if (q && /^\d+$/.test(String(q))) return Number(q)
  return auth.userId
})

const viewingOther = computed(() =>
  auth.userId != null && targetUserId.value != null && Number(targetUserId.value) !== Number(auth.userId)
)
const targetName = computed(() =>
  playerInfo.value?.user_settings?.username || (viewingOther.value ? '该玩家' : auth.username)
)
const accountGames = computed(() => Number(quota.value.account_games) || 0)
const targetGames = computed(() => {
  const info = playerInfo.value
  if (!info) return viewingOther.value ? 0 : accountGames.value
  return RULE_DEFS.reduce((sum, def) => (
    sum + (info[def.statsField] || []).reduce((s, row) => s + (Number(row.total_games) || 0), 0)
  ), 0)
})
const targetBars = computed(() => barsFromCount(targetGames.value))
const playerChips = computed(() => {
  const selfId = Number(auth.userId)
  const selfCached = cachedPlayers.value.find((p) => Number(p.userId) === selfId)
  const chips = [{
    userId: selfId,
    username: auth.username || '自己',
    count: selfCached?.count || 0,
    isSelf: true,
  }]
  for (const p of cachedPlayers.value) {
    if (Number(p.userId) === selfId) continue
    chips.push({ ...p, isSelf: false })
  }
  return chips
})
const quotaPercent = computed(() => {
  if (quota.value.unlimited) return 0
  const max = Number(quota.value.max) || 200
  return Math.min(100, Math.round(((Number(quota.value.used) || 0) / max) * 100))
})
const quotaMeterTitle = computed(() =>
  quota.value.unlimited ? '不限' : `剩余 ${quota.value.remaining} 局`
)
const localCount = computed(() =>
  recordItems.value.filter((row) => localIds.value.has(String(row.game_id))).length
)
const missingCount = computed(() =>
  recordItems.value.filter((row) => !localIds.value.has(String(row.game_id))).length
)
const filterBars = computed(() => barsFromItems(recordItems.value, localIds.value))
const filterKey = computed(() => JSON.stringify({
  user: targetUserId.value,
  rule: currentRule.value,
  scene: scene.value,
  length: length.value,
  event_id: selectedEventId.value,
  date: dateRange.value,
}))
const statsDisplay = computed(() =>
  analyzedStats.value ? buildPlayerStatsRows(analyzedStats.value) : []
)

const RANK_COLORS = { 1: '#6fd86f', 2: '#5dadff', 3: '#aab4c2', 4: '#ff7a7a' }
const PIE_LABELS = { 1: '一位', 2: '二位', 3: '三位', 4: '四位' }
const pieSegments = computed(() => {
  const s = analyzedStats.value
  if (!s) return []
  const counts = [
    { key: 1, value: s.first_place_count || 0 },
    { key: 2, value: s.second_place_count || 0 },
    { key: 3, value: s.third_place_count || 0 },
    { key: 4, value: s.fourth_place_count || 0 },
  ]
  const total = rankedGames(s)
  const C = 2 * Math.PI * 40
  let acc = 0
  return counts.map((c) => {
    const frac = total > 0 ? c.value / total : 0
    const len = frac * C
    const seg = {
      key: c.key,
      label: PIE_LABELS[c.key],
      value: c.value,
      color: RANK_COLORS[c.key],
      dash: `${len} ${C - len}`,
      offset: -acc,
      pct: rankRatePieLabel(c.value, s),
    }
    acc += len
    return seg
  })
})
const pieTotal = computed(() => rankedGames(analyzedStats.value))
const isGuobiao = computed(() => currentRule.value === 'guobiao')
const standardFanEntries = computed(() => (
  isGuobiao.value ? listGuobiaoFanEntries() : []
))
const getStandardFanValue = (fanKey) => analyzedStats.value?.fan_stats?.[fanKey] || 0
const hasAnyResult = computed(() =>
  !!analyzedStats.value || !!advancedStats.value || fanQueried.value
)
const advancedHint = computed(() => {
  if (!isGuobiao.value) return '目前仅国标规则'
  if (analyzingKind.value === 'advanced' && analyzingProgress.value) {
    return analyzingProgress.value
  }
  return `将分析 ${localCount.value} / ${recordItems.value.length} 局`
})
const fanQueryLabel = computed(() => {
  const opt = FAN_SEARCH_OPTIONS.find((item) => item.key === selectedFanKey.value)
  return opt?.label || '未选择'
})
const pagedFanHits = computed(() => {
  const start = (fanPage.value - 1) * fanPageSize
  return fanHits.value.slice(start, start + fanPageSize)
})
const advancedMetricRows = computed(() => {
  const s = advancedStats.value
  if (!s) return []
  return [
    { label: '总对局', value: String(s.total_games || 0) },
    { label: '总回合', value: String(s.total_rounds || 0) },
    { label: '和牌次数', value: String(s.win_count || 0) },
    { label: '听牌率', value: ratio(s.tenpai_round_count, s.total_rounds) },
    { label: '平均首次听牌巡', value: avg(s.total_first_tenpai_turn, s.tenpai_round_count) },
    { label: '荒庄率', value: ratio(s.liuju_count, s.total_rounds) },
    { label: '门清和了率', value: ratio(s.closed_win_count, s.win_count) },
    { label: '凑番占和牌', value: ratio(s.coufan_count, s.win_count) },
    { label: '门断平占和牌', value: ratio(s.menduanping_count, s.win_count) },
    { label: '平均和番', value: avg(s.total_win_fan, s.win_count) },
    { label: '平均和了点数', value: avg(s.total_win_score, s.win_count) },
  ]
})
const dealInRows = computed(() => {
  const s = advancedStats.value
  if (!s) return []
  return [
    { label: '点炮次数', value: String(s.deal_in_count || 0) },
    { label: '点炮率', value: ratio(s.deal_in_count, s.total_rounds) },
    { label: '平均点炮番', value: avg(s.total_deal_in_fan, s.deal_in_count) },
    { label: '平均点炮失分', value: avg(s.total_deal_in_score, s.deal_in_count) },
    { label: '平均点炮巡目', value: avg(s.total_deal_in_turn, s.deal_in_count) },
  ]
})
const FULU_WIN_LABELS = ['门清（0 次）', '1 次', '2 次', '3 次', '4 次']
const fuluWinRows = computed(() => {
  const s = advancedStats.value
  if (!s) return []
  const max = Math.max(1, ...(s.fulu_at_win || [0]))
  return FULU_WIN_LABELS.map((label, i) => {
    const count = s.fulu_at_win?.[i] || 0
    return {
      label,
      count,
      pct: ratio(count, s.win_count),
      width: `${(count / max) * 100}%`,
    }
  })
})
const mainFanRows = computed(() => {
  const rows = buildMainFanRows(advancedStats.value)
  const max = Math.max(1, ...rows.map((row) => row.count))
  return rows.map((row) => ({
    ...row,
    pct: `${row.percent.toFixed(2)}%`,
    width: `${(row.count / max) * 100}%`,
  }))
})
const fmtTilePct = (n) => `${n.toFixed(1)}%`
const huTileRows = computed(() =>
  buildTileFreqRows(advancedStats.value?.hu_tile_counts, advancedStats.value?.win_count)
    .map((row) => row.map((cell) => ({ ...cell, pct: fmtTilePct(cell.percent) })))
)
const waitTileRows = computed(() =>
  buildTileFreqRows(advancedStats.value?.wait_tile_counts, advancedStats.value?.tenpai_round_count)
    .map((row) => row.map((cell) => ({ ...cell, pct: fmtTilePct(cell.percent) })))
)

const availableRules = computed(() =>
  RULE_DEFS.map((def) => ({
    key: def.key,
    label: def.label,
    count: (playerInfo.value?.[def.statsField] || []).reduce((s, x) => s + (x.total_games || 0), 0),
  }))
)

const sceneCount = (s) => {
  const c = scopeCountsFromApi.value
  if (c && c[s] != null) return c[s]
  return 0
}

const sceneToFilter = (s = scene.value) => {
  if (s === 'rank') return { scope: 'rank', tier: null }
  if (s === 'custom') return { scope: 'custom', tier: null }
  if (s === 'events') return { scope: 'all', tier: 'events' }
  return { scope: 'rank', tier: s }
}

const filterPayload = () => {
  const { scope, tier } = sceneToFilter()
  const payload = { rule: currentRule.value }
  if (length.value) payload.game_type = LENGTH_TO_GAME_TYPE[length.value]
  if (dateRange.value && dateRange.value.length === 2) {
    payload.date_from = dateRange.value[0] + 'T00:00:00'
    const end = new Date(dateRange.value[1])
    end.setDate(end.getDate() + 1)
    payload.date_to = end.toISOString().slice(0, 19)
  }
  if (tier) payload.tier = tier
  else if (scope === 'custom') payload.tier = 'custom'
  else if (scope === 'rank') payload.tier = 'rank'
  if (tier === 'events' && selectedEventId.value) payload.event_id = selectedEventId.value
  return payload
}

const goLogin = () => {
  router.push({ path: '/login', query: { redirect: route.fullPath } })
}

const applyQuota = (data) => {
  if (!data) return
  quota.value = {
    used: Number(data.used) || 0,
    max: Number(data.max) || 200,
    remaining: data.unlimited
      ? Math.max(0, Number(data.max) || 200)
      : Math.max(0, Number(data.remaining) || 0),
    unlimited: !!data.unlimited,
    account_games: data.account_games != null
      ? Number(data.account_games)
      : (quota.value.account_games || 0),
  }
}

const loadQuota = async () => {
  try {
    const resp = await playerApi.get('/download-quota')
    if (resp.data.success) applyQuota(resp.data.data)
  } catch (e) {
    if (e.response?.status === 401) return
  }
}

const eventOptionLabel = (ev) => {
  const status = ev.status === 'closed' ? '（已关闭）' : ev.status === 'registered' ? '（已注册）' : ''
  return `${ev.name}${status}`
}

const loadEventOptions = async () => {
  try {
    const resp = await axios.get('/api/player/events')
    if (resp.data.success) eventOptions.value = resp.data.data?.items || []
  } catch (_) {
    eventOptions.value = []
  }
}

const loadPlayerInfo = async () => {
  const uid = targetUserId.value
  if (uid == null) return
  try {
    const resp = await axios.get(`/api/player/info/${uid}`)
    if (resp.data.success) {
      const hadInfo = !!playerInfo.value
      playerInfo.value = resp.data.data
      const name = playerInfo.value?.user_settings?.username
      searchKey.value = name || String(uid)
      const def = RULE_DEFS.find((d) => (playerInfo.value[d.statsField] || []).some((r) => (r.total_games || 0) > 0))
      if (def && !queryStr('rule') && !hadInfo) currentRule.value = def.key
    }
  } catch (_) {
    playerInfo.value = null
  }
}

const refreshCachedPlayers = async () => {
  try {
    cachedPlayers.value = await listCachedPlayers()
  } catch (_) {
    cachedPlayers.value = []
  }
}

const selectTargetPlayer = (userId) => {
  const uid = Number(userId)
  if (!Number.isFinite(uid) || uid <= 0) return
  if (Number(uid) === Number(targetUserId.value)) return
  const nextQuery = { ...route.query }
  if (Number(uid) === Number(auth.userId)) delete nextQuery.q
  else nextQuery.q = String(uid)
  delete nextQuery.player
  router.replace({ path: route.path, query: nextQuery })
}

const searchTargetPlayer = async () => {
  const raw = String(searchKey.value || '').trim()
  if (!raw) {
    ElMessage.error('请输入玩家 ID 或用户名')
    return
  }
  if (/^\d+$/.test(raw) && Number(raw) === Number(targetUserId.value)) return
  searchingPlayer.value = true
  try {
    const resp = await axios.get(`/api/player/info/${encodeURIComponent(raw)}`)
    if (!resp.data.success || resp.data.data?.user_id == null) {
      ElMessage.error(resp.data.message || '未找到该玩家')
      return
    }
    selectTargetPlayer(resp.data.data.user_id)
  } catch (e) {
    ElMessage.error(e?.response?.data?.message || '查询玩家失败')
  } finally {
    searchingPlayer.value = false
  }
}

const loadScopeCounts = async () => {
  const uid = targetUserId.value
  if (uid == null) {
    scopeCountsFromApi.value = null
    return
  }
  const params = { rule: currentRule.value }
  if (length.value) params.game_type = LENGTH_TO_GAME_TYPE[length.value]
  if (dateRange.value && dateRange.value.length === 2) {
    params.date_from = dateRange.value[0] + 'T00:00:00'
    const end = new Date(dateRange.value[1])
    end.setDate(end.getDate() + 1)
    params.date_to = end.toISOString().slice(0, 19)
  }
  try {
    const resp = await axios.get(`/api/player/scope-counts/${uid}`, { params })
    scopeCountsFromApi.value = resp.data.success ? resp.data.data : null
  } catch (_) {
    scopeCountsFromApi.value = null
  }
}

const refreshLocalIds = async () => {
  const ids = recordItems.value.map((row) => String(row.game_id))
  try {
    localIds.value = await getLocalRecordIdSet(ids)
  } catch (_) {
    localIds.value = new Set()
  }
}

const loadRecordIds = async () => {
  const uid = targetUserId.value
  if (uid == null) return
  const seq = ++idsSeq
  idsLoading.value = true
  try {
    const resp = await axios.get(`/api/player/record-ids/${uid}`, { params: filterPayload() })
    if (seq !== idsSeq) return
    recordItems.value = resp.data.success ? (resp.data.data?.items || []) : []
    await refreshLocalIds()
  } catch (_) {
    if (seq !== idsSeq) return
    recordItems.value = []
    localIds.value = new Set()
  } finally {
    if (seq === idsSeq) idsLoading.value = false
  }
}

const handleDownloadError = (err, fallback) => {
  const status = err?.response?.status
  const data = err?.response?.data
  if (status === 401) {
    ElMessage.warning('请先登录后再下载牌谱')
    goLogin()
    return
  }
  if (status === 429) {
    applyQuota(data?.data)
    ElMessage.error(data?.message || '今日下载局数已达上限')
    return
  }
  ElMessage.error(data?.message || fallback || '下载失败')
}

const downloadGameIds = async (gameIds) => {
  const ids = [...new Set((gameIds || []).map(String).filter(Boolean))]
  if (!ids.length) return 0
  const remaining = quota.value.unlimited ? ids.length : Math.min(ids.length, quota.value.remaining)
  if (!quota.value.unlimited && remaining <= 0) {
    ElMessage.error('今日下载局数已达上限（凌晨 4 点刷新）')
    return 0
  }
  const toFetch = ids.slice(0, remaining)
  let saved = 0
  downloading.value = true
  try {
    for (let i = 0; i < toFetch.length; i += 100) {
      const chunk = toFetch.slice(i, i + 100)
      const resp = await playerApi.post('/records/fetch-json', {
        target_user_id: targetUserId.value,
        game_ids: chunk,
      }, { timeout: 120000 })
      if (!resp.data.success) {
        ElMessage.error(resp.data.message || '拉取牌谱失败')
        break
      }
      const createdAtById = new Map(
        recordItems.value.map((row) => [String(row.game_id), row.created_at])
      )
      const items = (resp.data.data?.items || []).map((item) => ({
        ...item,
        created_at: item.created_at || createdAtById.get(String(item.game_id)) || null,
      }))
      applyQuota(resp.data.data)
      await putLocalRecords(items)
      saved += items.length
      await refreshLocalIds()
      await refreshCachedPlayers()
      if (items.length < chunk.length && !quota.value.unlimited) break
    }
    if (saved) ElMessage.success(`已下载 ${saved} 局到本机`)
  } catch (e) {
    if (e?.response) handleDownloadError(e, '拉取牌谱失败')
    else ElMessage.error('本机保存牌谱失败')
  } finally {
    downloading.value = false
  }
  return saved
}

const onSelectBar = async (bar) => {
  if (!bar?.missingIds?.length) return
  await downloadGameIds(bar.missingIds)
}

const downloadAllFiltered = async () => {
  const missing = recordItems.value
    .map((row) => String(row.game_id))
    .filter((id) => !localIds.value.has(id))
  if (!missing.length) {
    ElMessage.info('当前筛选的牌谱都已下载')
    return
  }
  await downloadGameIds(missing)
}

const triggerBlob = (blob, filename) => {
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = filename
  document.body.appendChild(a)
  a.click()
  document.body.removeChild(a)
  URL.revokeObjectURL(url)
}

const zipDownloadedFiltered = async () => {
  const ids = recordItems.value
    .map((row) => String(row.game_id))
    .filter((id) => localIds.value.has(id))
  if (!ids.length) {
    ElMessage.info('当前筛选没有已下载的牌谱')
    return
  }
  zipping.value = true
  try {
    const items = await getLocalRecords(ids)
    const files = items.map((item) => ({
      name: `${item.game_id}.json`,
      data: JSON.stringify(item.record, null, 0),
    }))
    const blob = zipStoreFiles(files)
    const who = (targetName.value || 'player').replace(/[\\/:*?"<>|]/g, '_')
    triggerBlob(blob, `${who}_records_${ids.length}.zip`)
    ElMessage.success(`已打包 ${files.length} 局`)
  } catch (_) {
    ElMessage.error('打包 ZIP 失败')
  } finally {
    zipping.value = false
  }
}

const formatWinTime = (value) => {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '—'
  const pad = (n) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`
}

const formatRecordDate = (dateString) => {
  if (!dateString) return ''
  const date = new Date(dateString)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleString('zh-CN', {
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const gameTypeLabel = (matchType) => {
  if (!matchType) return '-'
  const base = String(matchType).replace(/_rank$/, '')
  return MODE_LABELS[base] || matchType
}

const sceneLabel = (row) => {
  if (row.room_type === 'custom') return '自定义'
  if (row.room_type === 'events') {
    if (row.event_name) return `比赛场 · ${row.event_name}`
    if (row.event_id) return `比赛场 · ${row.event_id}`
    return '比赛场'
  }
  if (row.room_type === 'match') {
    if (row.match_tier) return TIER_LABELS[row.match_tier] || row.match_tier
    return '天梯'
  }
  return row.room_type || row.rule || '-'
}

const myPlayer = (rec) => {
  const uid = targetUserId.value
  return rec.players?.find((p) => Number(p.user_id) === Number(uid))
}
const myRank = (rec) => myPlayer(rec)?.rank
const myScore = (rec) => myPlayer(rec)?.score
const scoreClass = (s) => (s > 0 ? 'pos' : s < 0 ? 'neg' : '')
const formatScore = (s) => (s === undefined || s === null ? '-' : (s > 0 ? '+' : '') + s)
const playersSummary = (rec) => (rec.players || []).map((p) => p.username || '?').join(' / ')

const loadResultRecords = async () => {
  const uid = targetUserId.value
  if (uid == null) return
  const seq = ++recordsSeq
  recordsLoading.value = true
  try {
    const resp = await axios.get(`/api/player/records/${uid}`, {
      params: {
        limit: recordsPage.size,
        offset: (recordsPage.current - 1) * recordsPage.size,
        ...filterPayload(),
      },
    })
    if (seq !== recordsSeq) return
    if (resp.data.success) {
      gameRecords.value = resp.data.data?.items || []
      recordsTotal.value = resp.data.data?.total || 0
    } else {
      gameRecords.value = []
      recordsTotal.value = 0
    }
    recordsLoadedKey.value = filterKey.value
  } catch (_) {
    if (seq !== recordsSeq) return
    gameRecords.value = []
    recordsTotal.value = 0
  } finally {
    if (seq === recordsSeq) recordsLoading.value = false
  }
}

const onRecordsSizeChange = () => {
  recordsPage.current = 1
  loadResultRecords()
}

const downloadRecordJson = async (gameId) => {
  const id = String(gameId || '')
  if (!id) return
  try {
    const items = await getLocalRecords([id])
    if (items.length) {
      triggerBlob(
        new Blob([JSON.stringify(items[0].record, null, 0)], { type: 'application/json' }),
        `${id}.json`,
      )
      return
    }
    ElMessage.info('该局尚未下载到本机，请先在上方色块下载')
  } catch (_) {
    ElMessage.error('导出 JSON 失败')
  }
}

const loadCachedRecords = async () => {
  const uid = targetUserId.value
  const cachedIds = recordItems.value
    .map((row) => String(row.game_id))
    .filter((id) => localIds.value.has(id))
  if (!cachedIds.length) {
    ElMessage.info('请先下载牌谱再分析')
    return null
  }
  const createdAtById = new Map(
    recordItems.value.map((row) => [String(row.game_id), row.created_at])
  )
  const items = await getLocalRecords(cachedIds)
  for (const item of items) {
    if (!item.created_at && createdAtById.has(String(item.game_id))) {
      item.created_at = createdAtById.get(String(item.game_id))
    }
  }
  return { uid, items }
}

const clearAnalysisResults = () => {
  analyzedStats.value = null
  advancedStats.value = null
  advancedHasTenpai.value = false
  winEvents.value = []
  winEventsKey.value = ''
  fanHits.value = []
  fanQueried.value = false
  fanPage.value = 1
  resultTab.value = ''
  analyzedFilterKey.value = ''
  advancedFilterKey.value = ''
  gameRecords.value = []
  recordsTotal.value = 0
  recordsLoadedKey.value = ''
  recordsPage.current = 1
}

const runStandardAnalysis = async () => {
  const loaded = await loadCachedRecords()
  if (!loaded) return
  analyzingKind.value = 'standard'
  try {
    const stats = analyzeRecords(loaded.items, loaded.uid)
    analyzedStats.value = stats
    analyzedFilterKey.value = filterKey.value
    resultTab.value = 'standard'
    ElMessage.success(`已分析 ${stats.total_games || 0} 局`)
  } catch (e) {
    ElMessage.error('本地分析失败')
  } finally {
    analyzingKind.value = ''
  }
}

const runAdvancedAnalysis = async () => {
  if (!isGuobiao.value) {
    ElMessage.info('高级分析目前仅支持国标')
    return
  }
  const loaded = await loadCachedRecords()
  if (!loaded) return
  analyzingKind.value = 'advanced'
  analyzingProgress.value = `0 / ${loaded.items.length} 局`
  try {
    const stats = await analyzeRecordsAdvanced(loaded.items, loaded.uid, {
      tingpai: true,
      onProgress: (done, total) => {
        analyzingProgress.value = `${done} / ${total} 局`
      },
    })
    advancedStats.value = stats
    advancedHasTenpai.value = true
    advancedFilterKey.value = filterKey.value
    winEvents.value = stats.wins || []
    winEventsKey.value = filterKey.value
    resultTab.value = 'advanced'
    ElMessage.success(`已分析 ${stats.total_games || 0} 局 · ${stats.win_count || 0} 次和牌`)
  } catch (e) {
    ElMessage.error('高级分析失败')
  } finally {
    analyzingKind.value = ''
    analyzingProgress.value = ''
  }
}

const ensureWinEvents = async () => {
  if (winEventsKey.value === filterKey.value) {
    return winEvents.value
  }
  const loaded = await loadCachedRecords()
  if (!loaded) return null
  analyzingProgress.value = `0 / ${loaded.items.length} 局`
  const stats = await analyzeRecordsAdvanced(loaded.items, loaded.uid, {
    tingpai: false,
    onProgress: (done, total) => {
      analyzingProgress.value = `${done} / ${total} 局`
    },
  })
  winEvents.value = stats.wins || []
  winEventsKey.value = filterKey.value
  return winEvents.value
}

const runFanSearch = async () => {
  if (!isGuobiao.value) {
    ElMessage.info('按番查谱目前仅支持国标')
    return
  }
  if (!selectedFanKey.value) {
    ElMessage.info('请选择番种')
    return
  }
  analyzingKind.value = 'fan'
  try {
    const wins = await ensureWinEvents()
    if (!wins) return
    fanHits.value = sortWinsByTimeDesc(filterWinsByFan(wins, selectedFanKey.value))
    fanQueried.value = true
    fanPage.value = 1
    resultTab.value = 'fan'
    ElMessage.success(`找到 ${fanHits.value.length} 次和牌`)
  } catch (e) {
    ElMessage.error('查谱失败')
  } finally {
    analyzingKind.value = ''
    analyzingProgress.value = ''
  }
}

const openFanFromAdvanced = (row) => {
  if (row.isCoufan) selectedFanKey.value = COUFAN_KEY
  else if (row.isMenduanping) selectedFanKey.value = MENDUANPING_KEY
  else {
    const opt = FAN_SEARCH_OPTIONS.find((item) => item.name === row.label)
    selectedFanKey.value = opt?.key || COUFAN_KEY
  }
  fanHits.value = sortWinsByTimeDesc(filterWinsByFan(winEvents.value, selectedFanKey.value))
  fanQueried.value = true
  fanPage.value = 1
  resultTab.value = 'fan'
}

const shortId = (id) => {
  const s = String(id || '')
  return s.length > 12 ? `${s.slice(0, 8)}…` : s
}

const copyWinLink = async (win) => {
  const path = sharePathForWin(win)
  if (!path) return
  const url = `${window.location.origin}${path}`
  try {
    await navigator.clipboard.writeText(url)
    ElMessage.success(`已复制：第${win.round}局 巡${win.xunmu} node ${win.node}`)
  } catch (_) {
    ElMessage.error('复制失败')
  }
}

const applyQueryFilters = () => {
  const rule = queryStr('rule')
  if (rule && RULE_DEFS.some((d) => d.key === rule)) currentRule.value = rule
  const sc = queryStr('scene')
  if (sc && SCENE_OPTIONS.some((s) => s.value === sc)) scene.value = sc
  const len = queryStr('length')
  if (len && LENGTH_OPTIONS.some((l) => l.value === len)) length.value = len
  const eventId = queryStr('event_id')
  if (eventId) selectedEventId.value = eventId
  const from = queryStr('date_from')
  const to = queryStr('date_to')
  if (from && to) dateRange.value = [from, to]
}

const switchRule = (rule) => {
  currentRule.value = rule
  onFilterChange()
}
const selectScene = (s) => {
  scene.value = s
  if (s === 'events') loadEventOptions()
  else selectedEventId.value = null
  onFilterChange()
}
const selectLength = (l) => {
  length.value = l
  onFilterChange()
}
const onFilterChange = () => {
  clearAnalysisResults()
  loadScopeCounts()
  loadRecordIds()
}

watch(resultTab, (tab) => {
  if (tab !== 'standard' && tab !== 'advanced') return
  if (recordsLoadedKey.value === filterKey.value) return
  recordsPage.current = 1
  loadResultRecords()
})

watch(filterKey, () => {
  if (analyzedFilterKey.value && analyzedFilterKey.value !== filterKey.value) {
    analyzedStats.value = null
  }
  if (advancedFilterKey.value && advancedFilterKey.value !== filterKey.value) {
    advancedStats.value = null
    advancedHasTenpai.value = false
  }
  if (winEventsKey.value && winEventsKey.value !== filterKey.value) {
    winEvents.value = []
    fanHits.value = []
    fanQueried.value = false
  }
})

watch(targetUserId, async (uid, prev) => {
  if (uid == null || uid === prev) return
  if (!authReady.value || !auth.isLoggedIn) return
  clearAnalysisResults()
  await loadPlayerInfo()
  if (scene.value === 'events') loadEventOptions()
  await Promise.all([loadScopeCounts(), loadRecordIds()])
})

const maybeAutoDownload = async () => {
  if (autoDownloadStarted) return
  if (queryStr('autodownload') !== '1') return
  autoDownloadStarted = true
  const nextQuery = { ...route.query }
  delete nextQuery.autodownload
  router.replace({ path: route.path, query: nextQuery })
  const missing = recordItems.value
    .map((row) => String(row.game_id))
    .filter((id) => !localIds.value.has(id))
  if (missing.length) await downloadGameIds(missing)
  if (localCount.value > 0) await runStandardAnalysis()
}

onMounted(async () => {
  if (!auth.loaded) await auth.fetchMe()
  authReady.value = true
  if (!auth.isLoggedIn) return
  applyQueryFilters()
  if (scene.value === 'events') loadEventOptions()
  await Promise.all([loadQuota(), loadPlayerInfo(), refreshCachedPlayers()])
  await Promise.all([loadScopeCounts(), loadRecordIds()])
  await maybeAutoDownload()
  await refreshCachedPlayers()
})
</script>

<style scoped>
.record-analysis {
  color: #1f2329;
  text-align: left;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}
.ra-panel {
  background: #fff;
  border: 1px solid #dcdfe6;
  padding: 14px 16px;
  min-width: 0;
}
.ra-status {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 10px;
  color: #64748b;
  font-size: 13px;
}
.ra-status p {
  margin: 0;
  line-height: 1.65;
  max-width: 36em;
  min-width: 0;
  width: 100%;
  overflow-wrap: break-word;
}
.account-meta {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}
.player-search-bar {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}
.search-label { font-size: 13px; color: #64748b; }
.player-search-input { width: 220px; max-width: 100%; }
.cached-players { margin-bottom: 10px; }
.cached-label {
  display: block;
  font-size: 11px;
  color: #94a3b8;
  margin-bottom: 6px;
}
.cached-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  max-height: 76px;
  overflow-y: auto;
}
.cached-empty { font-size: 12px; color: #94a3b8; }
.target-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 8px;
  font-size: 13px;
}
.other-note {
  font-size: 11px;
  color: #64748b;
  background: #f5f7fa;
  border: 1px solid #eef0f3;
  padding: 2px 6px;
}
.account-who, .quota-line {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  font-size: 13px;
}
.u-name { font-weight: 700; color: #1e293b; font-size: 15px; }
.u-sep { color: #cbd5e1; }
.u-id, .u-games { font-family: Consolas, Menlo, monospace; color: #64748b; }
.quota-line { color: #64748b; font-size: 12px; }
.quota-meter {
  width: 120px;
  height: 6px;
  background: #eef0f3;
  overflow: hidden;
  flex-shrink: 0;
}
.quota-fill {
  height: 100%;
  background: #409eff;
  min-width: 0;
}
.quota-help-button { width: 28px; height: 28px; color: #8c8c8c; font-size: 18px; }
.quota-help-button:hover,
.quota-help-button:focus-visible { color: #1677ff; background: #e6f4ff; }
.other-banner {
  font-size: 12px;
  color: #475569;
  background: #f5f7fa;
  border: 1px solid #eef0f3;
  padding: 6px 8px;
  margin-bottom: 10px;
}
.filter-row {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-bottom: 8px;
}
.filter-row.with-date { justify-content: space-between; }
.tier-group { display: flex; align-items: center; gap: 6px; flex-wrap: wrap; }
.event-select { width: 200px; }
.filter-date { width: 240px !important; }
.chip {
  appearance: none;
  border: 1px solid #dcdfe6;
  background: #fff;
  color: #475569;
  font-size: 13px;
  padding: 4px 10px;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 5px;
  line-height: 1.4;
}
.chip:hover { border-color: #409eff; color: #409eff; }
.chip.selected {
  background: #409eff;
  border-color: #409eff;
  color: #fff;
  font-weight: 700;
}
.chip.selected .chip-count { color: #fff; }
.chip-count {
  font-size: 11px;
  color: #94a3b8;
  font-family: Consolas, Menlo, monospace;
}
.account-strip-label {
  font-size: 11px;
  color: #94a3b8;
  margin-bottom: 6px;
}
.bar-box {
  border: 1px solid #eef0f3;
  background: #fafbfc;
  padding: 10px 12px;
  min-height: 120px;
  max-height: 280px;
  overflow-y: auto;
}
.bar-box-head {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 8px;
  font-size: 12px;
  color: #475569;
  margin-bottom: 8px;
}
.bar-box-head-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}
.bar-box-actions { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.bar-legend { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; color: #94a3b8; }
.lg { display: inline-flex; align-items: flex-end; gap: 3px; }
.lg-bar { display: inline-block; }
.lg-bar.h { width: 8px; height: 16px; background: #409eff; }
.lg-bar.t { width: 6px; height: 12px; background: #67c23a; }
.lg-bar.o { width: 4px; height: 8px; background: #e6a23c; }
.lg-note { font-size: 11px; }
.bar-empty { font-size: 12px; color: #94a3b8; padding: 16px 0; }
.section-title { font-size: 14px; font-weight: 700; color: #1e293b; margin-bottom: 10px; }
.tool-row { display: flex; gap: 12px; flex-wrap: wrap; }
.tool-card {
  flex: 1 1 240px;
  max-width: none;
  border: 1px solid #eef0f3;
  padding: 12px 14px;
}
.tool-card.active {
  border-color: #409eff;
  box-shadow: 0 0 0 1px #409eff inset;
}
.tool-name { font-size: 14px; font-weight: 700; color: #1e293b; margin-bottom: 6px; }
.tool-desc { font-size: 12px; color: #64748b; line-height: 1.6; margin: 0 0 10px; }
.tool-actions { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.tool-hint { font-size: 12px; color: #94a3b8; }
.fan-actions { width: 100%; }
.fan-select { width: 220px; max-width: 100%; }
.result-tabs { display: flex; gap: 6px; flex-wrap: wrap; margin-bottom: 12px; }
.result-note { margin: 0 0 10px; font-size: 12px; color: #64748b; line-height: 1.6; }
.stats-table.nested { margin-bottom: 0; grid-template-columns: repeat(2, minmax(0, 1fr)); }
.adv-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  margin-bottom: 12px;
}
.fan-box .bar-list { max-height: 320px; overflow-y: auto; }
.bar-list { display: flex; flex-direction: column; gap: 6px; }
.bar-row {
  display: grid;
  grid-template-columns: 7.5em 1fr 7em;
  gap: 8px;
  align-items: center;
  border: 0;
  background: transparent;
  padding: 0;
  text-align: left;
  color: inherit;
  font: inherit;
}
.bar-row.clickable { cursor: pointer; }
.bar-row.clickable:hover .bar-lab { color: #409eff; }
.bar-row.coufan .bar-track i { background: #e6a23c; }
.bar-row.menduanping .bar-track i { background: #9b59b6; }
.bar-lab { font-size: 12px; color: #475569; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.bar-track {
  height: 8px;
  background: #eef0f3;
  overflow: hidden;
}
.bar-track i {
  display: block;
  height: 100%;
  background: #409eff;
  min-width: 0;
}
.bar-num {
  font-size: 11px;
  color: #64748b;
  font-family: Consolas, Menlo, monospace;
  text-align: right;
}
.tile-freq { display: flex; flex-direction: column; gap: 8px; }
.tile-freq-row { display: flex; gap: 4px; flex-wrap: wrap; }
.tile-freq-cell {
  display: flex;
  flex-direction: column;
  align-items: center;
  min-width: 28px;
  padding: 2px 1px;
}
.tile-freq-cell.zero { opacity: 0.28; }
.tile-n { font-size: 11px; font-weight: 700; font-family: Consolas, Menlo, monospace; color: #1e293b; }
.tile-p { font-size: 10px; color: #94a3b8; font-family: Consolas, Menlo, monospace; }
.fan-table-wrap { overflow-x: auto; }
.fan-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}
.fan-table th, .fan-table td {
  border-bottom: 1px solid #eef0f3;
  padding: 7px 8px;
  text-align: left;
  vertical-align: middle;
}
.fan-table th { color: #64748b; font-weight: 600; }
.fan-table .mono { white-space: nowrap; }
.mono { font-family: Consolas, Menlo, monospace; }
.yaku-cell { max-width: 280px; line-height: 1.45; }
.fan-ops { display: flex; gap: 8px; white-space: nowrap; }
.fan-ops a, .linkish {
  color: #409eff;
  background: none;
  border: 0;
  padding: 0;
  cursor: pointer;
  font: inherit;
}
.fan-pager { margin-top: 10px; justify-content: flex-end; }
.stats-table {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1px;
  background: #dcdfe6;
  border: 1px solid #dcdfe6;
  overflow: hidden;
  margin-bottom: 12px;
}
.stats-row {
  display: flex;
  flex-direction: column;
  background: #fff;
  padding: 6px 9px;
}
.stats-label { font-size: 11px; color: #64748b; line-height: 1.2; }
.stats-value {
  font-size: 0.95rem;
  font-weight: 700;
  color: #1e293b;
  font-family: Consolas, Menlo, monospace;
}
.charts-panel { display: flex; gap: 12px; }
.chart-box {
  border: 1px solid #eef0f3;
  padding: 8px 10px;
}
.chart-pie { width: 280px; max-width: 100%; }
.chart-title { font-size: 12px; font-weight: 600; color: #475569; margin-bottom: 4px; }
.pie-wrap { display: flex; align-items: center; gap: 10px; min-height: 74px; }
.pie-svg { width: 80px; height: 80px; flex-shrink: 0; }
.pie-center { font-size: 15px; font-weight: 700; fill: #1e293b; }
.pie-legend { display: flex; flex-direction: column; gap: 3px; flex: 1; }
.legend-item { display: flex; align-items: center; gap: 5px; font-size: 11px; color: #475569; }
.legend-dot { width: 8px; height: 8px; flex-shrink: 0; }
.legend-label { flex: 1; white-space: nowrap; }
.legend-pct { font-weight: 700; font-family: Consolas, Menlo, monospace; }
.fan-collapse { margin: 6px 0 10px; }
:deep(.fan-collapse .el-collapse-item__header) { font-size: 13px; font-weight: 600; }
.fan-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 4px;
}
.fan-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 4px 8px;
  background: #f5f7fa;
  border: 1px solid #eef0f3;
  font-size: 12px;
}
.fan-name { color: #475569; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.fan-pts { color: #94a3b8; font-weight: 400; }
.fan-value { font-weight: 700; color: #409eff; font-family: 'Consolas', 'Menlo', monospace; }
.records-section { margin-top: 16px; border-top: 1px solid #eef0f3; padding-top: 12px; }
.records-head {
  display: flex;
  align-items: baseline;
  justify-content: space-between;
  margin-bottom: 8px;
}
.records-total { font-size: 12px; color: #94a3b8; }
.records-table { width: 100%; }
:deep(.records-table .el-table__cell) { padding: 4px 0; }
:deep(.records-table th.el-table__cell) {
  background: #f5f7fa;
  color: #475569;
  font-weight: 600;
  border-bottom: 1px solid #dcdfe6;
}
:deep(.records-table .el-table__border-left-patch),
:deep(.records-table td.el-table__cell) { border-color: #eef0f3; }
.cell-game-id {
  font-family: Consolas, Menlo, monospace;
  font-size: 12px;
  color: #64748b;
  word-break: break-all;
}
.cell-time { font-size: 12px; color: #64748b; font-family: Consolas, Menlo, monospace; }
.cell-scene { font-size: 12px; color: #303133; }
.cell-mode { font-size: 12px; color: #64748b; font-family: Consolas, Menlo, monospace; }
.cell-score { font-weight: 700; font-family: Consolas, Menlo, monospace; }
.cell-score.pos { color: #c0392b; }
.cell-score.neg { color: #2c7a2c; }
.cell-players { font-size: 12px; color: #64748b; cursor: help; }
.tip-player { font-size: 12px; line-height: 1.7; }
.tip-player .pos { color: #ff7a7a; font-weight: 700; }
.tip-player .neg { color: #6ee06e; font-weight: 700; }
.rank-badge {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 18px;
  height: 18px;
  padding: 0 4px;
  font-weight: 700;
  font-size: 11px;
  font-family: Consolas, Menlo, monospace;
}
.rank-1 { background: #6fd86f; color: #1f5e1f; }
.rank-2 { background: #5dadff; color: #fff; }
.rank-3 { background: #aab4c2; color: #2c3848; }
.rank-4 { background: #ff7a7a; color: #fff; }
.records-foot {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 10px;
}
@media (max-width: 720px) {
  .filter-row.with-date { flex-direction: column; align-items: flex-start; }
  .filter-date { width: 100% !important; }
  .stats-table { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .chart-pie { width: auto; flex: 1 1 100%; }
  .tool-card { max-width: none; }
  .adv-grid { grid-template-columns: 1fr; }
  .stats-table.nested { grid-template-columns: 1fr; }
  .bar-row { grid-template-columns: 5.5em 1fr 6.5em; }
  .account-meta { flex-direction: column; }
  .player-search-input { width: 100%; }
}
</style>
