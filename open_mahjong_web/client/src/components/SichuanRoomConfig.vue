<template>
  <div>
    <el-form-item label="四川子规则">
      <el-select v-model="form.sub_rule" aria-label="四川子规则">
        <el-option label="四川麻将（血战到底）" value="sichuan/standard" />
        <el-option label="血流成河（丢三张）" value="sichuan/xueliu" />
        <el-option label="血流成河（换三张）" value="sichuan/xueliu_exchange" />
        <el-option label="血战双打·2V2" value="sichuan/doubles" />
        <el-option label="血流双打·2V2" value="sichuan/xueliu_doubles" />
        <el-option v-if="allowPaired" label="血战双打复式" value="sichuan/doubles_duplicate" />
        <el-option v-if="allowPaired" label="血流双打复式" value="sichuan/xueliu_doubles_duplicate" />
      </el-select>
    </el-form-item>
    <el-form-item label="战术鸣牌">
      <el-switch v-model="form.tactical_call" aria-label="战术鸣牌" />
    </el-form-item>
    <el-form-item v-if="!isSichuanDuplicate(form.sub_rule)" label="每盘副数">
      <el-select v-model="form.game_round" aria-label="每盘副数">
        <el-option v-for="n in 4" :key="n" :label="`${n * 4} 副`" :value="n" />
      </el-select>
    </el-form-item>
    <el-alert v-if="form.sub_rule === 'sichuan/xueliu_doubles'" type="info" :closable="false"
      title="随机单桌2V2，对家两阶段换三张；后手仅看队友换出花色。定缺后可控猪，仅向对手收分；花猪赔付每名对手至少32分，队伍同分加副。" />
    <el-alert v-if="form.sub_rule === 'sichuan/doubles'" type="info" :closable="false"
      title="同桌四人、每队两人。在 Unity 房间内选队，队友开局坐对家；不定缺、无顺和，和牌须缺一门。同队两人先和结束本副，按队伍累计副分判盘胜负。" />
  </div>
</template>
<script setup>
import { watch } from 'vue'
import { isSichuanDuplicate } from '@/utils/sichuanDuplicate'
const props = defineProps({ form: { type: Object, required: true }, allowPaired: Boolean })
watch(() => props.form.sub_rule, value => {
  if (!['sichuan/standard', 'sichuan/xueliu', 'sichuan/xueliu_exchange', 'sichuan/doubles', 'sichuan/xueliu_doubles'].includes(value) && !(props.allowPaired && isSichuanDuplicate(value))) props.form.sub_rule = 'sichuan/standard'
}, { immediate: true })
</script>
