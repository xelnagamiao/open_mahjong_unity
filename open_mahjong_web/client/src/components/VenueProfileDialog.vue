<template>
  <el-dialog :model-value="modelValue" :title="`修改${venueKindLabel(venue?.kind)}资料`"
    width="min(680px, 94vw)" destroy-on-close @update:model-value="$emit('update:modelValue', $event)">
    <VenueProfileEditor v-if="modelValue && venue?.event_id" :key="venue.event_id"
      :event-id="venue.event_id" :kind="venue.kind" :admin="admin" @updated="onUpdated" />
  </el-dialog>
</template>

<script setup>
import VenueProfileEditor from '@/components/VenueProfileEditor.vue'
import { venueKindLabel } from '@/utils/eventMeta'

const props = defineProps({
  modelValue: { type: Boolean, default: false },
  venue: { type: Object, default: null },
  admin: { type: Boolean, default: false },
})
const emit = defineEmits(['update:modelValue', 'updated'])

function onUpdated(data) {
  emit('updated', data)
  if (props.admin) emit('update:modelValue', false)
}
</script>
