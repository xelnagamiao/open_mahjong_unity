<!--
  计算器切换：国标计算器 / 虹雀² 计算器。
  替代页面顶部“XX计算器”标题行，点击即可在两个计算器间切换。
-->
<template>
  <div class="calc-switcher" role="tablist" aria-label="计算器切换">
    <button
      v-for="calc in calculators"
      :key="calc.to"
      type="button"
      role="tab"
      class="calc-switch-btn"
      :class="{ on: isActive(calc.to) }"
      :aria-selected="isActive(calc.to)"
      @click="go(calc.to)"
    >{{ calc.label }}</button>
  </div>
</template>

<script setup>
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const calculators = [
  { to: '/calc/chinese', label: '国标计算器' },
  { to: '/calc/hongque', label: '虹雀² 计算器' },
]

const isActive = (to) => route.path === to

const go = (to) => {
  if (route.path !== to) router.replace(to)
}
</script>

<style scoped>
.calc-switcher {
  display: inline-flex;
  gap: 4px;
  background: rgba(255, 255, 255, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.5);
  border-radius: 8px;
  padding: 4px 4px;
  margin: 10px 0 18px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
}

.calc-switch-btn {
  border: none;
  background: transparent;
  color: rgba(255, 255, 255, 0.95);
  font-size: 15px;
  font-weight: 700;
  padding: 7px 18px;
  border-radius: 6px;
  cursor: pointer;
  font-family: inherit;
  letter-spacing: 0.5px;
  transition: background 0.15s ease, color 0.15s ease;
}

.calc-switch-btn:hover {
  background: rgba(255, 255, 255, 0.18);
}

.calc-switch-btn.on {
  background: #ffffff;
  color: #1e3a8a;
}
</style>
