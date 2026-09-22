<script setup lang="ts">
/**
 * 系统配置页「AI 配置」页签的模型列表卡片：
 * - 卡片行：默认单选（切换即时生效）/ 测试 / 编辑 / 删除 / 启用开关——全部即时保存（无批量保存按钮）
 * - 新增/编辑弹窗：CommonDialog（项目弹窗规范）；编辑时 ApiKey 留空 = 保持原 Key 不变；
 *   「保存并测试」保存后立刻发 ping 验证连通
 * - CRUD 权限复用 sys-config:save；ApiKey 永不回传明文（仅 HasKey 标记）
 */
import { onMounted, ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Delete, Edit, Plus, QuestionFilled } from '@element-plus/icons-vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import {
  type AiModelItem,
  listAiModels, saveAiModel, testAiModel, setAiModelDefault, deleteAiModel,
} from '@/common/api/ai'

const models = ref<AiModelItem[]>([])
const loading = ref(false)
const testingId = ref(0)
const helpVisible = ref(false)

/** 表格列配置 */
const columns: DataTableColumn<AiModelItem>[] = [
  { prop: 'name', label: '名称', minWidth: 120, custom: true },
  { prop: 'modelId', label: '模型标识', minWidth: 150 },
  { prop: 'baseUrl', label: '接口地址', minWidth: 200, showOverflowTooltip: true },
  { prop: 'enabled', label: '状态', width: 80, align: 'center', custom: true },
  { prop: 'isDefault', label: '默认', width: 80, align: 'center', custom: true },
  { prop: 'hasKey', label: '密钥', width: 100, align: 'center', custom: true },
  { label: '操作', width: 200, align: 'center', custom: true, fixed: 'right' },
]

// 新增/编辑弹窗
const editDialog = ref(false)
const editSaving = ref(false)
const editTesting = ref(false)
const form = ref({ id: 0, name: '', baseUrl: '', apiKey: '', modelId: '', maxContextChars: 60000, enabled: true })

async function load() {
  loading.value = true
  try {
    models.value = await listAiModels()
  } catch {
    /* httpGet silent：无权限时卡片显示空态 */
  } finally {
    loading.value = false
  }
}

function openCreate() {
  form.value = { id: 0, name: '', baseUrl: '', apiKey: '', modelId: '', maxContextChars: 60000, enabled: true }
  editDialog.value = true
}

function openEdit(m: AiModelItem) {
  // apiKey 留空 = 编辑保存时保持原 Key 不变
  form.value = {
    id: m.id, name: m.name, baseUrl: m.baseUrl, apiKey: '',
    modelId: m.modelId, maxContextChars: m.maxContextChars, enabled: m.enabled,
  }
  editDialog.value = true
}

async function doSave(andTest: boolean) {
  const f = form.value
  if (!f.name.trim()) return ElMessage.warning('请填写显示名称')
  if (!f.baseUrl.trim()) return ElMessage.warning('请填写接口地址')
  if (!f.modelId.trim()) return ElMessage.warning('请填写模型标识')
  editSaving.value = true
  try {
    const saved = await saveAiModel({
      id: f.id,
      name: f.name.trim(),
      baseUrl: f.baseUrl.trim(),
      apiKey: f.apiKey.trim() || undefined,
      modelId: f.modelId.trim(),
      maxContextChars: f.maxContextChars,
      enabled: f.enabled,
    })
    ElMessage.success(f.id > 0 ? '模型已保存' : '模型已添加')
    editDialog.value = false
    await load()
    if (andTest) await doTest(saved.id)
  } catch {
    /* httpPost 已弹错误提示 */
  } finally {
    editSaving.value = false
  }
}

async function doTest(id: number) {
  const m = models.value.find(x => x.id === id)
  if (!m) return
  if (!m.hasKey && !m.baseUrl.includes('localhost') && !m.baseUrl.includes('127.0.0.1')) {
    ElMessage.warning('该模型未配置 API Key（本地 Ollama 等无需 Key 的服务除外），请先编辑填写')
    return
  }
  testingId.value = id
  try {
    const result = await testAiModel({ id: m.id, baseUrl: m.baseUrl, modelId: m.modelId })
    if (result.ok) ElMessage.success(`连接正常 · 响应 ${result.elapsedMs}ms`)
    else ElMessage.error(`测试失败：${result.message}`)
  } catch {
    /* httpPost 已弹错误提示 */
  } finally {
    testingId.value = 0
  }
}

async function doSetDefault(m: AiModelItem) {
  if (m.isDefault || !m.enabled) return
  try {
    await setAiModelDefault(m.id)
    ElMessage.success(`已将「${m.name}」设为默认模型`)
    await load()
  } catch {
    /* httpPost 已弹错误提示 */
  }
}

async function toggleEnabled(m: AiModelItem, enabled: boolean) {
  try {
    await saveAiModel({
      id: m.id, name: m.name, baseUrl: m.baseUrl,
      modelId: m.modelId, maxContextChars: m.maxContextChars, enabled,
    })
    ElMessage.success(enabled ? `已启用「${m.name}」` : `已禁用「${m.name}」`)
    await load()
  } catch {
    /* httpPost 已弹错误提示 */
  }
}

async function doDelete(m: AiModelItem) {
  const tip = m.isDefault
    ? `「${m.name}」是当前默认模型，删除后默认将自动切换到首个启用模型，确认删除？`
    : `确认删除模型「${m.name}」？`
  try {
    await ElMessageBox.confirm(tip, '删除模型', { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' })
  } catch {
    return
  }
  try {
    await deleteAiModel(m.id)
    ElMessage.success('已删除')
    await load()
  } catch {
    /* httpDelete 已弹错误提示 */
  }
}

onMounted(load)
defineExpose({ reload: load })

/** 打开外部链接 */
function openUrl(url: string) {
  window.open(url, '_blank')
}

/** 模型教程文档数据 */
const modelGuides = [
  {
    name: 'DeepSeek',
    registerUrl: 'https://platform.deepseek.com/',
    baseUrl: 'https://api.deepseek.com/v1',
    modelId: 'deepseek-chat',
    description: '性价比高，中文理解能力强，价格便宜',
  },
  {
    name: '通义千问',
    registerUrl: 'https://bailian.console.aliyun.com/',
    baseUrl: 'https://xxx.cn-beijing.maas.aliyuncs.com/compatible-mode/v1（每个人的专属域名不同，在百炼平台 API Key 页面复制）',
    modelId: 'qwen-turbo / qwen-plus',
    description: '阿里云出品，中文能力优秀，每天免费 100 万 token',
  },
  {
    name: 'Kimi（月之暗面）',
    registerUrl: 'https://platform.moonshot.cn/',
    baseUrl: 'https://api.moonshot.cn/v1',
    modelId: 'moonshot-v1-8k / moonshot-v1-32k',
    description: '长上下文能力强，适合处理长文档',
  },
  {
    name: '智谱 GLM',
    registerUrl: 'https://open.bigmodel.cn/',
    baseUrl: 'https://open.bigmodel.cn/api/paas/v4',
    modelId: 'glm-4-flash / glm-4',
    description: '清华系模型，综合能力均衡',
  },
  {
    name: 'Ollama（本地部署）',
    registerUrl: 'https://ollama.com/',
    baseUrl: 'http://localhost:11434/v1',
    modelId: '自定义模型名（如 llama3、qwen2）',
    description: '完全免费，需本地显卡（8GB+ 显存）',
  },
]
</script>

<template>
  <el-card shadow="hover" class="model-card">
    <template #header>
      <div class="card-header">
        <span class="card-title">
          <span class="card-icon">🧠</span>
          模型列表
          <el-tooltip content="查看模型配置教程" placement="top">
            <el-icon class="help-icon" @click="helpVisible = true"><QuestionFilled /></el-icon>
          </el-tooltip>
        </span>
        <span class="default-hint" v-if="models.length > 0">
          当前默认：{{ models.find(m => m.isDefault)?.name || '无' }}
        </span>
        <el-button
          v-if="$has('sys-config:save')"
          type="primary"
          size="small"
          :icon="Plus"
          @click="openCreate"
        >
          新增模型
        </el-button>
      </div>
    </template>

    <CommonDataTable
      :columns="columns"
      :data="models"
      :loading="loading"
      :empty-text="'尚未配置模型'"
      row-key="id"
      size="small"
    >
      <!-- 名称列 -->
      <template #cell-name="{ row }">
        <span class="model-name-cell">
          {{ row.name }}
          <span v-if="row.isDefault" class="default-tag">默认</span>
        </span>
      </template>
    
      <!-- 状态列 -->
      <template #cell-enabled="{ row }">
        <el-switch
          :model-value="row.enabled"
          :disabled="!$has('sys-config:save')"
          inline-prompt
          active-text="启用"
          inactive-text="停用"
          size="small"
          @change="(v: string | number | boolean) => toggleEnabled(row, v === true)"
        />
      </template>
    
      <!-- 默认列 -->
      <template #cell-isDefault="{ row }">
        <el-button
          v-if="!row.isDefault"
          size="small"
          :disabled="!row.enabled || !$has('sys-config:save')"
          @click="doSetDefault(row)"
        >
          设为默认
        </el-button>
        <el-tag v-else type="success" size="small">★ 默认</el-tag>
      </template>
    
      <!-- 密钥列 -->
      <template #cell-hasKey="{ row }">
        <el-tag :type="row.hasKey ? 'success' : 'danger'" size="small" effect="plain">
          {{ row.hasKey ? '✔ 已配置' : '✘ 未配置' }}
        </el-tag>
      </template>
    
      <!-- 操作列 -->
      <template #cell-undefined="{ row }">
        <el-button size="small" :loading="testingId === row.id" :disabled="!$has('sys-config:save')" @click="doTest(row.id)">
          测试
        </el-button>
        <el-button size="small" :icon="Edit" :disabled="!$has('sys-config:save')" @click="openEdit(row)" />
        <el-button size="small" type="danger" plain :icon="Delete" :disabled="!$has('sys-config:save')" @click="doDelete(row)" />
      </template>
    </CommonDataTable>

    <!-- 新增/编辑模型弹窗（CommonDialog 项目弹窗规范） -->
    <CommonDialog v-model="editDialog" :title="form.id > 0 ? '编辑模型' : '新增模型'" width="520px" :close-on-click-modal="false">
      <el-form label-width="92px" label-position="left" class="edit-form">
        <el-form-item label="显示名称" required>
          <el-input v-model="form.name" placeholder="如 DeepSeek-V3 / Kimi / 本地Ollama" />
        </el-form-item>
        <el-form-item label="接口地址" required>
          <el-input v-model="form.baseUrl" placeholder="https://api.deepseek.com/v1（OpenAI 兼容 /base）" />
          <div class="form-tip">自建 vLLM、Ollama(http://localhost:11434/v1) 均可</div>
        </el-form-item>
        <el-form-item label="API Key">
          <el-input v-model="form.apiKey" type="password" show-password autocomplete="new-password"
            :placeholder="form.id > 0 ? '留空 = 保持原 Key 不变' : '本地 Ollama 无需填写'" />
        </el-form-item>
        <el-form-item label="模型标识" required>
          <el-input v-model="form.modelId" placeholder="供应商文档中的 model 参数值，如 deepseek-chat" />
        </el-form-item>
        <el-form-item label="上下文上限">
          <el-input-number v-model="form.maxContextChars" :min="1000" :max="2000000" :step="10000" controls-position="right" />
          <div class="form-tip">字符数：会话历史超长时自动截断保留最近消息</div>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" inline-prompt active-text="启用" inactive-text="停用" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :loading="editTesting || editSaving" @click="doSave(true)">保存并测试</el-button>
        <el-button @click="editDialog = false">取消</el-button>
        <el-button type="primary" :loading="editSaving" :disabled="editTesting" @click="doSave(false)">保存</el-button>
      </template>
    </CommonDialog>

    <!-- 模型配置教程弹窗 -->
    <el-dialog v-model="helpVisible" title="AI 模型配置教程" width="680px" class="help-dialog">
      <div class="help-content">
        <p class="help-intro">以下是主流 AI 模型的注册地址和配置参数，点击链接注册获取 API Key：</p>
        <div class="guide-list">
          <div v-for="guide in modelGuides" :key="guide.name" class="guide-item">
            <div class="guide-header">
              <span class="guide-name">{{ guide.name }}</span>
              <el-button type="primary" size="small" @click="openUrl(guide.registerUrl)">
                前往注册
              </el-button>
            </div>
            <div class="guide-desc">{{ guide.description }}</div>
            <div class="guide-config">
              <div class="guide-config-row">
                <span class="guide-label">接口地址：</span>
                <code>{{ guide.baseUrl }}</code>
              </div>
              <div class="guide-config-row">
                <span class="guide-label">模型标识：</span>
                <code>{{ guide.modelId }}</code>
              </div>
            </div>
          </div>
        </div>
        <div class="help-tips">
          <h4>配置步骤</h4>
          <ol>
            <li>点击「前往注册」注册账号并获取 API Key</li>
            <li>点「新增模型」，填写名称、接口地址、API Key、模型标识</li>
            <li>点「测试」验证连通性</li>
            <li>设为默认模型后即可在 AI 助手中使用</li>
          </ol>
          <p><strong>提示：</strong>所有支持 OpenAI 兼容协议的模型均可接入（如 vLLM、LocalAI 等）</p>
        </div>
      </div>
    </el-dialog>
  </el-card>
</template>

<style scoped>
.model-card {
  border-radius: 16px;
  border: 1px solid #e4e7ed;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.04) !important;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 4px 0;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
  color: #1f2d3d;
  display: flex;
  align-items: center;
  gap: 10px;
  flex: 1;
}

.card-icon {
  font-size: 20px;
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: linear-gradient(135deg, #e0f2fe, #f0f9ff);
  border-radius: 10px;
}

.default-hint {
  font-size: 13px;
  color: #909399;
}

.model-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-height: 60px;
}

.empty-icon {
  font-size: 48px;
}

.empty-tip {
  font-size: 13px;
  color: #909399;
  margin: 0;
  padding: 0 24px;
  line-height: 1.6;
}

.model-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 14px 16px;
  border: 1px solid #f0f0f0;
  border-radius: 12px;
  transition: border-color 0.2s;
}

/* 模型名称单元格 */
.model-name-cell {
  font-weight: 500;
}

.default-tag {
  margin-left: 6px;
  font-size: 12px;
  color: var(--el-color-primary);
}

.edit-form {
  padding: 4px 8px;
}

.form-tip {
  font-size: 12px;
  color: #909399;
  line-height: 1.5;
  margin-top: 4px;
}

/* 帮助图标 */
.help-icon {
  font-size: 16px;
  color: #909399;
  cursor: pointer;
  margin-left: 6px;
  transition: color 0.2s;
}

.help-icon:hover {
  color: #409eff;
}

/* 帮助弹窗 */
.help-content {
  padding: 0 8px;
}

.help-intro {
  color: #606266;
  margin-bottom: 16px;
}

.guide-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.guide-item {
  padding: 16px;
  background: #f5f7fa;
  border-radius: 8px;
  border: 1px solid #e4e7ed;
}

.guide-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 8px;
}

.guide-name {
  font-size: 15px;
  font-weight: 600;
  color: #1f2d3d;
}

.guide-desc {
  font-size: 13px;
  color: #606266;
  margin-bottom: 12px;
}

.guide-config {
  background: #fff;
  padding: 12px;
  border-radius: 6px;
  border: 1px solid #e4e7ed;
}

.guide-config-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 8px;
}

.guide-config-row:last-child {
  margin-bottom: 0;
}

.guide-label {
  font-size: 13px;
  color: #909399;
  flex-shrink: 0;
}

.guide-config code {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  color: #409eff;
  background: #f0f9ff;
  padding: 2px 8px;
  border-radius: 4px;
  word-break: break-all;
}

.help-tips {
  margin-top: 20px;
  padding: 16px;
  background: #fdf6ec;
  border-radius: 8px;
  border: 1px solid #faecd8;
}

.help-tips h4 {
  margin: 0 0 12px 0;
  color: #e6a23c;
  font-size: 14px;
}

.help-tips ol {
  margin: 0 0 12px 0;
  padding-left: 20px;
}

.help-tips li {
  color: #606266;
  margin-bottom: 6px;
  font-size: 13px;
}

.help-tips p {
  margin: 0;
  font-size: 13px;
  color: #909399;
}
</style>
