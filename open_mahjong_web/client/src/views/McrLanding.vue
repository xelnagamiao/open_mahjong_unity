<template><div /></template>

<script setup>
import { onMounted } from 'vue'
import { useRoute } from 'vue-router'

// These URLs are complete static documents. Internal SPA navigation must use
// the same source-derived document, rather than a second copy of its prose.
const route = useRoute()
onMounted(() => window.location.replace(route.fullPath))
</script>