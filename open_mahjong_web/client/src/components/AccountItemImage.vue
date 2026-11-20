<template>
  <div class="account-item-image" :class="{ framed: item?.slot === 'avatar_frame' }" :style="frameStyle">
    <img v-if="source && !failed" :src="source" :alt="item?.name || ''" loading="lazy" @error="failed = true" />
    <span v-else-if="item?.slot === 'avatar_frame'" class="frame-sample" aria-hidden="true"></span>
    <span v-else aria-hidden="true">{{ item?.effect === 'rename_credit' ? '名' : item?.category === 'character' ? '人' : '物' }}</span>
  </div>
</template>
<script setup>
import { computed, ref, watch } from 'vue'
import { mailIcon } from '@/utils/mailbox'
const props = defineProps({ item: Object })
const failed = ref(false), source = computed(() => mailIcon(props.item))
watch(source, () => { failed.value = false })
const frameStyle = computed(() => /^#[\da-f]{6}$/i.test(props.item?.frame_color || '') ? { '--frame-color': props.item.frame_color } : {})
</script>
<style scoped>
.account-item-image { width:64px; height:64px; box-sizing:border-box; display:grid; place-items:center; border-radius:12px; background:#eef3f1; color:#53796e; flex-shrink:0; overflow:hidden; }
.account-item-image img { width:100%; height:100%; object-fit:contain; }.account-item-image span { font-size:25px; font-weight:400; }
.account-item-image.framed { border:3px solid var(--frame-color,#a7b8b0); padding:4px; }
.frame-sample { position:relative; width:80%; height:80%; overflow:hidden; border-radius:50%; background:#dde6df; }.frame-sample::before { content:''; position:absolute; width:35%; height:35%; left:32.5%; top:17%; background:#9faea3; border-radius:50%; }.frame-sample::after { content:''; position:absolute; width:65%; height:56%; left:17.5%; top:54%; background:#9faea3; border-radius:50% 50% 0 0; }
</style>
