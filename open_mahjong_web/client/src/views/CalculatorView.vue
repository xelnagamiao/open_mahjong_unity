<!--
  计算器统一路由 /calc/:kind（chinese | hongque）。
  合并国标/虹雀两个计算器的路由，切换时用 replace 不产生返回历史，
  避免玩家点返回在两个计算器之间来回切换。
-->
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
