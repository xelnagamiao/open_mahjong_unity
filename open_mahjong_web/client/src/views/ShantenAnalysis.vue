<template>
  <div class="shanten-analysis">
    <div class="page-header">
      <h1>听牌待牌判断</h1>
      <p>分析手牌是否听牌，以及听牌手牌的待牌</p>
    </div>

    <el-row :gutter="30">
      <el-col :lg="12" :md="24">
        <el-card class="input-card">
          <template #header>
            <span>手牌输入</span>
          </template>

          <el-form :model="form" :rules="rules" ref="formRef" label-width="120px">
            <el-form-item label="手牌" prop="hand">
              <el-input
                v-model="form.hand"
                placeholder="请输入13张手牌，例如：123456789m123p"
                :maxlength="50"
                show-word-limit
                clearable
              />
            </el-form-item>

            <el-form-item>
              <el-button type="primary" @click="analyzeHand" :loading="loading">
                开始分析
              </el-button>
              <el-button @click="clearForm">
                清空
              </el-button>
            </el-form-item>
          </el-form>
        </el-card>
      </el-col>

      <el-col :lg="12" :md="24">
        <el-card class="result-card">
          <template #header>
            <span>分析结果</span>
          </template>

          <div v-if="loading" class="loading">
            <el-icon class="is-loading" size="32">
              <Loading />
            </el-icon>
            <p>正在分析中...</p>
          </div>

          <div v-else-if="result" class="result-content">
            <el-alert
              :title="result.success ? '分析成功' : '分析失败'"
              :type="result.success ? 'success' : 'error'"
              :description="result.message"
              show-icon
            />
            
            <div v-if="result.success" class="result-details">
              <h3>分析详情：</h3>
              <pre>{{ result.data.output }}</pre>
            </div>
          </div>

          <div v-else class="empty-result">
            <p>请输入手牌开始分析</p>
          </div>
        </el-card>
      </el-col>
    </el-row>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue'
import { ElMessage } from 'element-plus'
import axios from 'axios'
import { Loading } from '@element-plus/icons-vue'

const formRef = ref()
const loading = ref(false)
const result = ref(null)

const form = reactive({
  hand: ''
})

const rules = {
  hand: [
    { required: true, message: '请输入手牌', trigger: 'blur' }
  ]
}

const analyzeHand = async () => {
  try {
    await formRef.value.validate()
    loading.value = true
    result.value = null

    const response = await axios.post('/api/mahjong/count-hand', {
      hand: form.hand
    })

    result.value = response.data
    if (response.data.success) {
      ElMessage.success('分析完成')
    } else {
      ElMessage.error(response.data.message)
    }
  } catch (error) {
    ElMessage.error('分析失败')
  } finally {
    loading.value = false
  }
}

const clearForm = () => {
  form.hand = ''
  result.value = null
  formRef.value?.resetFields()
}
</script>

<style scoped>
.shanten-analysis {
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

.input-card,
.result-card {
  margin-bottom: 30px;
  background: rgba(255, 255, 255, 0.95);
}

.loading {
  text-align: center;
  padding: 40px;
}

.result-content {
  margin-top: 20px;
}

.result-details {
  margin-top: 20px;
}

.result-details pre {
  background: #f5f7fa;
  padding: 15px;
  border-radius: 8px;
  white-space: pre-wrap;
}

.empty-result {
  text-align: center;
  padding: 60px 20px;
  color: #909399;
}
</style> 