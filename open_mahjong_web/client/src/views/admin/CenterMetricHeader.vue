<template>
  <el-popover
    v-model:visible="open"
    :persistent="false"
    trigger="click"
    width="240"
    @before-enter="prepare"
  >
    <template #reference>
      <button
        class="metric-header"
        :class="{ active }"
        :aria-label="`${context}筛选排序`"
      >
        <span
          >{{ label }}<small v-if="subtitle">{{ subtitle }}</small></span
        >
        <span class="header-icon" aria-hidden="true">{{
          active ? (query.order === "asc" ? "▴" : "▾") : "⌄"
        }}</span>
      </button>
    </template>
    <div class="header-filter">
      <div class="sort-actions">
        <el-button size="small" @click="apply('asc')">升序</el-button>
        <el-button size="small" @click="apply('desc')">降序</el-button>
      </div>
      <div class="range-inputs">
        <el-input-number
          v-model="draft.min"
          :min="0"
          :max="100"
          :controls="false"
          placeholder="最低 %"
          aria-label="最低百分比"
          size="small"
        />
        <span>至</span>
        <el-input-number
          v-model="draft.max"
          :min="0"
          :max="100"
          :controls="false"
          placeholder="最高 %"
          aria-label="最高百分比"
          size="small"
        />
      </div>
      <el-checkbox v-model="draft.both" size="small"
        >喂牌与收牌两侧</el-checkbox
      >
      <div v-if="field === 'occurrence_probability'" class="anomaly-shortcut">
        <span>异常用户 · 概率上限</span
        ><el-input-number
          v-model="draft.threshold"
          :min="0"
          :max="100"
          :controls="false"
          aria-label="异常概率上限百分比"
          size="small"
        /><span>%</span>
        <el-button type="primary" size="small" @click="applyAnomaly"
          >筛选异常用户</el-button
        >
      </div>
      <div class="sort-actions">
        <el-button size="small" @click="clear">清除</el-button>
        <el-button size="small" type="primary" @click="apply()">筛选</el-button>
      </div>
    </div>
  </el-popover>
</template>
<script setup>
import { computed, reactive, ref } from "vue";
const props = defineProps({
  label: String,
  subtitle: String,
  context: String,
  channel: String,
  direction: String,
  field: String,
  query: Object,
});
const emit = defineEmits(["apply"]);
const open = ref(false);
const draft = reactive({
  min: null,
  max: null,
  both: false,
  anomaly: "all",
  threshold: 5,
});
const active = computed(
  () =>
    props.query?.channel === props.channel &&
    props.query?.sort_by === props.field &&
    (props.query?.direction === props.direction ||
      props.query?.direction === "any"),
);
function prepare() {
  const sameFilter = active.value && props.query?.filter_by === props.field;
  draft.min =
    sameFilter && props.query.min_value != null
      ? props.query.min_value * 100
      : null;
  draft.max =
    sameFilter && props.query.max_value != null
      ? props.query.max_value * 100
      : null;
  draft.both = active.value && props.query.direction === "any";
  draft.anomaly = "all";
  draft.threshold =
    sameFilter && props.query.max_value != null
      ? props.query.max_value * 100
      : 5;
}
function apply(order) {
  emit("apply", {
    channel: props.channel,
    direction: draft.both ? "any" : props.direction,
    sort_by: props.field,
    filter_by: props.field,
    order:
      order ||
      (active.value
        ? props.query.order
        : props.field === "occurrence_probability"
          ? "asc"
          : "desc"),
    min_percent: draft.min,
    max_percent: draft.max,
    anomaly: draft.anomaly,
  });
  open.value = false;
}
function applyAnomaly() {
  draft.min = null;
  draft.max = draft.threshold ?? 5;
  draft.anomaly = "all";
  apply("asc");
}
function clear() {
  draft.min = null;
  draft.max = null;
  draft.anomaly = "all";
  apply();
}
</script>
<style scoped>
.metric-header {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  width: 100%;
  padding: 0;
  color: inherit;
  background: none;
  border: 0;
  font: inherit;
  font-weight: 400;
  text-align: left;
  cursor: pointer;
}
.metric-header.active {
  color: #3c67c6;
}
.header-icon {
  font-size: 10px;
}
small {
  display: block;
  font-size: 11px;
}
.header-filter {
  display: grid;
  gap: 8px;
  font-weight: 400;
  font-synthesis: none;
}
.header-filter :deep(*) {
  font-weight: 400 !important;
  font-synthesis: none;
}
.sort-actions,
.range-inputs {
  display: flex;
  align-items: center;
  gap: 6px;
}
.sort-actions :deep(.el-button) {
  flex: 1;
  margin: 0;
}
.range-inputs :deep(.el-input-number) {
  width: 92px;
}
:global(.center-column-filter-options *) {
  font-weight: 400 !important;
  font-synthesis: none;
}
.anomaly-shortcut {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}
.anomaly-shortcut :deep(.el-input-number) {
  width: 90px;
}
</style>
