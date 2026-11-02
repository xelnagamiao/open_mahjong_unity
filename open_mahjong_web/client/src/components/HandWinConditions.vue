<template>
  <div class="win-conditions">
    <div v-if="rule === 'guobiao'" class="condition-row">
      <span>花牌（每张 1 番）</span>
      <el-input-number v-model="form.flowerCount" :min="0" :max="8" size="small" controls-position="right" aria-label="花牌数量" />
    </div>
    <div class="condition-row">
      <span>和牌方式</span>
      <el-radio-group v-model="form.hepaiType" size="small" aria-label="和牌方式">
        <el-radio-button label="dianhe">{{ rule === 'riichi' ? '荣和' : '点和' }}</el-radio-button>
        <el-radio-button label="zimo">自摸</el-radio-button>
      </el-radio-group>
      <el-select v-model="form.changFeng" size="small" :style="{ width: locale === 'fr' ? '205px' : '110px' }" aria-label="场风">
        <el-option v-for="opt in ['场风东','场风南','场风西','场风北']" :key="opt" :label="tr(opt)" :value="opt" />
      </el-select>
      <el-select v-model="form.menFeng" size="small" :style="{ width: locale === 'fr' ? '205px' : '110px' }" aria-label="自风">
        <el-option v-for="opt in ['自风东','自风南','自风西','自风北']" :key="opt" :label="tr(opt)" :value="opt" />
      </el-select>
    </div>
    <div class="condition-row">
      <el-checkbox v-if="rule === 'guobiao'" v-model="form.flagSet.heJueZhang" size="small">和绝张</el-checkbox>
      <el-checkbox v-model="form.flagSet.gangShangKaiHua" size="small">{{ rule === 'guobiao' ? '杠上开花' : '岭上开花' }}</el-checkbox>
      <el-checkbox v-model="form.flagSet.qiangGangHe" size="small">{{ rule === 'guobiao' ? '抢杠和' : '抢杠' }}</el-checkbox>
      <el-checkbox v-model="form.flagSet.miaoShouHuiChun" size="small">{{ rule === 'guobiao' ? '妙手回春' : '海底摸月' }}</el-checkbox>
      <el-checkbox v-model="form.flagSet.haiDiLaoYue" size="small">{{ rule === 'guobiao' ? '海底捞月' : '河底捞鱼' }}</el-checkbox>
    </div>
    <template v-if="rule === 'riichi'">
      <div class="condition-row">
        <span>立直</span>
        <el-radio-group v-model="form.riichi" size="small" :disabled="!closed" aria-label="立直状态">
          <el-radio-button :label="0">无</el-radio-button>
          <el-radio-button :label="1">立直</el-radio-button>
          <el-radio-button :label="2">双立直</el-radio-button>
        </el-radio-group>
        <el-checkbox v-model="form.ippatsu" :disabled="!closed || !form.riichi" size="small">一发</el-checkbox>
      </div>
      <div class="condition-row">
        <label v-for="item in bonuses" :key="item.key" class="bonus-input">{{ item.label }}
          <el-input-number v-model="form[item.key]" :aria-label="item.label + '数量'" :min="0" :max="item.max" :disabled="item.key === 'uraDora' && (!closed || !form.riichi)" size="small" controls-position="right" />
        </label>
      </div>
      <p class="hint">宝牌输入实际张数。四人日麻：食断、累计役满、复合役满开启；双倍役满、切上满贯关闭。不含本场与供托。</p>
    </template>
  </div>
</template>
<script setup>
import { locale, tr } from '@/i18n'
defineProps({ form: { type: Object, required: true }, rule: { type: String, required: true }, closed: { type: Boolean, default: true } })
const bonuses = [{ key: 'dora', label: '宝牌', max: 40 }, { key: 'redDora', label: '赤宝牌', max: 4 }, { key: 'uraDora', label: '里宝牌', max: 40 }]
</script>
<style scoped>
.win-conditions { padding: 8px 12px; border-top: 1px dashed var(--omu-border, #ebeef5); color: var(--omu-text-soft, #475569); font-size: 12.5px; }
.condition-row { display: flex; flex-wrap: wrap; gap: 8px 14px; align-items: center; margin-bottom: 8px; }
.condition-row:last-child { margin-bottom: 0; }
.bonus-input { display: inline-flex; gap: 6px; align-items: center; }
.bonus-input .el-input-number { width: 90px; }
.hint { margin: 6px 0 0; font-size: 12px; line-height: 1.6; }
</style>
