<script setup lang="ts">
import { onMounted, ref, computed } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import {
  getScanRules, saveScanRule, deleteScanRule, importScanTemplates,
  runCodeScan, getScanResults, getScanResult,
  type CodeScanRule, type CodeScanResult, type CodeScanIssue,
} from '@/common/api/codeScan'

// ===== 状态 =====
const activeTab = ref('results')
const targetPath = ref('')
const scanning = ref(false)
const currentResult = ref<CodeScanResult | null>(null)
const historyList = ref<CodeScanResult[]>([])
const rules = ref<CodeScanRule[]>([])

// 规则编辑弹窗
const ruleDialogVisible = ref(false)
const editingRule = ref<Partial<CodeScanRule>>({})

// ===== 计算 =====
const issuesByFile = computed(() => {
  if (!currentResult.value?.issues) return []
  const map = new Map<string, CodeScanIssue[]>()
  for (const issue of currentResult.value.issues) {
    const list = map.get(issue.filePath) || []
    list.push(issue)
    map.set(issue.filePath, list)
  }
  return Array.from(map.entries()).map(([file, issues]) => ({ file, issues }))
})

// ===== 生命周期 =====
onMounted(async () => {
  await Promise.all([loadRules(), loadHistory()])
})

// ===== 方法 =====
async function loadRules() {
  try {
    const res = await getScanRules()
    rules.value = res.data ?? []
  } catch { /* ignore */ }
}

async function loadHistory() {
  try {
    const res = await getScanResults()
    historyList.value = res.data ?? []
  } catch { /* ignore */ }
}

async function handleScan(mode: 'full' | 'diff') {
  if (!targetPath.value.trim()) {
    ElMessage.warning('请输入扫描目标目录')
    return
  }
  scanning.value = true
  try {
    const res = await runCodeScan(targetPath.value.trim(), mode)
    currentResult.value = res.data ?? null
    ElMessage.success(`扫描完成: ${currentResult.value?.issueCount ?? 0} 个问题`)
    await loadHistory()
  } catch (e: any) {
    ElMessage.error(e?.message || '扫描失败')
  } finally {
    scanning.value = false
  }
}

async function viewHistoryResult(id: number) {
  try {
    const res = await getScanResult(id)
    currentResult.value = res.data ?? null
  } catch {
    ElMessage.error('加载扫描结果失败')
  }
}

async function handleImportTemplates() {
  try {
    await importScanTemplates()
    ElMessage.success('模板导入完成')
    await loadRules()
  } catch {
    ElMessage.error('导入失败')
  }
}

function openRuleDialog(rule?: CodeScanRule) {
  editingRule.value = rule ? { ...rule } : {
    name: '硬编码密码',
    pattern: '(?i)(password|passwd|pwd)\\s*[:=]\\s*"[^"]{3,}"',
    severity: 'Error',
    fileGlob: '*.cs;*.json;*.ts',
    enabled: true,
    description: '检测代码中硬编码的密码字符串',
  }
  ruleDialogVisible.value = true
}

/** 下载规则模板示例 JSON */
function downloadTemplate() {
  const templates = [
    { name: '硬编码密码', pattern: '(?i)(password|passwd|pwd)\\s*[:=]\\s*"[^"]{3,}"', severity: 'Error', fileGlob: '*.cs;*.json;*.ts', enabled: true, description: '检测代码中硬编码的密码字符串' },
    { name: '连接串明文', pattern: '(?i)(password|pwd)\\s*=\\s*[^;\\s"\']+', severity: 'Error', fileGlob: '*.json;*.config;*.xml', enabled: true, description: '检测配置文件中的明文连接字符串密码' },
    { name: 'Console输出残留', pattern: 'Console\\.(Write|WriteLine)\\(', severity: 'Warning', fileGlob: '*.cs', enabled: true, description: '生产代码中不应出现 Console 输出' },
    { name: 'TODO/HACK注释', pattern: '(?i)\\b(TODO|HACK|FIXME|XXX)\\b', severity: 'Info', fileGlob: '*.cs;*.ts;*.vue;*.js', enabled: true, description: '标记待处理的 TODO/HACK/FIXME 注释' },
    { name: 'Thread.Sleep', pattern: '\\bThread\\.Sleep\\s*\\(', severity: 'Warning', fileGlob: '*.cs', enabled: true, description: '应使用 async/await + Task.Delay 替代 Thread.Sleep' },
    { name: '硬编码IP地址', pattern: '\\b\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\b', severity: 'Info', fileGlob: '*.cs;*.json;*.ts', enabled: true, description: '硬编码 IP 地址应提取为配置项' },
  ]
  const blob = new Blob([JSON.stringify(templates, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = 'code-scan-rules-template.json'
  a.click()
  URL.revokeObjectURL(url)
}

async function handleSaveRule() {
  if (!editingRule.value.name || !editingRule.value.pattern) {
    ElMessage.warning('请填写规则名称和正则表达式')
    return
  }
  try {
    await saveScanRule(editingRule.value)
    ElMessage.success('规则已保存')
    ruleDialogVisible.value = false
    await loadRules()
  } catch (e: any) {
    ElMessage.error(e?.message || '保存失败')
  }
}

async function handleDeleteRule(id: number) {
  try {
    await ElMessageBox.confirm('确定删除该规则？', '确认', { type: 'warning' })
    await deleteScanRule(id)
    ElMessage.success('已删除')
    await loadRules()
  } catch { /* 取消 */ }
}

async function toggleRuleEnabled(rule: CodeScanRule) {
  await saveScanRule({ ...rule, enabled: rule.enabled })
  ElMessage.success(rule.enabled ? '已启用' : '已禁用')
}

function severityColor(severity: string) {
  if (severity === 'Error') return '#f56c6c'
  if (severity === 'Warning') return '#e6a23c'
  return '#909399'
}
</script>

<template>
  <div class="code-scan-page">
    <!-- 顶部工具栏 -->
    <div class="scan-toolbar">
      <el-input
        v-model="targetPath"
        placeholder="扫描目录路径，如 /home/projects/MyApp 或 E:\Code\Project"
        style="flex: 1; max-width: 500px"
        @keyup.enter="handleScan('full')"
      />
      <el-button type="primary" :loading="scanning" @click="handleScan('full')">
        全量扫描
      </el-button>
      <el-button type="warning" :loading="scanning" @click="handleScan('diff')">
        增量扫描 (git diff)
      </el-button>
    </div>

    <!-- 页签 -->
    <el-tabs v-model="activeTab" class="scan-tabs">
      <!-- 扫描结果 -->
      <el-tab-pane label="扫描结果" name="results">
        <!-- 当前结果摘要 -->
        <div v-if="currentResult" class="result-summary">
          <div class="summary-stats">
            <span class="stat-item">
              <span class="stat-label">文件:</span> {{ currentResult.fileCount }}
            </span>
            <span class="stat-item">
              <span class="stat-label">问题:</span>
              <span :style="{ color: currentResult.issueCount > 0 ? '#f56c6c' : '#67c23a' }">
                {{ currentResult.issueCount }}
              </span>
            </span>
            <span class="stat-item" style="color: #f56c6c">Error: {{ currentResult.errorCount }}</span>
            <span class="stat-item" style="color: #e6a23c">Warning: {{ currentResult.warningCount }}</span>
            <span class="stat-item" style="color: #909399">Info: {{ currentResult.infoCount }}</span>
            <span class="stat-item">耗时: {{ currentResult.durationMs }}ms</span>
            <span class="stat-item" style="color: #909399">{{ currentResult.createTime }}</span>
          </div>

          <!-- 按文件分组的问题列表 -->
          <div class="issues-container">
            <div v-for="group in issuesByFile" :key="group.file" class="file-group">
              <div class="file-header">
                <span class="file-icon">📄</span>
                <span class="file-path">{{ group.file }}</span>
                <span class="issue-count">{{ group.issues.length }} 个问题</span>
              </div>
              <div class="issue-list">
                <div v-for="issue in group.issues" :key="issue.id" class="issue-item">
                  <span class="issue-line">L{{ issue.lineNumber }}</span>
                  <el-tag :color="severityColor(issue.severity)" size="small" effect="dark" class="severity-tag">
                    {{ issue.severity }}
                  </el-tag>
                  <span class="issue-rule">{{ issue.ruleName }}</span>
                  <code class="issue-code">{{ issue.lineContent }}</code>
                </div>
              </div>
            </div>
            <div v-if="issuesByFile.length === 0" class="no-issues">
              ✅ 未发现问题
            </div>
          </div>
        </div>
        <div v-else class="empty-state">选择目录并点击扫描开始检测</div>

        <!-- 历史记录 -->
        <div v-if="historyList.length > 0" class="history-section">
          <div class="section-title">历史记录</div>
          <el-table :data="historyList" size="small" stripe @row-click="(row: CodeScanResult) => viewHistoryResult(row.id)">
            <el-table-column prop="createTime" label="时间" width="160" />
            <el-table-column prop="scanMode" label="模式" width="80">
              <template #default="{ row }">
                <el-tag size="small" :type="row.scanMode === 'diff' ? 'warning' : 'info'">
                  {{ row.scanMode === 'diff' ? '增量' : '全量' }}
                </el-tag>
              </template>
            </el-table-column>
            <el-table-column prop="fileCount" label="文件" width="70" />
            <el-table-column prop="issueCount" label="问题" width="70">
              <template #default="{ row }">
                <span :style="{ color: row.issueCount > 0 ? '#f56c6c' : '#67c23a', fontWeight: 600 }">
                  {{ row.issueCount }}
                </span>
              </template>
            </el-table-column>
            <el-table-column prop="durationMs" label="耗时" width="80">
              <template #default="{ row }">{{ row.durationMs }}ms</template>
            </el-table-column>
            <el-table-column prop="targetPath" label="目录" show-overflow-tooltip />
          </el-table>
        </div>
      </el-tab-pane>

      <!-- 规则管理 -->
      <el-tab-pane label="规则管理" name="rules">
        <div class="rules-toolbar">
          <el-button size="small" type="primary" @click="openRuleDialog()">+ 新建规则</el-button>
          <el-button size="small" @click="handleImportTemplates">导入内置模板</el-button>
        </div>
        <el-table :data="rules" size="small" stripe>
          <el-table-column prop="name" label="名称" width="160" />
          <el-table-column prop="pattern" label="正则表达式" show-overflow-tooltip />
          <el-table-column prop="severity" label="等级" width="80">
            <template #default="{ row }">
              <el-tag :color="severityColor(row.severity)" size="small" effect="dark">{{ row.severity }}</el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="fileGlob" label="文件匹配" width="140" show-overflow-tooltip />
          <el-table-column label="启用" width="70">
            <template #default="{ row }">
              <el-switch v-model="row.enabled" @change="toggleRuleEnabled(row)" />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="120">
            <template #default="{ row }">
              <el-button link size="small" type="primary" @click="openRuleDialog(row)">编辑</el-button>
              <el-button link size="small" type="danger" @click="handleDeleteRule(row.id)">删除</el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>
    </el-tabs>

    <!-- 规则编辑弹窗 -->
    <el-dialog v-model="ruleDialogVisible" :title="editingRule.id ? '编辑规则' : '新建规则'" width="520px">
      <el-form label-width="90px">
        <el-form-item label="名称">
          <el-input v-model="editingRule.name" placeholder="规则名称" />
        </el-form-item>
        <el-form-item label="正则表达式">
          <el-input v-model="editingRule.pattern" placeholder="正则模式（支持 .NET 正则语法）" />
        </el-form-item>
        <el-form-item label="严重等级">
          <el-select v-model="editingRule.severity" style="width: 120px">
            <el-option label="Error" value="Error" />
            <el-option label="Warning" value="Warning" />
            <el-option label="Info" value="Info" />
          </el-select>
        </el-form-item>
        <el-form-item label="文件匹配">
          <el-input v-model="editingRule.fileGlob" placeholder="如 *.cs;*.json（空=全部文件）" />
        </el-form-item>
        <el-form-item label="说明">
          <el-input v-model="editingRule.description" type="textarea" :rows="2" placeholder="规则说明" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="editingRule.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <div style="display: flex; justify-content: space-between; width: 100%">
          <el-button type="info" plain size="small" @click="downloadTemplate" v-if="!editingRule.id">📥 下载模板</el-button>
          <span v-else></span>
          <div>
            <el-button @click="ruleDialogVisible = false">取消</el-button>
            <el-button type="primary" @click="handleSaveRule">保存</el-button>
          </div>
        </div>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.code-scan-page {
  height: 100%;
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  padding: 20px;
  background: #f5f7fa;
}

.scan-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
  padding: 16px;
  background: #fff;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.06);
}

.scan-tabs {
  flex: 1;
  background: #fff;
  border-radius: 8px;
  padding: 0 16px 16px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.06);
}

/* 结果摘要 */
.result-summary { margin-bottom: 20px; }
.summary-stats {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 16px;
  background: #f8f9fb;
  border-radius: 6px;
  margin-bottom: 16px;
  font-size: 13px;
}
.stat-label { color: #909399; margin-right: 4px; }

/* 问题列表 */
.issues-container { max-height: 500px; overflow-y: auto; }
.file-group { margin-bottom: 12px; border: 1px solid #ebeef5; border-radius: 6px; overflow: hidden; }
.file-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  background: #fafafa;
  border-bottom: 1px solid #ebeef5;
  font-size: 13px;
}
.file-icon { font-size: 14px; }
.file-path { font-weight: 500; color: #303133; flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.issue-count { font-size: 12px; color: #909399; }

.issue-list { padding: 0; }
.issue-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 12px;
  border-bottom: 1px solid #f5f5f5;
  font-size: 12px;
}
.issue-item:last-child { border-bottom: none; }
.issue-line {
  color: #606266;
  font-weight: 600;
  font-family: monospace;
  min-width: 42px;
}
.severity-tag { border: none !important; font-size: 11px; }
.issue-rule { color: #606266; font-weight: 500; min-width: 80px; }
.issue-code {
  flex: 1;
  font-family: 'Cascadia Code', Consolas, monospace;
  font-size: 12px;
  color: #4a4a4a;
  background: #f8f8f8;
  padding: 2px 6px;
  border-radius: 3px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.no-issues {
  text-align: center;
  padding: 40px;
  color: #67c23a;
  font-size: 14px;
}

.empty-state {
  text-align: center;
  padding: 60px 20px;
  color: #909399;
  font-size: 14px;
}

/* 历史记录 */
.history-section { margin-top: 24px; }
.section-title { font-size: 14px; font-weight: 600; color: #303133; margin-bottom: 12px; }

/* 规则管理 */
.rules-toolbar { margin-bottom: 12px; display: flex; gap: 8px; }
</style>
