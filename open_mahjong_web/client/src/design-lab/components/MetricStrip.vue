<script setup>
defineProps({metrics:{type:Array,default:()=>[]}})
</script>
<template><div class="metric-strip"><div class="metric" v-for="(item,i) in metrics" :key="item.label" :title="item.detail"><span class="metric-index">0{{i+1}}</span><span class="metric-label">{{item.label}}</span><div class="metric-number">{{item.value}}<small>{{item.unit}}</small></div><p>{{item.detail}}</p></div></div></template>
