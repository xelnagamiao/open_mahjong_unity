<template>
  <div class="chart-shell">
    <div v-if="empty" class="chart-empty">{{ emptyText }}</div>
    <div
      v-else
      ref="element"
      class="chart"
      role="img"
      :aria-label="label"
      :style="{ height: `${height}px` }"
    />
  </div>
</template>

<script setup>
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import { init, use } from "echarts/core";
import { BarChart } from "echarts/charts";
import { GridComponent, LegendComponent, MarkLineComponent, TooltipComponent } from "echarts/components";
import { CanvasRenderer } from "echarts/renderers";
use([BarChart, GridComponent, LegendComponent, MarkLineComponent, TooltipComponent, CanvasRenderer]);
const props = defineProps({
  option: Object,
  label: String,
  empty: Boolean,
  emptyText: { type: String, default: "暂无可展示的数据" },
  height: { type: Number, default: 220 },
});
const element = ref(null);
let chart,
  observer,
  disposed = false;
async function render() {
  await nextTick();
  if (disposed) return;
  if (props.empty || !element.value) {
    observer?.disconnect();
    chart?.dispose();
    chart = null;
    return;
  }
  if (!chart) {
    chart = init(element.value, null, { renderer: "canvas" });
    observer?.disconnect();
    observer = new ResizeObserver(([entry]) => {
      if (!chart) return;
      const { width, height } = entry.contentRect;
      if (Math.round(width) !== chart.getWidth() || Math.round(height) !== chart.getHeight()) {
        chart.resize();
      }
    });
    observer.observe(element.value);
  }
  chart.setOption(
    props.option || {},
    { notMerge: true },
  );
}
watch(() => [props.option, props.empty, props.height], render);
onMounted(render);
onBeforeUnmount(() => {
  disposed = true;
  observer?.disconnect();
  chart?.dispose();
});
</script>

<style scoped>
.chart-shell {
  min-width: 0;
}
.chart {
  width: 100%;
}
.chart-empty {
  min-height: 150px;
  display: flex;
  justify-content: center;
  align-items: center;
  color: #929aa8;
  font-size: 13px;
}
</style>
