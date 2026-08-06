<template>
  <ChineseMahjong v-if="kind === 'chinese'" />
  <HongqueCalc v-else />
</template>

<script setup>
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import ChineseMahjong from '@/views/ChineseMahjong.vue'
import HongqueCalc from '@/views/HongqueCalc.vue'

const route = useRoute()

const kind = computed(() => (route.params.kind === 'hongque' ? 'hongque' : 'chinese'))

watch(
  kind,
  () => {
    document.title = kind.value === 'hongque'
      ? '虹雀² 计算器 - salasasa.cn'
      : '国标计算器 - salasasa.cn'
  },
  { immediate: true }
)
</script>
