<template>
  <el-collapse class="notation-collapse">
    <el-collapse-item title="牌面简写说明（点击展开）" name="1">
      <ul class="notation-list">
        <li><strong>m</strong>：万子，数字 1–9 表示一至九万；<strong>0m</strong> 表示赤五万（5m 的红宝牌写法）。</li>
        <li><strong>p</strong>：饼（筒）子；<strong>s</strong>：条（索）子；写法同万子。</li>
        <li><strong>z</strong>：字牌，数字 1–7 依次为 <strong>东南西北白发中</strong>。</li>
        <li>同花色数字可连写，花色字母放在一段数字末尾，例如 <code>123m456p</code> 表示万 123 与饼 456。</li>
        <li>多段按顺序拼接即可，例如 <code>35m146678p24s344z5m</code>。</li>
        <template v-if="meldCodes">
          <li>副露用括号编码并区分大小写：<code>(s13)</code> 为二三四万明顺，<code>(K23)</code> 为三饼暗刻；编码中的数字为项目牌号。</li>
          <li><code>s / S</code> 表示明顺 / 暗顺（数字取顺子中间牌）；<code>k / K</code> 表示明刻 / 暗刻；<code>g / G</code> 表示明杠 / 暗杠。</li>
          <li>项目牌号：11–19 万、21–29 饼、31–39 条、41–44 东南西北、45 中、46 白、47 发；与 <code>z</code> 简写顺序不同。</li>
          <li>例如 <code>567m45s22z(s13)(K23) + 6s</code>；<code>+</code> 后为单张和牌张。副露与简写双向同步，删除括号编码也会移除对应副露。</li>
        </template>
      </ul>
    </el-collapse-item>
  </el-collapse>
</template>

<script setup>
defineProps({ meldCodes: { type: Boolean, default: false } })
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
