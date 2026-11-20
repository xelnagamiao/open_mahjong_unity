<template>
  <section class="player-detail" v-loading="loading">
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div class="detail-heading">
      <span>{{ player.name || player.user_id }} · 同桌玩家</span>
    </div>
    <el-radio-group v-model="chartChannel" size="small" aria-label="图表口径"
      ><el-radio-button label="ALL" value="ALL">全口径</el-radio-button
      ><el-radio-button label="RON" value="RON"
        >和牌</el-radio-button
      ></el-radio-group
    >
    <CenterChart
      :option="chartOption"
      :height="Math.max(200, Math.min(420, partners.length * 34 + 65))"
      :empty="!partners.length"
      label="各伙伴喂牌与收牌递送率，33.3%均衡基线"
    />
    <el-table :data="partners" size="small" empty-text="没有同桌记录"
      ><el-table-column label="伙伴" min-width="110">
        <template #header>
          <el-popover trigger="click" width="220">
            <template #reference
              ><button class="column-header" aria-label="伙伴规则筛选">
                伙伴 ⌄
              </button></template
            >
            <el-select
              v-model="ruleKey"
              clearable
              placeholder="全部规则"
              aria-label="规则筛选"
              popper-class="center-column-filter-options"
              @change="changeRule"
              ><el-option value="" label="全部规则" /><el-option
                v-for="key in ruleKeys"
                :key="key"
                :value="key"
                :label="ruleLabel(key)"
            /></el-select>
          </el-popover> </template
        ><template #default="{ row }"
          >{{ row.name || row.user_id
          }}<small
            >{{ row.user_id }} · {{ ruleLabel(row.rule_key) }}</small
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
              :query="effectiveQuery"
              @apply="handleHeaderFilter"
            />
          </template>
          <template #default="{ row }">
            <template v-if="column.field === 'rate'">
              <span>{{
                percent(channelMetric(row, column.channel, side.key)?.rate)
              }}</span>
              <small
                >{{
                  count(channelMetric(row, column.channel, side.key)?.count)
                }}
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
                {{
                  count(channelMetric(row, column.channel, side.key)?.games)
                }}
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
        <el-table-column label="机器人参考" width="108"
          ><template #default="{ row }"
            ><el-button
              link
              type="primary"
              :aria-label="`${side.label}·${row.name || row.user_id}·罗伯特复核`"
              @click="openRobert(row, side.key)"
              >罗伯特复核</el-button
            ></template
          ></el-table-column
        >
      </el-table-column>
    </el-table>
    <el-pagination
      v-model:current-page="page"
      :page-size="pageSize"
      :total="total"
      layout="total, prev, pager, next"
      @current-change="load()"
    />
    <CenterRobertReview
      v-if="robertSelection"
      v-model="robertOpen"
      :selection="robertSelection"
    />
  </section>
</template>
<script setup>
import {
  computed,
  defineAsyncComponent,
  ref,
  shallowRef,
  watch,
  onBeforeUnmount,
} from "vue";
import { antiCheatApi } from "@/api/antiCheat";
import { rulePoolLabel } from "@/utils/antiCheat";
import {
  deliveryColumns,
  channelMetric,
  buildRankingQuery,
  numeric,
  percent,
  occurrenceProbability,
  isUnusualOccurrence,
  directionalChart,
} from "./centerCharts";
import CenterMetricHeader from "./CenterMetricHeader.vue";
const CenterChart = defineAsyncComponent(() => import("./CenterChart.vue"));
const CenterRobertReview = defineAsyncComponent(
  () => import("./CenterRobertReview.vue"),
);
const robertOpen = ref(false),
  robertSelection = shallowRef(null);
function openRobert(partner, direction) {
  robertSelection.value = {
    player: props.player,
    partner,
    direction,
    window_days: props.windowDays,
    rule_key: ruleKey.value || "__ALL__",
    channel: effectiveQuery.value.channel === "RON" ? "RON" : "ALL",
  };
  robertOpen.value = true;
}
const props = defineProps({
  player: Object,
  windowDays: Number,
  channel: String,
  revision: [String, Number],
  query: Object,
});
const loading = ref(false),
  error = ref(""),
  record = shallowRef(null),
  page = ref(1),
  ruleKey = ref("");
const ruleLabel = (key) =>
  key === "__ALL__" ? "全部规则" : rulePoolLabel(key);
const detailQuery = ref(null);
const effectiveQuery = computed(() => detailQuery.value || props.query || {});
const pageSize = 12,
  directions = [
    { key: "feeding", label: "喂牌" },
    { key: "receiving", label: "收牌" },
  ];
let pending;
const partners = computed(() => record.value?.partners?.items || []);
const total = computed(() => record.value?.partners?.total || 0);
const ruleKeys = computed(
  () =>
    record.value?.rule_pools?.map((p) =>
      typeof p === "string" ? p : p.rule_key,
    ) ||
    props.player?.rule_keys ||
    record.value?.player?.rule_keys ||
    [],
);
const chartChannel = ref("ALL");
const chartOption = computed(() =>
  directionalChart(partners.value, chartChannel.value),
);
function count(v) {
  const n = numeric(v);
  return n === null ? "—" : n.toLocaleString("zh-CN");
}
function load() {
  const { q: _playerSearch, ...inheritedQuery } = effectiveQuery.value;
  const params = {
    ...inheritedQuery,
    window_days: props.windowDays,
    channel: effectiveQuery.value.channel || props.channel || "ALL",
    rule_key: ruleKey.value || "__ALL__",
    page: page.value,
    page_size: pageSize,
  };
  const userId = props.player.user_id;
  const key = JSON.stringify([userId, props.revision, params]);
  if (pending?.key === key) return pending.promise;
  pending?.controller.abort();
  const controller = new AbortController();
  loading.value = true;
  error.value = "";
  const promise = antiCheatApi
    .player(userId, params, { signal: controller.signal })
    .then((result) => {
      if (!controller.signal.aborted) record.value = result;
    })
    .catch((e) => {
      if (!controller.signal.aborted)
        error.value = e?.response?.data?.message || e.message;
    })
    .finally(() => {
      if (!controller.signal.aborted) {
        loading.value = false;
        pending = undefined;
      }
    });
  pending = { key, controller, promise };
  return promise;
}
function handleHeaderFilter(options) {
  const { min_value, max_value, ...base } = effectiveQuery.value;
  detailQuery.value = buildRankingQuery({ ...base, ...options });
  page.value = 1;
  return load();
}
function changeRule() {
  page.value = 1;
  return load();
}
watch(
  () => [
    props.player?.user_id,
    props.windowDays,
    props.channel,
    props.revision,
    JSON.stringify(props.query),
  ],
  () => {
    detailQuery.value = null;
    chartChannel.value = props.query?.channel === "RON" ? "RON" : "ALL";
    page.value = 1;
    return load();
  },
  { immediate: true },
);
onBeforeUnmount(() => pending?.controller.abort());
</script>
<style scoped>
.player-detail {
  padding: 10px 16px;
  background: #f8fafc;
  font-size: 12px;
  font-weight: 400;
  font-synthesis: none;
}
.player-detail :deep(*) {
  font-weight: 400 !important;
  font-synthesis: none;
}
.player-detail :deep(.el-loading-mask) {
  transition-duration: 80ms;
}
.detail-heading {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
}
small {
  display: block;
  font-size: 11px;
  color: #8792a3;
}
.abnormal {
  color: #d24545;
}
.el-pagination {
  justify-content: flex-end;
  margin-top: 10px;
}

:deep(.el-table .cell) {
  padding-left: 6px;
  padding-right: 6px;
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
</style>
