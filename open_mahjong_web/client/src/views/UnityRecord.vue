<template>
  <UnityGame v-if="validGameId" />

  <main v-else class="invalid-record-page">
    <section class="invalid-record-card">
      <p class="eyebrow">SALASASA · UNITY 3D</p>
      <h1>牌谱链接无效</h1>
      <p>网址中的牌谱 ID 格式不正确，无法启动 3D 牌谱。</p>
      <router-link to="/">返回首页</router-link>
    </section>
  </main>
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import UnityGame from '@/views/UnityGame.vue'

const route = useRoute()
const gameId = computed(() => String(route.params.gameId || '').trim())
const validGameId = computed(() => /^[0-9A-Za-z]{1,16}$/.test(gameId.value))
</script>

<style scoped>
.invalid-record-page {
  min-height: 100vh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: #101315;
  color: #eef0f1;
}

.invalid-record-card {
  width: min(560px, 100%);
  padding: clamp(26px, 6vw, 48px);
  border: 1px solid rgba(255, 255, 255, 0.14);
  border-radius: 18px;
  background: #1b1f21;
  box-shadow: 0 24px 70px rgba(0, 0, 0, 0.38);
}

.eyebrow {
  margin: 0 0 8px;
  color: #df7d23;
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.14em;
}

h1 {
  margin: 0;
  font-size: clamp(30px, 7vw, 46px);
}

p:not(.eyebrow) {
  margin: 22px 0;
  color: #bbc1c3;
  line-height: 1.7;
}

a {
  color: #e38a3d;
}
</style>
