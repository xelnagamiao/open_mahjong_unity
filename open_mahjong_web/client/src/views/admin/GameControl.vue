<template>
  <div>
    <h2 class="page-title">对局管理</h2>
    <el-card>
      <EventGameControl platform-admin show-filters />
    </el-card>
  </div>
</template>

<script setup>
import EventGameControl from '@/components/EventGameControl.vue'
</script>

<style scoped>
.page-title { margin: 0 0 16px; font-weight: 400; }
</style>
