<template>
  <span
    class="tile-input-placeholder"
    :class="tileFaceStyle"
    role="img"
    :aria-label="label"
    :title="label"
  />
</template>

<script setup>
import { useTileFaceStyle } from '@/composables/useTileFaceStyle'

defineProps({ label: { type: String, required: true } })
const { tileFaceStyle } = useTileFaceStyle()
</script>

<style scoped>
.tile-input-placeholder {
  display: inline-block;
  flex: 0 0 auto;
  box-sizing: border-box;
  width: 26px;
  height: 36px;
  border: 2px solid #409eff;
  border-radius: 2px;
  background: transparent;
  vertical-align: top;
}

.tile-input-placeholder.oblique {
  width: 34px;
  height: 42px;
}
</style>
