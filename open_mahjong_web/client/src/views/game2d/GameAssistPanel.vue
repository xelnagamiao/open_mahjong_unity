<template>
  <div v-if="detailOnly" class="scene-appearance-panel assist-detail">
    <div class="scene-appearance-panel__header">
      <h3 class="scene-appearance-panel__title">鸣牌 / 和牌过滤</h3>
    </div>
    <button
      v-for="item in detailToggles"
      :key="item.key"
      type="button"
      class="assist-switch assist-switch--row"
      :class="{ 'is-on': item.value }"
      :aria-pressed="item.value"
      @click="item.toggle()"
    >
      <span class="assist-switch__label">{{ item.label }}</span>
      <span class="assist-switch__track" aria-hidden="true">
        <span class="assist-switch__thumb" />
      </span>
    </button>
    <p class="scene-appearance-panel__hint">
      「不吃碰杠」与前三项联动：全开则亮，任一关闭则灭。不点和参与自动过牌筛除；不自摸/不抢杠仅在自动和牌开启时生效。
    </p>
  </div>

  <div v-else class="assist-inline">
    <button
      v-for="item in mainToggles"
      :key="item.key"
      type="button"
      class="assist-switch"
      :class="{ 'is-on': item.value }"
      :aria-pressed="item.value"
      @click="item.toggle()"
    >
      <span class="assist-switch__label">{{ item.label }}</span>
      <span class="assist-switch__track" aria-hidden="true">
        <span class="assist-switch__thumb" />
      </span>
    </button>

    <button
      type="button"
      class="assist-switch assist-switch--expand"
      :class="{ 'is-on': expanded }"
      :aria-expanded="expanded"
      @click="$emit('toggle-expand')"
    >
      <span class="assist-switch__label">展开</span>
    </button>
  </div>
</template>

<script setup>
import { computed } from 'vue'
import { withAutoPass, withMeldPassOption } from '@/game2d/lib/assistSettings'

const props = defineProps({
  settings: { type: Object, required: true },
  expanded: { type: Boolean, default: false },
  detailOnly: { type: Boolean, default: false },
})

const emit = defineEmits(['update', 'toggle-expand'])

const mainToggles = computed(() => [
  {
    key: 'autoFlower',
    label: '自动补花',
    value: Boolean(props.settings.autoFlower),
    toggle: () => emit('update', { autoFlower: !props.settings.autoFlower }),
  },
  {
    key: 'autoDiscard',
    label: '自动摸切',
    value: Boolean(props.settings.autoDiscard),
    toggle: () => emit('update', { autoDiscard: !props.settings.autoDiscard }),
  },
  {
    key: 'autoPass',
    label: '不吃碰杠',
    value: Boolean(props.settings.autoPass),
    toggle: () => emit('update', withAutoPass(props.settings, !props.settings.autoPass)),
  },
  {
    key: 'autoWin',
    label: '自动和牌',
    value: Boolean(props.settings.autoWin),
    toggle: () => emit('update', { autoWin: !props.settings.autoWin }),
  },
])

const detailToggles = computed(() => [
  {
    key: 'passChi',
    label: '不吃',
    value: Boolean(props.settings.passChi),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passChi', !props.settings.passChi)),
  },
  {
    key: 'passPeng',
    label: '不碰',
    value: Boolean(props.settings.passPeng),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passPeng', !props.settings.passPeng)),
  },
  {
    key: 'passMingGang',
    label: '不杠',
    value: Boolean(props.settings.passMingGang),
    toggle: () => emit('update', withMeldPassOption(props.settings, 'passMingGang', !props.settings.passMingGang)),
  },
  {
    key: 'noRon',
    label: '不点和',
    value: Boolean(props.settings.noRon),
    toggle: () => emit('update', { noRon: !props.settings.noRon }),
  },
  {
    key: 'noTsumo',
    label: '不自摸',
    value: Boolean(props.settings.noTsumo),
    toggle: () => emit('update', { noTsumo: !props.settings.noTsumo }),
  },
  {
    key: 'noRobKong',
    label: '不抢杠',
    value: Boolean(props.settings.noRobKong),
    toggle: () => emit('update', { noRobKong: !props.settings.noRobKong }),
  },
])
</script>

<style scoped>
.assist-inline {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  justify-content: flex-end;
}

.assist-detail {
  display: flex;
  flex-direction: column;
  gap: 8px;
}
</style>
