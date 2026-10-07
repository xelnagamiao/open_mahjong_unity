<template>
  <div v-if="page" v-html="content" />
</template>

<script setup>
import { computed } from 'vue'
import { useRoute } from 'vue-router'
import { mcrPageForPath } from '@/seo/mcr-pages.js'
import { renderMcrContent } from '@/seo/mcr-render.js'
import '@/seo/mcr-landing.css'

const route = useRoute()
const page = computed(() => mcrPageForPath(route.path))
const content = computed(() => page.value ? renderMcrContent(page.value) : '')
</script>
