<template>
  <section class="panel">
    <div class="panel-hd">
      <strong>{{ label }}</strong>
      <span class="dim">{{ used }}/{{ maxGuesses }}</span>
      <span v-if="correct" class="ok">已猜中</span>
      <span v-else-if="done" class="dim">次数用尽</span>
    </div>

    <table class="gf-table">
      <colgroup>
        <col class="c-name" />
        <col class="c-rules" />
        <col class="c-types" />
        <col class="c-len" />
        <col class="c-fan" />
      </colgroup>
      <thead>
        <tr>
          <th>名字</th>
          <th>规则</th>
          <th>类型</th>
          <th>组数</th>
          <th>番数</th>
        </tr>
      </thead>
      <tbody>
        <tr v-if="!rows.length">
          <td colspan="5" class="empty">{{ emptyText }}</td>
        </tr>

        <template v-else-if="mode === 'full'">
          <tr v-for="(row, i) in rows" :key="'f' + i">
            <td>
              <span class="cell" :class="row.result.name.tone">{{ row.result.name.value }}</span>
            </td>
            <td>
              <span class="cell" :class="row.result.rules.tone">{{ row.result.rules.value }}</span>
            </td>
            <td>
              <span class="cell" :class="row.result.types.tone">{{ row.result.types.value }}</span>
            </td>
            <td>
              <span class="cell" :class="row.result.reqLength.tone">
                {{ row.result.reqLength.value }}
                <i v-if="row.result.reqLength.hint === 'up'" class="arrow">↑</i>
                <i v-else-if="row.result.reqLength.hint === 'down'" class="arrow">↓</i>
              </span>
            </td>
            <td>
              <span class="cell" :class="row.result.fan.tone">
                {{ row.result.fan.value }}
                <i v-if="row.result.fan.hint === 'up'" class="arrow">↑</i>
                <i v-else-if="row.result.fan.hint === 'down'" class="arrow">↓</i>
              </span>
            </td>
          </tr>
        </template>

        <template v-else>
          <tr v-for="(row, i) in rows" :key="'p' + i">
            <td><span class="block" :class="row.preview.name" /></td>
            <td><span class="block" :class="row.preview.rules" /></td>
            <td><span class="block" :class="row.preview.types" /></td>
            <td><span class="block" :class="row.preview.reqLength" /></td>
            <td><span class="block" :class="row.preview.fan" /></td>
          </tr>
        </template>
      </tbody>
    </table>
  </section>
</template>

<script setup>
defineProps({
  label: { type: String, default: '' },
  used: { type: Number, default: 0 },
  maxGuesses: { type: Number, default: 8 },
  correct: { type: Boolean, default: false },
  done: { type: Boolean, default: false },
  mode: { type: String, default: 'full' }, // full | preview
  rows: { type: Array, default: () => [] },
  emptyText: { type: String, default: '暂无猜测' },
})
</script>

<style scoped>
.panel {
  background: #fff;
  border: 1px solid #e0e0e0;
  padding: 12px;
  min-width: 0;
}

.panel-hd {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 10px;
  font-size: 14px;
}

.dim {
  color: #999;
  font-size: 12px;
}

.ok {
  color: #67c23a;
  font-size: 12px;
  font-weight: 600;
}

.gf-table {
  width: 100%;
  table-layout: fixed;
  border-collapse: separate;
  border-spacing: 6px 6px;
  margin: -6px;
}

.gf-table th {
  font-size: 11px;
  font-weight: 500;
  color: #999;
  text-align: center;
  padding: 0 2px 2px;
}

.gf-table td {
  padding: 0;
  vertical-align: middle;
}

.c-name {
  width: 28%;
}
.c-rules {
  width: 18%;
}
.c-types {
  width: 28%;
}
.c-len {
  width: 13%;
}
.c-fan {
  width: 13%;
}

.cell {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 2px;
  width: 100%;
  min-height: 38px;
  padding: 6px 4px;
  box-sizing: border-box;
  background: #e8e8e8;
  color: #555;
  font-size: 13px;
  font-weight: 600;
  line-height: 1.25;
  text-align: center;
  word-break: break-all;
}

.block {
  display: block;
  width: 100%;
  height: 38px;
  background: #c0c4cc;
}

.arrow {
  font-style: normal;
  font-weight: 800;
  font-size: 13px;
  line-height: 1;
}

.cell.green,
.block.green {
  background: #67c23a;
  color: #fff;
}

.cell.yellow,
.block.yellow {
  background: #e6a23c;
  color: #fff;
}

.cell.gray {
  background: #dcdfe6;
  color: #606266;
}

.block.gray {
  background: #c0c4cc;
}

.empty {
  text-align: center;
  color: #bbb;
  font-size: 13px;
  padding: 24px 8px !important;
}

@media (max-width: 720px) {
  .cell {
    font-size: 11px;
    min-height: 32px;
    padding: 4px 2px;
  }
  .block {
    height: 32px;
  }
  .gf-table {
    border-spacing: 4px 4px;
    margin: -4px;
  }
}
</style>
