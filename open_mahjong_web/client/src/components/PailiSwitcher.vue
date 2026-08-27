<template>
  <div class="paili-switcher" role="tablist" aria-label="牌理切换">
    <button
      v-for="item in items"
      :key="item.to"
      type="button"
      role="tab"
      class="paili-switch-btn"
      :class="{ on: isActive(item.to) }"
      :aria-selected="isActive(item.to)"
      @click="go(item.to)"
    >{{ item.label }}</button>
  </div>
</template>

<script setup>
import { useRoute, useRouter } from 'vue-router'

const route = useRoute()
const router = useRouter()

const items = [
  { to: '/paili', label: '国标牌理' },
  { to: '/paili/hongque', label: '虹雀² 牌理' },
]

const isActive = (to) => route.path === to

const go = (to) => {
  if (route.path !== to) router.replace(to)
}
</script>

<style scoped>
.paili-switcher {
  display: inline-flex;
  gap: 4px;
  background: rgba(255, 255, 255, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.5);
  border-radius: 8px;
  padding: 4px 4px;
  margin: 0 0 18px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
}

.paili-switch-btn {
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

.paili-switch-btn:hover {
  background: rgba(255, 255, 255, 0.18);
}

.paili-switch-btn.on {
  background: #ffffff;
  color: #1e3a8a;
}
</style>
