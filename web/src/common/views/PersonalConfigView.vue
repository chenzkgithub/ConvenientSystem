<script setup lang="ts">
/**
 * 个人配置页面：当前登录用户的个性化配置（锁屏开关/超时等），仅影响自己。
 * 原系统配置页签中的「个人配置」拆分出来的独立菜单页面。
 */
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import { getMyConfig, updateMyConfig } from '@/common/api/userConfig'
import type { UserConfigGroup } from '@/common/api/userConfig'
import { useLockStore } from '@/common/stores/lock'

const lock = useLockStore()

/** 分组图标映射 */
const CATEGORY_ICONS: Record<string, string> = {
  '锁屏设置': '🔐',
}

const myGroups = ref<UserConfigGroup[]>([])
const myLoading = ref(false)

/** 编辑副本：configKey → 当前值 */
const myEditMap = ref<Record<string, string>>({})
/** 原始快照：configKey → 加载时值 */
const myOriginalMap = ref<Record<string, string>>({})
/** 每组保存中状态 */
const mySavingGroup = ref<Set<string>>(new Set())

/** 切换开关值：DB 存 "true"/"false" 字符串 */
function getMySwitchVal(key: string): boolean {
  return myEditMap.value[key] === 'true'
}
function setMySwitchVal(key: string, val: boolean) {
  myEditMap.value[key] = val ? 'true' : 'false'
}

/** 数字值：DB 存字符串 */
function getMyNumberVal(key: string): number {
  const v = myEditMap.value[key]
  return v ? parseInt(v, 10) || 0 : 0
}
function setMyNumberVal(key: string, val: number | undefined) {
  myEditMap.value[key] = val != null ? String(val) : ''
}

function isMyGroupDirty(category: string): boolean {
  const group = myGroups.value.find(g => g.category === category)
  if (!group) return false
  return group.items.some(item => {
    const cur = myEditMap.value[item.configKey] ?? ''
    const orig = myOriginalMap.value[item.configKey] ?? ''
    return cur !== orig
  })
}

async function loadMyConfig() {
  myLoading.value = true
  try {
    const data = await getMyConfig()
    myGroups.value = data || []
    myEditMap.value = {}
    myOriginalMap.value = {}
    for (const g of myGroups.value) {
      for (const item of g.items) {
        myEditMap.value[item.configKey] = item.configValue || ''
        myOriginalMap.value[item.configKey] = item.configValue || ''
      }
    }
  } catch {
    /* getMyConfig 已弹错误提示 */
  } finally {
    myLoading.value = false
  }
}

async function saveMyGroup(group: UserConfigGroup) {
  const dirtyItems = group.items
    .filter(item => {
      const cur = myEditMap.value[item.configKey] ?? ''
      const orig = myOriginalMap.value[item.configKey] ?? ''
      return cur !== orig
    })
    .map(item => ({
      configKey: item.configKey,
      configValue: myEditMap.value[item.configKey] ?? '',
    }))
  if (dirtyItems.length === 0) {
    ElMessage.info('没有修改项')
    return
  }
  mySavingGroup.value.add(group.category)
  try {
    await updateMyConfig(dirtyItems)
    for (const item of dirtyItems) {
      myOriginalMap.value[item.configKey] = item.configValue
    }
    ElMessage.success(`${group.category} 配置已保存`)
    // 锁屏相关配置变更后，立即刷新锁屏功能开关状态。
    // loadConfig 只负责「关掉」与「改超时」的联动；从关到开时空闲计时尚未启用，
    // 需要再走一次 start()（幂等，且内部自带开关判断）才能当场生效。
    if (dirtyItems.some(i => i.configKey === 'AppSettings.EnableLock' || i.configKey === 'AppSettings.LockTimeout')) {
      await lock.loadConfig()
      lock.start()
    }
  } catch {
    /* updateMyConfig 已弹错误提示 */
  } finally {
    mySavingGroup.value.delete(group.category)
  }
}

loadMyConfig()
</script>

<template>
  <div class="personal-config-page" v-loading="myLoading"> 
    <div v-for="group in myGroups" :key="group.category" class="config-card-wrapper">
      <el-card shadow="hover" class="config-card">
        <template #header>
          <div class="card-header">
            <span class="card-title">
              <span class="card-icon">{{ CATEGORY_ICONS[group.category] || '👤' }}</span>
              {{ group.category }}
            </span>
            <el-button
              v-if="$has('personal-config:save')"
              type="primary"
              size="small"
              :loading="mySavingGroup.has(group.category)"
              :disabled="!isMyGroupDirty(group.category)"
              @click="saveMyGroup(group)"
            >
              保存{{ isMyGroupDirty(group.category) ? ' *' : '' }}
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
              <!-- switch -->
              <el-switch
                v-if="item.inputType === 'switch'"
                :model-value="getMySwitchVal(item.configKey)"
                @update:model-value="(v: string | number | boolean) => setMySwitchVal(item.configKey, v === true)"
                active-text="开启"
                inactive-text="关闭"
                inline-prompt
                style="--el-switch-on-color: #409eff;"
              />

              <!-- number -->
              <el-input-number
                v-else-if="item.inputType === 'number'"
                :model-value="getMyNumberVal(item.configKey)"
                @update:model-value="(v: number | undefined) => setMyNumberVal(item.configKey, v)"
                :min="0"
                controls-position="right"
                class="config-number"
              />

              <!-- text -->
              <el-input
                v-else
                v-model="myEditMap[item.configKey]"
                :placeholder="`请输入${item.displayName}`"
                class="config-input"
              />
            </div>
          </div>
        </div>
      </el-card>
    </div>
    <el-empty v-if="myGroups.length === 0 && !myLoading" description="暂无个人配置" />
  </div>
</template>

<style scoped>
.personal-config-page {
  max-width: 720px;
  margin: 0 auto;
  padding: 24px;
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

.config-number {
  width: 160px;
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
</style>
