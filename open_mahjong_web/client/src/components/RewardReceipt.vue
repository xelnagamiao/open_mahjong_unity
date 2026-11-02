<template>
  <section v-if="receipt" class="reward-receipt" aria-live="polite">
    <h3>获得物品</h3>
    <div class="reward-list">
      <article v-for="item in receipt.items" :key="item.item_id" class="reward-item">
        <span class="reward-name">{{ item.name }}</span><span class="reward-count">× {{ item.quantity }}</span>
      </article>
    </div>
    <el-button class="confirm" type="primary" @click="$emit('confirm')">确定</el-button>
  </section>
</template>
<script setup>
defineProps({ receipt: Object })
defineEmits(['confirm'])
</script>
<style scoped>
.reward-receipt { margin-top:24px; padding:24px; background:#f2f6fa; border:1px solid #cad9e4; border-radius:6px; }
h3 { margin:0 0 20px; font-size:22px; font-weight:400; color:#263e53; }
.reward-list { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:12px; }
.reward-item { display:grid; grid-template-columns:1fr auto; gap:12px; background:white; padding:16px; border-bottom:3px solid #4c839f; }
.reward-name { color:#243849; }.reward-count { color:#28749a; font-size:20px; }
.confirm { display:block;margin:20px auto 0;min-width:140px; }
</style>
