<script setup lang="ts">
/**
 * 系统配置管理页面（分页签）
 * - el-tabs 两个页签：系统配置 / 第三方配置
 * - 按 Category 分组展示，支持 text/password/number/switch
 * - password 类型默认脱敏（••••••••），点击「查看明文」需验证登录密码后回填
 * - 每组独立保存，仅提交修改过的配置项
 * - 个人配置已拆分为独立菜单页面 PersonalConfigView.vue
 */
import { ref, computed } from 'vue'
import { ElMessage } from 'element-plus'
import { Lock } from '@element-plus/icons-vue'
import { httpGet, httpPost, httpPut } from '@/api/request'
import CommonDialog from '@/common/components/CommonDialog.vue'

interface SysConfigItem {
  id: number
  configKey: string
  configValue: string
  category: string
  displayName: string
  description: string | null
  inputType: string
  tabGroup: string
  sortOrder: number
}

interface SysConfigGroup {
  category: string
  items: SysConfigItem[]
}

const MASKED = '••••••••'

/** 分组图标映射 */
const CATEGORY_ICONS: Record<string, string> = {
  '翻译服务': '🔤',
  '系统安全': '🔒',
  '日志管理': '📋',
  '系统配置': '⚙️',
}

const allGroups = ref<SysConfigGroup[]>([])
const loading = ref(false)
const activeTab = ref('system')

/** 编辑副本：configKey → 当前值 */
const editMap = ref<Record<string, string>>({})
/** 原始快照：configKey → 加载时值（password 为脱敏占位） */
const originalMap = ref<Record<string, string>>({})
/** 已验证明文的 key 集合 */
const revealedKeys = ref<Set<string>>(new Set())
/** 每组保存中状态 */
const savingGroup = ref<Set<string>>(new Set())

/** 密码验证弹窗 */
const revealDialog = ref(false)
const revealKey = ref('')
const revealPassword = ref('')
const revealLoading = ref(false)

/** 按页签筛选分组 */
const systemGroups = computed(() =>
  allGroups.value.filter(g => g.items.some(i => i.tabGroup !== 'thirdparty'))
)
const thirdpartyGroups = computed(() =>
  allGroups.value.filter(g => g.items.some(i => i.tabGroup === 'thirdparty'))
)
const hasAnyData = computed(() => allGroups.value.length > 0)

/** 切换开关值：DB 存 "true"/"false" 字符串 */
function getSwitchVal(key: string): boolean {
  return editMap.value[key] === 'true'
}
function setSwitchVal(key: string, val: boolean) {
  editMap.value[key] = val ? 'true' : 'false'
}

/** 数字值：DB 存字符串 */
function getNumberVal(key: string): number {
  const v = editMap.value[key]
  return v ? parseInt(v, 10) || 0 : 0
}
function setNumberVal(key: string, val: number | undefined) {
  editMap.value[key] = val != null ? String(val) : ''
}

/** 某组是否有修改（脱敏占位符不算修改） */
function isGroupDirty(category: string): boolean {
  const group = allGroups.value.find(g => g.category === category)
  if (!group) return false
  return group.items.some(item => {
    const cur = editMap.value[item.configKey] ?? ''
    const orig = originalMap.value[item.configKey] ?? ''
    return cur !== orig && cur !== MASKED
  })
}

async function loadConfigs() {
  loading.value = true
  try {
    const data = await httpGet<SysConfigGroup[]>('/api/Common/SysConfig/GetAll')
    allGroups.value = data || []
    editMap.value = {}
    originalMap.value = {}
    revealedKeys.value.clear()
    for (const g of allGroups.value) {
      for (const item of g.items) {
        editMap.value[item.configKey] = item.configValue || ''
        originalMap.value[item.configKey] = item.configValue || ''
      }
    }
  } catch {
    /* httpGet 已弹错误提示 */
  } finally {
    loading.value = false
  }
}

/** 打开密码验证弹窗 */
function openRevealDialog(key: string) {
  revealKey.value = key
  revealPassword.value = ''
  revealDialog.value = true
}

/** 验证密码并回填明文 */
async function doReveal() {
  if (!revealPassword.value) {
    ElMessage.warning('请输入登录密码')
    return
  }
  revealLoading.value = true
  try {
    const result = await httpPost<{ ok: boolean; value: string | null }>(
      '/api/Common/SysConfig/RevealValue',
      { configKey: revealKey.value, password: revealPassword.value },
    )
    if (result?.ok && result.value != null) {
      editMap.value[revealKey.value] = result.value
      originalMap.value[revealKey.value] = result.value
      revealedKeys.value.add(revealKey.value)
      revealDialog.value = false
      ElMessage.success('验证通过')
    } else {
      ElMessage.error('密码错误')
    }
  } catch {
    /* httpPost 已弹错误提示 */
  } finally {
    revealLoading.value = false
  }
}

async function saveGroup(group: SysConfigGroup) {
  const dirtyItems = group.items
    .filter(item => {
      const cur = editMap.value[item.configKey] ?? ''
      const orig = originalMap.value[item.configKey] ?? ''
      return cur !== orig && cur !== MASKED
    })
    .map(item => ({
      configKey: item.configKey,
      configValue: editMap.value[item.configKey] ?? '',
    }))
  if (dirtyItems.length === 0) {
    ElMessage.info('没有修改项')
    return
  }

  savingGroup.value.add(group.category)
  try {
    await httpPut('/api/Common/SysConfig/UpdateBatch', dirtyItems)
    for (const item of dirtyItems) {
      originalMap.value[item.configKey] = item.configValue
    }
    ElMessage.success(`${group.category} 配置已保存`)
  } catch {
    /* httpPut 已弹错误提示 */
  } finally {
    savingGroup.value.delete(group.category)
  }
}

loadConfigs()
</script>

<template>
  <div class="sys-config-page" v-loading="loading">
    <el-tabs v-model="activeTab" class="config-tabs">
      <!-- 系统配置 -->
      <el-tab-pane label="系统配置" name="system">
        <div v-for="group in systemGroups" :key="group.category" class="config-card-wrapper">
          <el-card shadow="hover" class="config-card">
            <template #header>
              <div class="card-header">
                <span class="card-title">
                  <span class="card-icon">{{ CATEGORY_ICONS[group.category] || '📦' }}</span>
                  {{ group.category }}
                </span>
                <el-button
                  type="primary"
                  size="small"
                  :loading="savingGroup.has(group.category)"
                  :disabled="!isGroupDirty(group.category)"
                  @click="saveGroup(group)"
                >
                  保存{{ isGroupDirty(group.category) ? ' *' : '' }}
                </el-button>
              </div>
            </template>
            <div class="config-form">
              <div
                v-for="item in group.items"
                :key="item.configKey"
                class="config-item"
                :title="item.configKey"
              >
                <div class="config-label">
                  <span class="config-name">{{ item.displayName }}</span>
                  <span v-if="item.description" class="config-desc">{{ item.description }}</span>
                </div>
                <div class="config-control">
                  <!-- password（脱敏，需验证查看） -->
                  <template v-if="item.inputType === 'password'">
                    <el-input
                      v-if="revealedKeys.has(item.configKey)"
                      v-model="editMap[item.configKey]"
                      type="password"
                      show-password
                      :placeholder="`请输入${item.displayName}`"
                      class="config-input"
                    />
                    <template v-else>
                      <el-input
                        :model-value="MASKED"
                        type="password"
                        readonly
                        class="config-input masked-input"
                      />
                      <el-button
                        type="warning"
                        plain
                        size="small"
                        @click="openRevealDialog(item.configKey)"
                      >
                        查看明文
                      </el-button>
                    </template>
                  </template>

                  <!-- switch -->
                  <el-switch
                    v-else-if="item.inputType === 'switch'"
                    :model-value="getSwitchVal(item.configKey)"
                    @update:model-value="(v: string | number | boolean) => setSwitchVal(item.configKey, v === true)"
                    active-text="开启"
                    inactive-text="关闭"
                    inline-prompt
                    style="--el-switch-on-color: #409eff;"
                  />

                  <!-- number -->
                  <el-input-number
                    v-else-if="item.inputType === 'number'"
                    :model-value="getNumberVal(item.configKey)"
                    @update:model-value="(v: number | undefined) => setNumberVal(item.configKey, v)"
                    :min="0"
                    controls-position="right"
                    class="config-number"
                  />

                  <!-- text -->
                  <el-input
                    v-else
                    v-model="editMap[item.configKey]"
                    :placeholder="`请输入${item.displayName}`"
                    class="config-input"
                  />
                </div>
              </div>
            </div>
          </el-card>
        </div>
        <el-empty v-if="systemGroups.length === 0 && !loading" description="暂无系统配置" />
      </el-tab-pane>

      <!-- 第三方配置 -->
      <el-tab-pane label="第三方配置" name="thirdparty">
        <el-alert
          title="安全提示"
          type="warning"
          :closable="false"
          show-icon
          description="第三方配置包含 API 密钥等敏感信息，查看明文需验证登录密码。"
          class="security-alert"
        />
        <div v-for="group in thirdpartyGroups" :key="group.category" class="config-card-wrapper">
          <el-card shadow="hover" class="config-card">
            <template #header>
              <div class="card-header">
                <span class="card-title">
                  <span class="card-icon">{{ CATEGORY_ICONS[group.category] || '📦' }}</span>
                  {{ group.category }}
                </span>
                <el-button
                  type="primary"
                  size="small"
                  :loading="savingGroup.has(group.category)"
                  :disabled="!isGroupDirty(group.category)"
                  @click="saveGroup(group)"
                >
                  保存{{ isGroupDirty(group.category) ? ' *' : '' }}
                </el-button>
              </div>
            </template>
            <div class="config-form">
              <div
                v-for="item in group.items"
                :key="item.configKey"
                class="config-item"
                :title="item.configKey"
              >
                <div class="config-label">
                  <span class="config-name">{{ item.displayName }}</span>
                  <span v-if="item.description" class="config-desc">{{ item.description }}</span>
                </div>
                <div class="config-control">
                  <!-- password（脱敏，需验证查看） -->
                  <template v-if="item.inputType === 'password'">
                    <el-input
                      v-if="revealedKeys.has(item.configKey)"
                      v-model="editMap[item.configKey]"
                      type="password"
                      show-password
                      :placeholder="`请输入${item.displayName}`"
                      class="config-input"
                    />
                    <template v-else>
                      <el-input
                        :model-value="MASKED"
                        type="password"
                        readonly
                        class="config-input masked-input"
                      />
                      <el-button
                        type="warning"
                        plain
                        size="small"
                        @click="openRevealDialog(item.configKey)"
                      >
                        查看明文
                      </el-button>
                    </template>
                  </template>

                  <!-- switch -->
                  <el-switch
                    v-else-if="item.inputType === 'switch'"
                    :model-value="getSwitchVal(item.configKey)"
                    @update:model-value="(v: string | number | boolean) => setSwitchVal(item.configKey, v === true)"
                    active-text="开启"
                    inactive-text="关闭"
                    inline-prompt
                    style="--el-switch-on-color: #409eff;"
                  />

                  <!-- number -->
                  <el-input-number
                    v-else-if="item.inputType === 'number'"
                    :model-value="getNumberVal(item.configKey)"
                    @update:model-value="(v: number | undefined) => setNumberVal(item.configKey, v)"
                    :min="0"
                    controls-position="right"
                    class="config-number"
                  />

                  <!-- text -->
                  <el-input
                    v-else
                    v-model="editMap[item.configKey]"
                    :placeholder="`请输入${item.displayName}`"
                    class="config-input"
                  />
                </div>
              </div>
            </div>
          </el-card>
        </div>
        <el-empty v-if="thirdpartyGroups.length === 0 && !loading" description="暂无第三方配置" />
      </el-tab-pane>

    </el-tabs>

    <!-- 密码验证弹窗 -->
    <CommonDialog
      v-model="revealDialog"
      title="身份验证"
      width="360px"
      :close-on-click-modal="false"
    >
      <div class="reveal-dialog-body">
        <el-icon class="reveal-icon"><Lock /></el-icon>
        <p class="reveal-tip">请输入您的登录密码以查看敏感配置</p>
        <el-input
          v-model="revealPassword"
          type="password"
          show-password
          placeholder="登录密码"
          @keyup.enter="doReveal"
        />
      </div>
      <template #footer>
        <el-button @click="revealDialog = false">取消</el-button>
        <el-button type="primary" :loading="revealLoading" @click="doReveal">确认</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.sys-config-page {
  max-width: 720px;
  margin: 0 auto;
  padding: 24px;
}

.config-tabs {
  --el-tabs-header-height: 46px;
}

.security-alert {
  margin-bottom: 20px;
  border-radius: 8px;
}

.config-card-wrapper {
  margin-bottom: 24px;
}

.config-card {
  border-radius: 16px;
  border: 1px solid #e4e7ed;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.04) !important;
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 0;
}

.card-title {
  font-size: 16px;
  font-weight: 600;
  color: #1f2d3d;
  display: flex;
  align-items: center;
  gap: 10px;
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

.config-form {
  display: flex;
  flex-direction: column;
}

.config-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 24px;
  padding: 20px 0;
  border-bottom: 1px solid #f0f0f0;
}

.config-item:last-child {
  border-bottom: none;
}

.config-label {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.config-name {
  font-size: 15px;
  font-weight: 500;
  color: #303133;
}

.config-key {
  display: none;
}

.config-desc {
  font-size: 13px;
  color: #909399;
  line-height: 1.5;
}

.config-control {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 12px;
}

.config-input {
  width: 260px;
}

.masked-input {
  width: 180px;
}

.config-number {
  width: 160px;
}

/* 密码验证弹窗 */
.reveal-dialog-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
}

.reveal-icon {
  font-size: 36px;
  color: #e6a23c;
}

.reveal-tip {
  font-size: 13px;
  color: #606266;
  margin: 0;
  text-align: center;
}

/* 卡片头部背景优化 */
:deep(.el-card__header) {
  background: #fafbfc;
  padding: 16px 24px;
  border-bottom: 1px solid #f0f0f0;
}

:deep(.el-card__body) {
  padding: 0 24px;
}

/* 保存按钮 hover 时高亮 */
.card-header .el-button:not(:disabled):hover {
  box-shadow: 0 2px 8px rgba(64, 158, 255, 0.3);
}
</style>
