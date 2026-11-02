import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/dist/locale/zh-cn.mjs'
import 'element-plus/dist/index.css'
import '../../style.css'
import UnifiedCalculator from './UnifiedCalculator.vue'

createApp(UnifiedCalculator).use(ElementPlus, { locale: zhCn }).mount('#app')
