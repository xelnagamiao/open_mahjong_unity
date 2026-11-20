<template>
  <el-drawer
    append-to-body
    :model-value="modelValue"
    size="660px"
    title="罗伯特复核 · 机器人参考比较"
    @update:model-value="$emit('update:modelValue', $event)"
    ><section class="robert-review">
      <div class="relation">
        {{ selection.player.name || selection.player.user_id }} ·
        {{ selection.direction === "feeding" ? "喂给" : "收到" }}
        {{ selection.partner.name || selection.partner.user_id }}
      </div>
      <div class="controls">
        <el-radio-group v-model="channel" :disabled="busy" aria-label="复核口径"
          ><el-radio-button value="ALL" label="ALL">全口径</el-radio-button
          ><el-radio-button value="RON" label="RON"
            >和牌</el-radio-button
          ></el-radio-group
        ><el-button
          type="primary"
          :loading="starting"
          :disabled="busy"
          @click="start"
          >开始复核</el-button
        ><el-button v-if="busy" :loading="cancelling" @click="cancel"
          >取消</el-button
        >
      </div>
      <p class="muted">
        仅支持国标段位场，按弃牌者当时可见信息与机器人参考比较。
      </p>
      <details class="thresholds">
        <summary>明显偏离阈值</summary>
        <div class="threshold-grid">
          <label v-for="field in thresholdFields" :key="field.key"
            ><span>{{ field.label }}（{{ field.unit }}）</span
            ><el-input-number
              v-model="thresholds[field.key]"
              :min="1"
              :max="field.max"
              :precision="field.key === 'shanten' ? 0 : 2"
              :step="1"
              :disabled="busy"
              size="small"
              :aria-label="field.label + '阈值'"
          /></label>
        </div>
        <small>按本次决策的优先层比较，差值达到该层阈值才算明显偏离。</small>
      </details>
      <el-alert v-if="error" :title="error" type="error" :closable="false" />
      <div v-if="job" class="progress">
        {{ statusLabel(job.status)
        }}<span v-if="busy">
          · {{ count(job.progress?.done) }} /
          {{ count(job.progress?.total) }} 场</span
        >
      </div>
      <div v-if="job?.result" class="results">
        <div>
          <span>喂牌偏离率</span
          ><strong>{{ percent(job.result.feeding?.rate) }}</strong
          ><small
            >{{ count(job.result.feeding?.numerator) }} /
            {{ count(job.result.feeding?.denominator) }} 次</small
          >
        </div>
        <div>
          <span>常规出牌偏离率</span
          ><strong>{{ percent(job.result.regular_deviation?.rate) }}</strong
          ><small
            >{{ count(job.result.regular_deviation?.numerator) }} /
            {{ count(job.result.regular_deviation?.denominator) }} 次</small
          >
        </div>
      </div>
      <dl v-if="job?.result" class="comparison">
        <div><dt>偏离率差（喂牌 − 常规）</dt><dd>{{ differenceText(job.result.comparison?.difference_points) }}</dd></div>
        <div><dt>喂牌偏高的出现概率</dt><dd>{{ probabilityText(job.result.comparison?.p_greater) }}</dd></div>
      </dl>
      <p v-if="omitted.length" class="muted">{{ omitted.join("、") }}未计入分母。</p>
      <details class="definitions">
        <summary>指标含义</summary>
        <p>
          喂牌与常规出牌都按“明显差于罗伯特的弃牌次数 ÷ 该组可比较次数”计算。喂牌组只计选定口径。常规组取同一玩家在同批牌局中没有喂给目标伙伴的其余弃牌。
        </p>
        <p>
          概率采用单侧 Fisher 精确检验。假设每次可比弃牌有相同的偏离机会，固定两组次数和总偏离次数，计算喂牌组出现当前偏离次数或更多次的概率。按弃牌逐次比较，同局内的相关性和筛选多个伙伴的影响未校正。
        </p>
      </details>
      <el-empty
        v-if="!job && !error"
        description="选择口径后手动开始复核"
        :image-size="60"
      /></section
  ></el-drawer>
</template>
<script setup>
import { computed, reactive, ref, watch, onBeforeUnmount } from "vue";
import { antiCheatApi } from "@/api/antiCheat";
import { numeric, percent } from "./centerCharts";
const props = defineProps({ modelValue: Boolean, selection: Object });
defineEmits(["update:modelValue"]);
const channel = ref("ALL"),
  job = ref(null),
  error = ref(""),
  starting = ref(false),
  cancelling = ref(false);
const thresholds = reactive({
  shanten: 1,
  legal_ukeire: 4,
  ukeire: 4,
  fan: 8,
  ron_ukeire: 4,
  reshape_progress: 24,
});
const thresholdFields = [
  { key: "shanten", max: 8, label: "向听差", unit: "向听数" },
  { key: "legal_ukeire", max: 136, label: "足番一摸进张差", unit: "张" },
  { key: "ukeire", max: 136, label: "普通进张差", unit: "张" },
  { key: "fan", max: 1000, label: "番数差", unit: "番" },
  { key: "ron_ukeire", max: 136, label: "荣和进张差", unit: "张" },
  { key: "reshape_progress", max: 20000, label: "重塑差", unit: "分" },
];
const omitted = computed(() => {
  const result = job.value?.result || {};
  return [
    [result.unavailable_decisions, "次无法比较的弃牌"],
    [result.unavailable_games, "场无法复核的牌谱"],
    [result.skipped_unreconstructed_hands, "个无法重建的小局"],
  ].filter(([n]) => n > 0).map(([n, label]) => count(n) + label);
});
const busy = computed(
  () => starting.value || ["queued", "running"].includes(job.value?.status),
);
let timer,
  sequence = 0,
  disposed = false;
function count(v) {
  const n = numeric(v);
  return n === null ? "—" : n.toLocaleString("zh-CN");
}
function differenceText(value) {
  if (value == null) return "—";
  const rounded = Number(value.toFixed(2));
  if (rounded === 0 && value !== 0) return value > 0 ? "高不足 0.01 个百分点" : "低不足 0.01 个百分点";
  return (rounded > 0 ? "+" : "") + rounded + " 个百分点";
}
function probabilityText(value) {
  if (value == null) return "—";
  const pct = value * 100;
  if (pct < 1e-8) return "<0.00000001%";
  if (pct < 100 && pct > 99.99) return ">99.99%";
  return pct.toLocaleString("zh-CN", { maximumSignificantDigits: 4, useGrouping: false }) + "%";
}
function statusLabel(s) {
  return (
    {
      queued: "排队中",
      running: "复核中",
      completed: "复核完成",
      failed: "复核失败",
      cancelled: "已取消",
    }[s] || s
  );
}
function message(e) {
  return e?.response?.data?.message || e?.message || "复核请求失败";
}
function schedule(delay = 1500) {
  clearTimeout(timer);
  if (
    !disposed &&
    props.modelValue &&
    ["queued", "running"].includes(job.value?.status)
  )
    timer = setTimeout(poll, delay);
}
function accept(result, delay) {
  job.value = result;
  if (result.status === "failed") error.value = result.error || "复核失败";
  schedule(delay);
}
async function poll() {
  const id = job.value?.id,
    version = sequence;
  if (!id || disposed) return;
  try {
    const result = await antiCheatApi.robertReview(id);
    if (!disposed && version === sequence) {
      error.value = "";
      accept(result);
    }
  } catch (e) {
    if (!disposed && version === sequence) {
      error.value = message(e);
      if ([401, 403, 404, 410].includes(e?.response?.status)) {
        clearTimeout(timer);
        job.value = { ...job.value, status: "failed" };
      } else {
        schedule();
      }
    }
  }
}
async function start() {
  if (busy.value) return;
  clearTimeout(timer);
  const version = ++sequence;
  starting.value = true;
  error.value = "";
  job.value = null;
  const selection = props.selection;
  const feeding = selection.direction === "feeding";
  try {
    const result = await antiCheatApi.startRobertReview({
      donor_id: feeding ? selection.player.user_id : selection.partner.user_id,
      recipient_id: feeding
        ? selection.partner.user_id
        : selection.player.user_id,
      window_days: selection.window_days || 0,
      rule_key: selection.rule_key || "__ALL__",
      channel: channel.value,
      thresholds: { ...thresholds },
    });
    if (!disposed && version === sequence) accept(result, 200);
  } catch (e) {
    if (!disposed && version === sequence) error.value = message(e);
  } finally {
    if (!disposed && version === sequence) starting.value = false;
  }
}
async function cancel() {
  if (!job.value?.id || cancelling.value) return;
  clearTimeout(timer);
  const version = ++sequence;
  cancelling.value = true;
  try {
    const result = await antiCheatApi.cancelRobertReview(job.value.id);
    if (!disposed && version === sequence) accept(result);
  } catch (e) {
    if (!disposed && version === sequence) {
      error.value = message(e);
      schedule();
    }
  } finally {
    if (!disposed) cancelling.value = false;
  }
}
watch(
  () => props.selection,
  (selection) => {
    sequence++;
    clearTimeout(timer);
    job.value = null;
    error.value = "";
    starting.value = false;
    channel.value = selection?.channel === "RON" ? "RON" : "ALL";
  },
  { immediate: true },
);
watch(
  () => [channel.value, ...Object.values(thresholds)],
  () => {
    if (!busy.value) {
      sequence++;
      job.value = null;
      error.value = "";
      clearTimeout(timer);
    }
  },
);
watch(
  () => props.modelValue,
  (open) => {
    if (open) schedule();
    else clearTimeout(timer);
  },
);
onBeforeUnmount(() => {
  disposed = true;
  sequence++;
  clearTimeout(timer);
});
</script>
<style scoped>
.robert-review {
  font-weight: 400;
  font-synthesis: none;
  font-size: 13px;
  color: #42516a;
}
.robert-review :deep(*) {
  font-weight: 400 !important;
}
.relation {
  margin-bottom: 12px;
}
.controls {
  display: flex;
  align-items: center;
  gap: 10px;
}
.muted,
small {
  font-size: 12px;
  color: #8792a3;
}
.results {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin: 12px 0;
}
.results > div {
  padding: 12px;
  background: #f5f7fb;
  border-radius: 5px;
}
.results strong,
.results small {
  display: block;
}
.results strong {
  font-size: 24px;
  margin: 7px 0;
}
.comparison {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
  margin: 0 0 12px;
}
.comparison > div {
  padding: 0 12px;
}
.comparison dd {
  margin: 4px 0 0;
  font-variant-numeric: tabular-nums;
}
.progress {
  margin: 12px 0;
}
.definitions {
  margin-top: 12px;
}
.definitions p {
  margin: 6px 0;
}
.thresholds {
  margin: 12px 0;
}
.threshold-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
  margin: 10px 0;
}
.threshold-grid label {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
</style>
