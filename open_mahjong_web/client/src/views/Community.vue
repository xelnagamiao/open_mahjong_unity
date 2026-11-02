<template><main class="community-page"><header><h1>社群</h1><el-button @click="refresh">刷新</el-button></header><p v-if="error">{{ error }}</p><CommunityCards v-if="content" :items="content.communities" :appearance="content.appearance"/><p v-else-if="!error">正在加载…</p></main></template>
<script setup>
import CommunityCards from '@/components/CommunityCards.vue'
import { usePlatformContent } from '@/composables/usePlatformContent'
const { content, error, refresh } = usePlatformContent()
</script>
<style scoped>.community-page{max-width:1400px;margin:24px auto;padding:0 24px 40px}.community-page>header{display:flex;align-items:center;justify-content:space-between}.community-page h1{font-size:28px}</style>
