<template>
  <ChineseMahjong v-if="kind === 'chinese'" />
  <HongqueCalc v-else />
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import HongqueCalc from '@/views/HongqueCalc.vue'

const route = useRoute()

const kind = computed(() => (route.path === '/calc/hongque' ? 'hongque' : 'chinese'))
</script>
