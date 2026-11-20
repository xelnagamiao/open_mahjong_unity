<template>
  <el-drawer
    :model-value="modelValue"
    size="620px"
    title="指标说明"
    class="guide-drawer"
    @update:model-value="$emit('update:modelValue', $event)"
    ><article class="guide">
      <dl>
        <dt>递送口径</dt>
        <dd>
          全口径包含吃、碰、弃牌明杠和普通点和。和牌口径只计普通点和。每种口径用自己的次数和分母计算出现概率。
        </dd>
        <dt>喂牌率</dt>
        <dd>
          只看双方同桌的牌局：我给该玩家的事件数 ÷ 我给三个对手的总事件数。
        </dd>
        <dt>收牌率</dt>
        <dd>
          同一批共同牌局中：我从该玩家获得的事件数 ÷
          我从三个对手获得的总事件数。
        </dd>
        <dt>同桌率</dt>
        <dd>共同场数 ÷ 当前玩家在同一规则、时间窗口内的总场数，独立展示。</dd>
        <dt>出现概率</dt>
        <dd>
          假设三名对手机会均分（每人1/3），且事件独立，出现当前次数或更多次数的精确二项上尾概率。概率不高于5%标红；这不是作弊概率，未作多重比较校正。
        </dd>
        <dt>展示与筛选</dt>
        <dd>
          全口径与和牌同时展示，两种口径可能对应不同伙伴。所选口径、方向、范围和异常条件作用于全部关系；同一关系、同一方向满足条件才入选。范围包含上下界，留空则不限。点击相应出现概率表头可一键筛选异常用户，也可自定概率上限。
        </dd>
        <dt>排序与代表</dt>
        <dd>
          每种口径、每个方向按所选指标选出代表：出现概率取最低，其余取最高。升降序只改变榜序，不改变代表；缺失值排最后。展开伙伴沿用筛选与排序，图表口径仅改变图表。
        </dd>
        <dt>罗伯特复核</dt>
        <dd>
          按弃牌者当时可见的信息，比较喂给目标时和其余出牌中明显差于罗伯特的比例。差值按喂牌减常规计算，单侧 Fisher 检验衡量喂牌组偏离次数达到观察值或更多的概率。
        </dd>
      </dl>
      <div class="example">
        假设我喂出的4次牌都给了同一人，其中只有1次点和。全口径为4/4，出现概率1.2346%。和牌为1/1，出现概率33.33%。
      </div>
    </article></el-drawer
  >
</template>
<script setup>
defineProps({ modelValue: Boolean, model: Object });
defineEmits(["update:modelValue"]);
</script>
<style scoped>
:global(.guide-drawer) {
  max-width: 94vw;
}
.guide {
  font-size: 13px;
  line-height: 1.7;
  font-weight: 400;
  color: #42516a;
  font-synthesis: none;
}
.guide * {
  font-weight: 400;
}
dl {
  display: grid;
  grid-template-columns: 95px 1fr;
  margin: 0;
}
dt,
dd {
  margin: 0;
  padding: 10px 0;
  border-bottom: 1px solid #edf0f4;
}
dt {
  color: #243953;
  padding-right: 10px;
}
.example {
  padding: 12px;
  background: #f5f7fb;
  margin-top: 12px;
  border-radius: 5px;
}
</style>
