<!-- 立直麻将牌型解算页面 -->
<template>
  <div class="riichi-mahjong">
    <div class="page-header">
      <h1>立直麻将牌型解算</h1>
      <p>根据您输入的手牌、副露、宝牌、和牌方式计算出可能的和牌构成与他家支付的点数</p>
    </div>

    <el-card class="main-card">
      <el-form :model="form" label-width="120px">
        <el-row :gutter="20">
          <el-col :span="12">
            <el-form-item label="手牌">
              <el-input v-model="form.hand" placeholder="请输入手牌" />
            </el-form-item>
          </el-col>
          <el-col :span="12">
            <el-form-item label="和牌方式">
              <el-select v-model="form.wayToHepai" placeholder="选择和牌方式">
                <el-option label="自摸" value="zimo" />
                <el-option label="荣和" value="ronghe" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="6">
            <el-form-item label="副露1">
              <el-input v-model="form.fulu1" placeholder="副露1" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item label="副露2">
              <el-input v-model="form.fulu2" placeholder="副露2" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item label="副露3">
              <el-input v-model="form.fulu3" placeholder="副露3" />
            </el-form-item>
          </el-col>
          <el-col :span="6">
            <el-form-item label="副露4">
              <el-input v-model="form.fulu4" placeholder="副露4" />
            </el-form-item>
          </el-col>
        </el-row>

        <el-row :gutter="20">
          <el-col :span="8">
            <el-form-item label="宝牌数">
              <el-input-number v-model="form.doraNum" :min="0" :max="10" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="里宝牌数">
              <el-input-number v-model="form.deepDoraNum" :min="0" :max="10" />
            </el-form-item>
          </el-col>
          <el-col :span="8">
            <el-form-item label="位置">
              <el-select v-model="form.positionSelect" placeholder="选择位置">
                <el-option label="东家" value="east" />
                <el-option label="南家" value="south" />
                <el-option label="西家" value="west" />
                <el-option label="北家" value="north" />
              </el-select>
            </el-form-item>
          </el-col>
        </el-row>

        <el-form-item>
          <el-button type="primary" @click="calculate" :loading="loading">
            开始计算
          </el-button>
          <el-button @click="clearForm">
            清空
          </el-button>
        </el-form-item>
      </el-form>

      <div v-if="result" class="result-section">
        <el-divider>计算结果</el-divider>
        <pre>{{ result }}</pre>
      </div>
    </el-card>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue'
import { ElMessage } from 'element-plus'
import axios from 'axios'

const loading = ref(false)
const result = ref('')

const form = reactive({
  hand: '',
  fulu1: '',
  fulu2: '',
  fulu3: '',
  fulu4: '',
  wayToHepai: '',
  doraNum: 0,
  deepDoraNum: 0,
  positionSelect: ''
})

const calculate = async () => {
  if (!form.hand) {
    ElMessage.warning('请输入手牌')
    return
  }

  loading.value = true
  try {
    const response = await axios.post('/api/mahjong/count-riichi', form)
    if (response.data.success) {
      result.value = response.data.data.output
      ElMessage.success('计算完成')
    } else {
      ElMessage.error(response.data.message)
    }
  } catch (error) {
    ElMessage.error('计算失败')
  } finally {
    loading.value = false
  }
}

const clearForm = () => {
  Object.keys(form).forEach(key => {
    if (typeof form[key] === 'number') {
      form[key] = 0
    } else {
      form[key] = ''
    }
  })
  result.value = ''
}
</script>

<style scoped>
.riichi-mahjong {
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.page-header {
  text-align: center;
  margin-bottom: 40px;
  color: white;
}

.page-header h1 {
  font-size: 2.5rem;
  margin-bottom: 10px;
}

.main-card {
  background: rgba(255, 255, 255, 0.95);
}

.result-section {
  margin-top: 30px;
}

.result-section pre {
  background: #f5f7fa;
  padding: 20px;
  border-radius: 8px;
  white-space: pre-wrap;
  font-family: monospace;
}
</style> 