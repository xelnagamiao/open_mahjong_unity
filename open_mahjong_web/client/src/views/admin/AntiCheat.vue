<template>
  <main class="center-page">
    <header>
      <h2>反作弊数据中心</h2>
      <div class="actions">
        <el-button
          circle
          aria-label="查看算法说明"
          @click="algorithmOpen = true"
          >?</el-button
        ><el-button @click="openManagement('quality')">数据与记录</el-button
        ><el-button :loading="refreshing" @click="refreshAll">刷新</el-button
        ><el-button
          type="primary"
          :loading="updating"
          :disabled="!!activeUpdate"
          @click="updateData"
          >更新数据</el-button
        >
      </div>
    </header>
    <el-alert
      v-if="visibleError"
      :title="visibleError"
      type="error"
      :closable="false"
    />
    <div v-if="activeUpdate" class="update">
      <span>{{
        activeUpdate.progress?.message || statusLabel(activeUpdate.status)
      }}</span
      ><span
        >{{ count(activeUpdate.progress?.processed) }} /
        {{ count(activeUpdate.progress?.total) }}</span
      ><el-button size="small" :loading="cancelling" @click="cancelUpdate"
        >停止</el-button
      >
    </div>
    <div class="summary">
      <span>国标段位场 {{ count(summary.source_games) }}</span
      ><span>玩家 {{ count(playerTotal) }}</span
      ><span>统计截至 {{ date(summary.last_updated) }}</span>
    </div>
    <el-table
      :data="playerRows"
      v-loading="playerLoading"
      :row-key="(row) => String(row.user_id)"
      :expand-row-keys="expandedKeys"
      :empty-text="emptyText"
      size="small"
      @expand-change="expandPlayer"
    >
      <el-table-column type="expand"
        ><template #default="{ row }"
          ><CenterPlayerDetail
            v-if="expandedKeys.includes(String(row.user_id))"
            :player="row"
            :window-days="appliedQuery.window_days"
            :channel="appliedQuery.channel"
            :query="appliedQuery"
            :revision="summary.last_updated" /></template
      ></el-table-column>
      <el-table-column label="玩家" min-width="105" fixed>
        <template #header>
          <el-popover
            v-model:visible="playerFilterOpen"
            trigger="click"
            width="250"
          >
            <template #reference
              ><button class="column-header" aria-label="玩家筛选">
                玩家 ⌄
              </button></template
            >
            <div class="player-header-filter">
              <el-input
                v-model="filters.q"
                placeholder="昵称 / ID"
                clearable
                size="small"
                @keyup.enter="applyPlayerFilter"
              />
              <el-select
                v-model="filters.window_days"
                aria-label="统计窗口"
                size="small"
                popper-class="center-column-filter-options"
              >
                <el-option
                  v-for="days in windows"
                  :key="days"
                  :value="days"
                  :label="days ? '近' + days + '天' : '全部时间'"
                />
              </el-select>
              <div>
                <el-button size="small" @click="resetFilters">重置</el-button
                ><el-button
                  size="small"
                  type="primary"
                  @click="applyPlayerFilter"
                  >筛选</el-button
                >
              </div>
            </div>
          </el-popover> </template
        ><template #default="{ row }"
          ><button class="player-link" @click="togglePlayer(row)">
            {{ row.name || row.user_id }}</button
          ><small
            >{{ row.user_id }} · {{ count(row.games) }} 场</small
          ></template
        ></el-table-column
      >
      <el-table-column
        v-for="side in directions"
        :key="side.key"
        :label="side.label"
        align="center"
      >
        <el-table-column
          v-for="column in deliveryColumns"
          :key="column.channel + column.field"
          :label="column.label"
          :min-width="column.width"
        >
          <template #header>
            <CenterMetricHeader
              :label="column.label"
              :subtitle="column.subtitle"
              :context="side.label + '·' + column.context"
              :channel="column.channel"
              :direction="side.key"
              :field="column.field"
              :query="appliedQuery"
              @apply="handleHeaderFilter"
            />
          </template>
          <template #default="{ row }">
            <template v-if="column.field === 'rate'">
              <span>{{
                percent(channelMetric(row, column.channel, side.key)?.rate)
              }}</span>
              <small class="partner-name"
                >{{
                  channelMetric(row, column.channel, side.key)?.partner_name ||
                  channelMetric(row, column.channel, side.key)?.partner_id ||
                  "—"
                }}
                ·
                {{ count(channelMetric(row, column.channel, side.key)?.count) }}
                /
                {{
                  count(channelMetric(row, column.channel, side.key)?.total)
                }}</small
              >
            </template>
            <template v-else-if="column.field === 'coplay_rate'">
              <span>{{
                percent(
                  channelMetric(row, column.channel, side.key)?.coplay_rate,
                )
              }}</span>
              <small
                >{{
                  count(
                    channelMetric(row, column.channel, side.key)?.shared_games,
                  )
                }}
                /
                {{ count(channelMetric(row, column.channel, side.key)?.games) }}
                场</small
              >
            </template>
            <span
              v-else-if="column.field === 'occurrence_probability'"
              :class="{
                abnormal: isUnusualOccurrence(
                  channelMetric(row, column.channel, side.key),
                ),
              }"
              >{{
                occurrenceProbability(
                  channelMetric(row, column.channel, side.key),
                )
              }}</span
            >
          </template>
        </el-table-column>
      </el-table-column>
    </el-table>
    <el-pagination
      v-if="playerTotal > 0"
      class="pagination"
      v-model:current-page="filters.page"
      :page-size="filters.page_size"
      :total="playerTotal"
      layout="total, prev, pager, next"
      @current-change="changePage"
    />
    <CenterAlgorithmGuide v-if="algorithmOpen" v-model="algorithmOpen" />
    <el-drawer
      v-model="managementOpen"
      title="数据与记录"
      size="720px"
      class="management"
      ><el-tabs v-model="managementTab" @tab-change="managementChanged"
        ><el-tab-pane label="数据质量" name="quality"
          ><div class="quality" v-loading="qualityLoading">
            <el-alert
              v-if="qualityError"
              :title="qualityError"
              type="error"
              :closable="false"
            />
            <div v-for="item in qualityFields" :key="item.key">
              <span>{{ item.label }}</span
              ><span>{{ count(quality[item.key]) }}</span>
            </div>
          </div></el-tab-pane
        ><el-tab-pane label="更新记录" name="updates"
          ><el-table :data="updateRows" v-loading="historyLoading" size="small"
            ><el-table-column label="时间" min-width="160"
              ><template #default="{ row }">{{
                date(row.created_at || row.started_at)
              }}</template></el-table-column
            ><el-table-column label="状态"
              ><template #default="{ row }">{{
                statusLabel(row.status)
              }}</template></el-table-column
            ><el-table-column label="新增 / 变化"
              ><template #default="{ row }"
                >{{ count(row.new_games) }} /
                {{ count(row.changed_games) }}</template
              ></el-table-column
            ></el-table
          ></el-tab-pane
        ></el-tabs
      ></el-drawer
    >
  </main>
</template>
<script setup>
import { useAdminListState } from '@/composables/useAdminListState'
import {
  computed,
  defineAsyncComponent,
  onBeforeUnmount,
  onMounted,
  reactive,
  ref,
  shallowRef,
} from "vue";
import { ElMessage } from "element-plus";
import { antiCheatApi } from "@/api/antiCheat";
import {
  formatDate as date,
  rulePoolLabel as ruleLabel,
} from "@/utils/antiCheat";
import CenterPlayerDetail from "./CenterPlayerDetail.vue";
import CenterMetricHeader from "./CenterMetricHeader.vue";
const CenterAlgorithmGuide = defineAsyncComponent(
  () => import("./CenterAlgorithmGuide.vue"),
);
import {
  deliveryColumns,
  channelMetric,
  buildRankingQuery,
  numeric,
  percent,
  occurrenceProbability,
  isUnusualOccurrence,
} from "./centerCharts";
const overview = shallowRef(null),
  error = ref(""),
  refreshing = ref(false),
  updating = ref(false),
  cancelling = ref(false);
const summaryError = ref(""),
  qualityError = ref(""),
  qualityData = shallowRef({}),
  qualityLoading = ref(false);
const visibleError = computed(() => error.value || summaryError.value);
const algorithmOpen = ref(false);
const defaultFilters = () => ({
  window_days: 0,
  q: "",
  channel: "ALL",
  rule_key: "__ALL__",
  direction: "any",
  sort_by: "rate",
  order: "desc",
  filter_by: "",
  min_percent: null,
  max_percent: null,
  anomaly: "all",
  page: 1,
  page_size: 20,
});
const { saved, remember } = useAdminListState();
const filters = reactive({ ...defaultFilters(), ...saved.filters });
const appliedQuery = shallowRef(buildRankingQuery(filters));
const playerFilterOpen = ref(false);
function handleHeaderFilter(options) {
  Object.assign(filters, options);
  return search();
}
function applyPlayerFilter() {
  playerFilterOpen.value = false;
  return search();
}
function resetFilters() {
  Object.assign(filters, defaultFilters());
  playerFilterOpen.value = false;
  return search();
}
const playerRows = shallowRef([]),
  playerTotal = ref(0),
  playerLoading = ref(false),
  expandedKeys = ref([]);
const managementOpen = ref(false),
  managementTab = ref("quality"),
  updateRows = shallowRef([]),
  historyLoading = ref(false);
let disposed = false,
  pollTimer,
  playerLoadingTimer,
  refreshRequest = 0,
  playersRequest = 0;
let playerFlight = null,
  summaryFlight = null,
  qualityFlight = null,
  qualityRevision;
const isCancelled = (e) =>
  e?.code === "ERR_CANCELED" || e?.name === "AbortError";
const summary = computed(() => overview.value?.summary || {}),
  quality = computed(() => qualityData.value),
  activeUpdate = computed(() => overview.value?.active_update || null);
const windows = [0, 7, 30, 90];
const emptyText = computed(() =>
  !summary.value.last_updated
    ? "点击更新数据，建立本地玩家统计"
    : "没有符合条件的玩家",
);
const directions = [
  { key: "feeding", label: "喂牌" },
  { key: "receiving", label: "收牌" },
];
const qualityFields = [
  { key: "cached_games", label: "已缓存牌谱" },
  { key: "reconstructed_hands", label: "重建成功小局" },
  { key: "opportunity_verified_hands", label: "合法机会验证小局" },
  { key: "scored_hands", label: "计分核对小局" },
  { key: "unsupported_games", label: "未支持精细分析" },
  { key: "invalid_records", label: "无法归因记录" },
  { key: "partial_games", label: "部分特征待完善" },
  { key: "failed_games", label: "待重试牌谱" },
];
function count(v) {
  const n = numeric(v);
  return n == null
    ? "—"
    : n.toLocaleString("zh-CN", { maximumFractionDigits: 0 });
}
function statusLabel(v) {
  return (
    {
      queued: "排队中",
      scanning: "核对牌谱",
      analyzing: "计算新增牌谱",
      statistics: "更新统计",
      running: "更新中",
      completed: "已完成",
      failed: "失败",
      cancelled: "已停止",
      interrupted: "已中断",
    }[v] ||
    v ||
    "—"
  );
}
function message(e) {
  return e?.response?.data?.message || e?.message || "操作失败，请稍后重试";
}
function scheduleStatusRead() {
  clearTimeout(pollTimer);
  if (!disposed && activeUpdate.value)
    pollTimer = setTimeout(() => loadSummary(true), 2500);
}
function loadSummary(reconcile = false) {
  if (summaryFlight) return summaryFlight;
  const id = ++refreshRequest;
  const promise = (async () => {
    try {
      const result = await antiCheatApi.overview({ detail: "summary" });
      if (disposed || id !== refreshRequest) return;
      const wasActive = !!activeUpdate.value;
      const previousRevision = summary.value.last_updated;
      overview.value = result;
      summaryError.value = "";
      const changed = previousRevision !== result.summary?.last_updated;
      if (reconcile && (changed || (wasActive && !activeUpdate.value))) {
        await loadPlayers(true);
        if (managementOpen.value && managementTab.value === "quality")
          await loadQuality(true);
      }
      if (
        (wasActive && !activeUpdate.value) ||
        (managementOpen.value && managementTab.value === "updates")
      )
        await loadUpdates();
    } catch (e) {
      if (!disposed && id === refreshRequest && !isCancelled(e))
        summaryError.value = message(e);
    } finally {
      summaryFlight = null;
      if (!disposed && id === refreshRequest) scheduleStatusRead();
    }
  })();
  summaryFlight = promise;
  return promise;
}
async function refreshAll() {
  if (refreshing.value) return;
  refreshing.value = true;
  try {
    await Promise.all([loadSummary(), loadPlayers()]);
    if (managementOpen.value && managementTab.value === "quality")
      await loadQuality(true);
  } finally {
    if (!disposed) refreshing.value = false;
  }
}
function loadPlayers(force = false) {
  const query = buildRankingQuery(filters),
    key = JSON.stringify(query);
  if (force !== true && playerFlight?.key === key) return playerFlight.promise;
  remember({ filters: { ...filters } });
  playerFlight?.controller.abort();
  const controller = new AbortController(),
    id = ++playersRequest;
  clearTimeout(playerLoadingTimer);
  playerLoadingTimer = setTimeout(() => {
    if (!disposed && id === playersRequest) playerLoading.value = true;
  }, 450);
  const promise = (async () => {
    try {
      const result = await antiCheatApi.players(query, {
        signal: controller.signal,
      });
      if (disposed || id !== playersRequest) return;
      error.value = "";
      appliedQuery.value = query;
      playerRows.value = result.items || [];
      playerTotal.value = result.total || 0;
      expandedKeys.value = expandedKeys.value.filter((key) =>
        playerRows.value.some((row) => String(row.user_id) === key),
      );
    } catch (e) {
      if (!disposed && id === playersRequest && !isCancelled(e))
        error.value = message(e);
    } finally {
      if (playerFlight?.id === id) playerFlight = null;
      if (!disposed && id === playersRequest) {
        clearTimeout(playerLoadingTimer);
        playerLoading.value = false;
      }
    }
  })();
  playerFlight = { key, controller, promise, id };
  return promise;
}
function loadQuality(force = false) {
  if (qualityFlight) return qualityFlight;
  if (
    !force &&
    qualityRevision === summary.value.last_updated &&
    qualityRevision !== undefined
  )
    return Promise.resolve();
  qualityLoading.value = true;
  const revision = summary.value.last_updated;
  const promise = (async () => {
    try {
      const result = await antiCheatApi.overview();
      if (disposed) return;
      qualityData.value = result.quality || {};
      qualityRevision = revision;
      qualityError.value = "";
    } catch (e) {
      if (!disposed) qualityError.value = message(e);
    } finally {
      qualityFlight = null;
      if (!disposed) qualityLoading.value = false;
    }
  })();
  qualityFlight = promise;
  return promise;
}
function changePage(page) {
  filters.page = page;
  return loadPlayers();
}
function search() {
  filters.page = 1;
  expandedKeys.value = [];
  return loadPlayers();
}
function expandPlayer(row, expanded) {
  const key = String(row.user_id);
  expandedKeys.value = expanded.some((r) => String(r.user_id) === key)
    ? [key]
    : [];
}
function togglePlayer(row) {
  const key = String(row.user_id);
  expandedKeys.value = expandedKeys.value.includes(key) ? [] : [key];
}
async function updateData() {
  if (updating.value || activeUpdate.value) return;
  updating.value = true;
  error.value = "";
  try {
    await antiCheatApi.update();
    if (!disposed) {
      ElMessage.success("已开始增量更新");
      if (summaryFlight) await summaryFlight;
      if (disposed) return;
      await loadSummary(true);
    }
  } catch (e) {
    if (!disposed) error.value = message(e);
  } finally {
    updating.value = false;
  }
}
async function cancelUpdate() {
  if (!activeUpdate.value) return;
  cancelling.value = true;
  try {
    await antiCheatApi.cancelUpdate(activeUpdate.value.id);
    if (summaryFlight) await summaryFlight;
    if (disposed) return;
    await loadSummary(true);
  } catch (e) {
    error.value = message(e);
  } finally {
    cancelling.value = false;
  }
}
async function loadUpdates() {
  historyLoading.value = true;
  try {
    const r = await antiCheatApi.updates({ page_size: 50 });
    if (!disposed) updateRows.value = r.items || [];
  } catch (e) {
    error.value = message(e);
  } finally {
    historyLoading.value = false;
  }
}
function openManagement(name) {
  managementTab.value = name;
  managementOpen.value = true;
  if (name === "updates") return loadUpdates();
  if (name === "quality") return loadQuality();
}
function managementChanged(name) {
  if (name === "updates") return loadUpdates();
  if (name === "quality") return loadQuality();
}
onMounted(refreshAll);
onBeforeUnmount(() => {
  disposed = true;
  clearTimeout(pollTimer);
  refreshRequest++;
  playersRequest++;
  clearTimeout(playerLoadingTimer);
  playerFlight?.controller.abort();
});
</script>
<style scoped>
.center-page {
  font-size: 13px;
  color: #35445c;
  font-weight: 400;
  font-synthesis: none;
}
.center-page :deep(*),
.management :deep(*) {
  font-weight: 400 !important;
  font-synthesis: none;
}
header,
.actions,
.summary,
.update {
  display: flex;
  align-items: center;
  gap: 10px;
}
header {
  justify-content: space-between;
  margin-bottom: 12px;
}
h2 {
  font-size: 20px;
  margin: 0;
}
.summary {
  color: #8390a3;
  margin: 10px 0;
}
.actions :deep(.el-button + .el-button) {
  margin-left: 0;
}
.update {
  padding: 8px;
  background: #f5f7fb;
}
small {
  display: block;
  font-size: 11px;
  color: #8792a3;
  line-height: 1.5;
}
.player-link {
  border: 0;
  background: none;
  padding: 0;
  color: #3c67c6;
  cursor: pointer;
}
.abnormal {
  color: #d24545;
}
.pagination {
  justify-content: flex-end;
  margin-top: 12px;
}
.quality > div {
  display: flex;
  justify-content: space-between;
  padding: 10px;
  border-bottom: 1px solid #edf0f4;
}
@media (max-width: 850px) {
  header {
    flex-wrap: wrap;
  }
}

:deep(.el-table .cell) {
  padding-left: 6px;
  padding-right: 6px;
}
.partner-name {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.column-header {
  border: 0;
  padding: 0;
  color: inherit;
  background: none;
  font: inherit;
  font-weight: 400;
  cursor: pointer;
}
.player-header-filter {
  display: grid;
  gap: 8px;
  font-weight: 400;
  font-synthesis: none;
}
.player-header-filter :deep(*) {
  font-weight: 400 !important;
}
:deep(.el-loading-mask) {
  transition: none;
}
</style>
