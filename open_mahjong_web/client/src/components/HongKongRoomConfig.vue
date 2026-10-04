<template>
  <div class="hk-settings">
    <el-form-item label="香港麻将规则">
      <el-select v-model="form.sub_rule" @change="loadHongKongForm(form)">
        <el-option v-for="profile in hkProfiles" :key="profile.value" v-bind="profile" />
      </el-select>
    </el-form-item>
    <p class="source">{{ source }}</p>
    <div class="common-fields">
      <el-form-item label="圈数"><el-input-number v-model="form.game_round" :min="1" :max="4" :precision="0" /></el-form-item>
      <el-form-item label="局时（秒）"><el-input-number v-model="form.round_timer" :min="0" :max="1000" :precision="0" /></el-form-item>
      <el-form-item label="步时（秒）"><el-input-number v-model="form.step_timer" :min="0" :max="100" :precision="0" /></el-form-item>
      <el-form-item label="听牌提示"><el-switch v-model="form.tips" /></el-form-item>
      <el-form-item label="限制游客"><el-switch v-model="form.tourist_limit" /></el-form-item>
      <el-form-item label="允许观战"><el-switch v-model="form.allow_spectator" /></el-form-item>
    </div>
    <el-form-item v-if="form.sub_rule === 'hongkong/qingzhang' || lianhuise" label="花牌">
      <el-select v-model="form.hk_flowers">
        <el-option label="无花（136张）" :value="false" /><el-option label="有花（144张）" :value="true" />
      </el-select>
    </el-form-item>
    <el-form-item label="只可自摸"><el-switch v-model="form.hk_self_draw_only" /></el-form-item>
    <el-form-item label="多人和同一张牌">
      <el-select v-model="form.hk_win_claim"><el-option v-for="option in hkChoices.win_claim" :key="option.value" v-bind="option" /></el-select>
    </el-form-item>
    <el-form-item label="连庄方式">
      <el-select v-model="form.hk_dealer_mode"><el-option v-for="option in hongKongChoices(form, 'dealer_mode')" :key="option.value" v-bind="option" /></el-select>
    </el-form-item>
    <el-form-item v-if="form.sub_rule !== 'hongkong/new16'" label="包十二张"><el-switch v-model="form.hk_liability_twelve" /></el-form-item>
    <el-form-item v-if="form.sub_rule === 'hongkong/qingzhang' || lianhuise" label="包大三元"><el-switch v-model="form.hk_liability_dragons" /></el-form-item>
    <el-form-item v-if="lianhuise" label="生章明杠包自摸"><el-switch v-model="form.hk_liability_kong" /></el-form-item>
    <el-form-item v-if="isRemix(form)" label="包满贯"><el-switch v-model="form.hk_liability_limit" /></el-form-item>
    <el-form-item v-if="isGametower(form)" label="全冲（放铳者支付全部四份）">
      <el-switch v-model="form.hk_new13_full_shoot" />
    </el-form-item>
    <div class="rulebook-links">
      <a :href="rulebook.url" target="_blank" rel="noopener noreferrer">{{ rulebook.readLabel || `查看${rulebook.label}规则书` }}</a>
    </div>
    <p v-if="rulebook.readHint" class="rulebook-hint">{{ rulebook.readHint }}</p>
  </div>
</template>

<script setup>
import { computed, watch } from 'vue'
import { hongKongRulebook } from '../constants/hongKongRulebooks.js'
import { hkProfiles, hkChoices, hongKongChoices, loadHongKongForm, isLianhuise, isRemix, isGametower, displayHongKongProfile } from '../utils/hongKongRoomConfig.js'
const props = defineProps({ form: { type: Object, required: true } })
const rulebook = computed(() => hongKongRulebook(props.form))
watch(() => props.form.sub_rule, value => {
  if (value === 'hongkong/new13') props.form.sub_rule = displayHongKongProfile(props.form)
  else if (!hkProfiles.some(p => p.value === value)) props.form.sub_rule = 'hongkong/qingzhang'
}, { immediate: true })
const lianhuise = computed(() => isLianhuise(props.form))
const source = computed(() => lianhuise.value ? '恋绘色版本：《香港新章规则书》，默认144张、三番起和、十三番封顶、全铳制；庄和或流局连庄，不设报听。' : ({
  'hongkong/qingzhang': '香港麻雀协会《香港麻雀总例》：三番起糊，十番封顶，每局轮庄。',
  'hongkong/new13_gametower': 'Wiki版本：规则资料见 Mahjong Wiki 的 IGS 分栏。136张，支持七对子与报听。',
  'hongkong/qingzhang_lianhuise': '香港新派清章（恋绘色魔改版）是恋绘色以香港麻将（新派清章）为原型灵感，深度魔改而来的个人规则，在保留了三三制（混一色和对对和三番、清一色七番）骨架及番数与分数非线性对应关系的基础上大量增加国标麻将、立直麻将和中庸麻将的部分番种/役种/和种，以期达到更丰富的造牌体验，并避免了番数增加后分数过于膨胀等问题，总之是融合了香港麻将、立直麻将、国标麻将和中庸麻将的大杂烩。',
  'hongkong/new16': '香港麻雀协会港式十六张新章详述版：144张，叮牌、连庄、拉踢。',
})[props.form.sub_rule])
</script>

<style scoped>
.common-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 0 16px; }
.source { margin: 0 0 16px; color: #606266; line-height: 1.7; }
.hk-settings a { color: #337ecc; }
.rulebook-links { display: flex; flex-wrap: wrap; gap: 8px 16px; }
.rulebook-hint { margin: 8px 0 0; color: #606266; font-size: 13px; line-height: 1.7; }
</style>
