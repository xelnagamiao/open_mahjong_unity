<template>
  <Game :key="String(route.query.scenario || 'random')" demo :demo-scenario="scenario" />
</template>

<script setup>
import { useRoute } from 'vue-router'
import { computed } from 'vue'
import Game from './Game.vue'
const route = useRoute()
const scenario = computed(() => ['claim', 'discard'].includes(route.query.scenario) ? route.query.scenario : 'random')
</script>
