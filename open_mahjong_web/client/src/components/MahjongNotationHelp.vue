<template>
  <el-collapse class="notation-collapse">
    <el-collapse-item title="牌面简写说明（点击展开）" name="1">
      <ul class="notation-list">
        <li><strong>m</strong>：万子，数字 1–9 表示一至九万；<strong>0m</strong> 表示赤五万（5m 的红宝牌写法）。</li>
        <li><strong>p</strong>：饼（筒）子；<strong>s</strong>：条（索）子；写法同万子。</li>
        <li><strong>z</strong>：字牌，数字 1–7 依次为 <strong>东南西北白发中</strong>（与常见网页记法一致）。</li>
        <li>同花色数字可连写，花色字母放在一段数字末尾，例如 <code>123m456p</code> 表示万 123 与饼 456。</li>
        <li>多段按顺序拼接即可，例如 <code>35m146678p24s344z5m</code>。</li>
      </ul>
    </el-collapse-item>
  </el-collapse>
</template>

<script setup>
</script>

<style scoped>
.notation-collapse {
  margin-bottom: 8px;
  border: none;
  --el-collapse-header-height: 32px;
  font-size: 12px;
}
.notation-collapse :deep(.el-collapse-item__header) {
  font-size: 12px;
  padding: 0 8px;
  background: rgba(255, 255, 255, 0.15);
  color: #fff;
  border-radius: 6px;
}
.notation-collapse :deep(.el-collapse-item__wrap) {
  border: none;
}
.notation-collapse :deep(.el-collapse-item__content) {
  padding: 8px 10px;
  background: rgba(255, 255, 255, 0.92);
  color: #303133;
  border-radius: 0 0 6px 6px;
  font-size: 12px;
  line-height: 1.55;
}
.notation-list {
  margin: 0;
  padding-left: 18px;
}
.notation-list code {
  font-family: var(--omu-mono, Consolas, monospace);
  background: #f0f2f5;
  padding: 0 4px;
  border-radius: 3px;
}
</style>
