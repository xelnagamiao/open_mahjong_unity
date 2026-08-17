<template>
  <Paili v-if="kind === 'chinese'" />
  <HongquePaili v-else />
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import Paili from '@/views/Paili.vue'
import HongquePaili from '@/views/HongquePaili.vue'

const route = useRoute()

const kind = computed(() => (route.path === '/paili/hongque' ? 'hongque' : 'chinese'))
</script>
