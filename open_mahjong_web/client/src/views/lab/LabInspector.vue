<template>
  <aside class="insp">
    <div v-if="verdict" class="insp__verdict" :class="{ pass: verdict.ok, fail: !verdict.ok }">
      <strong>{{ (verdict.verdict && verdict.verdict.summary) || (verdict.ok ? '通过' : '失败') }}</strong>
      <span>{{ (verdict.verdict && verdict.verdict.detail) || '' }}</span>
      <ul v-if="problems.length">
        <li v-for="(problem, i) in problems" :key="i">
          <button v-if="problem.frame != null" type="button" @click="$emit('jump', problem.frame)">{{ problem.frame }}</button>
          <b>{{ problem.where }}</b>
          {{ problem.message }}
        </li>
      </ul>
    </div>
    <details
      v-for="panel in panels"
      :key="panel.key"
      class="insp__acc"
      :open="openDefault.includes(panel.key)"
    >
      <summary>{{ panel.name }}</summary>
      <pre>{{ pretty(components[panel.key]) }}</pre>
    </details>
  </aside>
</template>

<script>
import { OPEN_DEFAULT, UNITY_PANELS } from './labUnityMap'

export default {
  name: 'LabInspector',
  props: {
    components: { type: Object, default: () => ({}) },
    verdict: { type: Object, default: null },
  },
  data() {
    return { panels: UNITY_PANELS, openDefault: OPEN_DEFAULT }
  },
  computed: {
    problems() {
      return (this.verdict && this.verdict.verdict && this.verdict.verdict.problems) || []
    },
  },
  methods: {
    pretty(value) {
      return JSON.stringify(value ?? {}, null, 2)
    },
  },
}
</script>

<style scoped>
.insp {
  display: flex;
  flex-direction: column;
  min-height: 0;
  overflow: auto;
  padding: 14px 12px;
  background: #181b1d;
  color: #eee;
  font-family: system-ui, "Segoe UI", "Microsoft YaHei", sans-serif;
}
.insp__verdict {
  flex: 0 0 auto;
  margin-bottom: 12px;
  padding: 10px;
  border-radius: 8px;
  font-size: 12px;
}
.insp__verdict.pass { background: #1c3324; }
.insp__verdict.fail { background: #3a1f1f; }
.insp__verdict strong { display: block; font-size: 15px; margin-bottom: 4px; }
.insp__verdict ul { margin: 8px 0 0; padding-left: 16px; }
.insp__verdict li { margin-bottom: 6px; white-space: pre-wrap; }
.insp__verdict button {
  margin-right: 6px;
  border: 0;
  background: transparent;
  color: #9ec0ff;
  cursor: pointer;
}
.insp__acc {
  margin-bottom: 8px;
  border: 1px solid rgba(255, 255, 255, 0.17);
  border-radius: 8px;
  background: #121416;
}
.insp__acc summary {
  cursor: pointer;
  padding: 8px 10px;
  font-size: 13px;
  font-weight: 650;
}
.insp__acc pre {
  margin: 0;
  padding: 0 10px 10px;
  max-height: 280px;
  overflow: auto;
  font-size: 11px;
  color: #c7cdcf;
}
</style>
