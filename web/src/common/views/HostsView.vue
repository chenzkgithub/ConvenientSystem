<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { Refresh, Plus, Search, FolderOpened, Clock, Delete, EditPen, Collection } from '@element-plus/icons-vue'
import { getHosts, saveHosts, getHostsBackups, restoreHostsBackup, type HostsLine, type HostsBackup } from '@/common/api/hosts'
import { hostOpenLocation } from '@/common/hostFileBridge'
import { usePermission } from '@/common/composables/usePermission'

const { has } = usePermission()

// ── 数据加载 ──
const loading = ref(false)
const available = ref(true) // 本地控制器是否存在（仅桌面端内嵌 Kestrel 提供）
const writable = ref(true)
const hostsPath = ref('')
const lines = ref<HostsLine[]>([])
const otherLineCount = ref(0)

/** 条目视图行：带行号索引，操作后按索引回写 lines */
interface EntryItem { line: HostsLine; index: number }

const entries = computed(() =>
  lines.value
    .map((line, index) => ({ line, index }))
    .filter(x => x.line.kind === 'entry'),
)

async function load() {
  loading.value = true
  try {
    const dto = await getHosts()
    available.value = true
    writable.value = dto.writable
    hostsPath.value = dto.path
    lines.value = dto.lines
    otherLineCount.value = dto.otherLineCount
  } catch {
    available.value = false
  } finally {
    loading.value = false
  }
}
onMounted(load)

// ── 筛选 ──
const groupFilter = ref('')
const keyword = ref('')
const groups = computed(() => {
  const set = new Set<string>()
  for (const l of lines.value) if (l.kind === 'group' && l.group) set.add(l.group)
  return [...set]
})
const filteredEntries = computed(() =>
  entries.value.filter(({ line }) => {
    if (groupFilter.value === '__none__') { if (line.group) return false }
    else if (groupFilter.value && (line.group ?? '') !== groupFilter.value) return false
    const kw = keyword.value.trim().toLowerCase()
    if (!kw) return true
    return (line.ip ?? '').toLowerCase().includes(kw)
      || (line.domains ?? []).some(d => d.toLowerCase().includes(kw))
      || (line.comment ?? '').toLowerCase().includes(kw)
  }),
)
const enabledCount = computed(() => entries.value.filter(e => e.line.enabled).length)

// ── 保存（所有编辑即时生效：整体写回 + 自动备份） ──
const saving = ref(false)
async function persist() {
  if (saving.value) return
  saving.value = true
  const snapshot = JSON.parse(JSON.stringify(lines.value)) as HostsLine[]
  try {
    await saveHosts(lines.value)
  } catch {
    lines.value = snapshot
    ElMessage.error('保存失败，已恢复本次修改前的内容')
    return
  } finally {
    saving.value = false
  }
  // 保存成功后重新解析：条目归属分组（entry.group）以服务端回填为准
  await load().catch(() => {})
}

// ── 启停 / 删除 ──
async function toggleEnabled(item: EntryItem) {
  lines.value[item.index] = { ...item.line, enabled: !item.line.enabled }
  await persist()
}
async function removeEntry(item: EntryItem) {
  try {
    await ElMessageBox.confirm(
      `确定删除「${item.line.ip} → ${(item.line.domains ?? []).join(' ')}」？删除前会自动备份。`,
      '删除条目',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch { return }
  lines.value.splice(item.index, 1)
  await persist()
}

// ── 添加 / 编辑 ──
const editVisible = ref(false)
const editMode = ref<'add' | 'edit'>('add')
const editIndex = ref(-1)
const form = ref({ ip: '', domains: '', comment: '', group: '' })
const addGroups = computed(() => [...groups.value, '（文件末尾）'])

function openAdd() {
  editMode.value = 'add'
  editIndex.value = -1
  const gf = groupFilter.value
  form.value = { ip: '', domains: '', comment: '', group: gf && gf !== '__none__' ? gf : '（文件末尾）' }
  editVisible.value = true
}
function openEdit(item: EntryItem) {
  editMode.value = 'edit'
  editIndex.value = item.index
  form.value = {
    ip: item.line.ip ?? '',
    domains: (item.line.domains ?? []).join('\n'),
    comment: item.line.comment ?? '',
    group: item.line.group ?? '',
  }
  editVisible.value = true
}
async function submitEdit() {
  const domains = form.value.domains.split(/[\s,，;；]+/).map(s => s.trim()).filter(Boolean)
  if (!form.value.ip.trim()) { ElMessage.warning('请填写 IP 地址'); return }
  if (domains.length === 0) { ElMessage.warning('请至少填写一个域名'); return }
  const entry: HostsLine = {
    kind: 'entry',
    ip: form.value.ip.trim(),
    domains,
    enabled: true,
    comment: form.value.comment.trim() || undefined,
  }
  if (editMode.value === 'edit' && editIndex.value >= 0) {
    // 编辑：保留原启用状态；分组未变则原地替换，变了则移动到目标分组区域
    const idx = editIndex.value
    entry.enabled = lines.value[idx].enabled
    if ((lines.value[idx].group ?? '') === form.value.group) {
      lines.value[idx] = entry
    } else {
      lines.value.splice(idx, 1)
      insertEntry(entry, form.value.group || '（文件末尾）')
    }
  } else {
    insertEntry(entry, form.value.group)
  }
  editVisible.value = false
  await persist()
}
/** 按目标分组计算插入位置：分组区域内最后一个条目之后；文件末尾直接追加 */
function insertEntry(entry: HostsLine, target: string) {
  if (!target || target === '（文件末尾）') {
    // 选「文件末尾」：追加到全文件最后
    lines.value.push(entry)
    return
  }
  if (!groups.value.includes(target)) {
    // 新分组：末尾加分组标记 + 条目
    lines.value.push({ kind: 'group', group: target })
    lines.value.push(entry)
    return
  }
  // 已有分组：定位分组区域（group 行 → 下一个 group 行之前），插到区域内最后一条 entry 之后
  let groupIdx = -1
  for (let i = 0; i < lines.value.length; i++) {
    if (lines.value[i].kind === 'group' && lines.value[i].group === target) { groupIdx = i; break }
  }
  if (groupIdx < 0) { lines.value.push(entry); return }
  let insertAt = lines.value.length
  for (let i = groupIdx + 1; i < lines.value.length; i++) {
    if (lines.value[i].kind === 'group') { insertAt = i; break }
  }
  // 区域内最后一个 entry 之后
  for (let i = insertAt - 1; i > groupIdx; i--) {
    if (lines.value[i].kind === 'entry') { insertAt = i + 1; break }
  }
  lines.value.splice(insertAt, 0, entry)
}

// ── 分组管理 ──
const groupDlgVisible = ref(false)
const newGroupName = ref('')
const renaming = ref('') // 正在重命名的分组原名，空=无
const renameInput = ref('')
/** 分组列表（名称 + 组内条目数），来自 lines 中的 group 行 */
const groupStats = computed(() =>
  lines.value
    .filter(l => l.kind === 'group' && l.group)
    .map(l => ({
      name: l.group!,
      count: entries.value.filter(e => e.line.group === l.group).length,
    })),
)
function validGroupName(name: string): boolean {
  if (!name) { ElMessage.warning('请输入分组名称'); return false }
  if (/[#\r\n]/.test(name)) { ElMessage.warning('分组名称不能包含 # 或换行'); return false }
  return true
}
async function createGroup() {
  const name = newGroupName.value.trim()
  if (!validGroupName(name)) return
  if (groups.value.includes(name)) { ElMessage.warning('分组已存在'); return }
  lines.value.push({ kind: 'group', group: name })
  newGroupName.value = ''
  await persist()
}
function startRename(name: string) {
  renaming.value = name
  renameInput.value = name
}
async function submitRename() {
  const old = renaming.value
  if (!old) return
  const name = renameInput.value.trim()
  if (name === old) { renaming.value = ''; return }
  if (!validGroupName(name)) return
  if (groups.value.includes(name)) { ElMessage.warning('分组已存在'); return }
  for (const l of lines.value) if (l.kind === 'group' && l.group === old) l.group = name
  renaming.value = ''
  await persist()
}
async function deleteGroup(name: string) {
  const count = entries.value.filter(e => e.line.group === name).length
  try {
    await ElMessageBox.confirm(
      count > 0
        ? `确定删除分组「${name}」？组内 ${count} 个条目会保留，并归入顶层（仍在文件原位置）。`
        : `确定删除分组「${name}」？`,
      '删除分组',
      { type: 'warning', confirmButtonText: '删除', cancelButtonText: '取消' },
    )
  } catch { return }
  const idx = lines.value.findIndex(l => l.kind === 'group' && l.group === name)
  if (idx >= 0) lines.value.splice(idx, 1)
  await persist()
}

// ── 备份管理 ──
const backupVisible = ref(false)
const backups = ref<HostsBackup[]>([])
const backupLoading = ref(false)
async function openBackups() {
  backupVisible.value = true
  backupLoading.value = true
  try {
    backups.value = await getHostsBackups()
  } catch {
    backups.value = []
    ElMessage.error('读取备份列表失败')
  } finally {
    backupLoading.value = false
  }
}
async function restoreBackup(name: string) {
  try {
    await ElMessageBox.confirm(
      `确定用备份「${name}」还原 hosts？当前内容会先自动备份。`,
      '还原备份',
      { type: 'warning', confirmButtonText: '还原', cancelButtonText: '取消' },
    )
  } catch { return }
  try {
    await restoreHostsBackup(name)
    ElMessage.success('已还原')
    await load()
    backupVisible.value = false
  } catch { /* 失败提示由请求层弹出 */ }
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}
</script>

<template>
  <div class="hosts-page">
    <!-- 头部 -->
    <div class="page-header">
      <div class="title-wrap">
        <h2 class="page-title">Hosts 管理</h2>
        <span class="path-text" :title="hostsPath">{{ hostsPath || '—' }}</span>
      </div>
      <div class="header-actions" v-if="available">
        <el-button :icon="FolderOpened" @click="hostOpenLocation(hostsPath)">打开位置</el-button>
        <el-button v-if="has('hosts:edit')" :icon="Collection" :disabled="!writable" @click="groupDlgVisible = true">分组管理</el-button>
        <el-button :icon="Clock" @click="openBackups">备份管理</el-button>
        <el-button :icon="Refresh" :loading="loading" @click="load">刷新</el-button>
        <el-button
          v-if="has('hosts:edit')"
          type="primary"
          :icon="Plus"
          :disabled="!writable"
          @click="openAdd"
        >添加条目</el-button>
      </div>
    </div>

    <!-- 仅桌面端提示 -->
    <el-alert
      v-if="!available"
      type="info"
      :closable="false"
      show-icon
      title="Hosts 管理是桌面端本机功能"
      description="它操作的是运行程序的这台电脑的 hosts 文件。请通过桌面客户端打开本页面；浏览器直连远程服务器时不可用。"
    />

    <!-- 权限提示 -->
    <el-alert
      v-else-if="available && !writable"
      type="warning"
      :closable="false"
      show-icon
      title="当前程序无 hosts 写入权限"
      description="写入 hosts 需要管理员身份，请以管理员身份重启本程序后再编辑（读取与备份查看不受影响）。"
    />

    <template v-if="available">
      <!-- 工具栏 -->
      <div class="toolbar">
        <el-select v-model="groupFilter" placeholder="全部分组" clearable class="group-select">
          <el-option label="全部分组" value="" />
          <el-option v-for="g in groups" :key="g" :label="g" :value="g" />
          <el-option label="未分组" value="__none__" v-if="entries.some(e => !e.line.group)" />
        </el-select>
        <el-input
          v-model="keyword"
          placeholder="搜索 IP / 域名 / 备注"
          :prefix-icon="Search"
          clearable
          class="search-input"
        />
      </div>

      <!-- 条目表格 -->
      <el-table v-loading="loading" :data="filteredEntries" class="entry-table" :row-key="e => String(e.index)">
        <el-table-column label="启用" width="70" align="center">
          <template #default="{ row }">
            <el-switch
              :model-value="row.line.enabled"
              :disabled="!writable || !has('hosts:edit')"
              :loading="saving"
              @change="toggleEnabled(row as EntryItem)"
            />
          </template>
        </el-table-column>
        <el-table-column label="IP 地址" width="150">
          <template #default="{ row }">
            <span class="ip-text">{{ row.line.ip }}</span>
          </template>
        </el-table-column>
        <el-table-column label="域名" min-width="220">
          <template #default="{ row }">
            <el-tag v-for="d in row.line.domains" :key="d" size="small" class="domain-tag">{{ d }}</el-tag>
          </template>
        </el-table-column>
        <el-table-column label="备注" min-width="140">
          <template #default="{ row }">
            <span class="comment-text">{{ row.line.comment || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="分组" width="130">
          <template #default="{ row }">
            <span class="group-text">{{ row.line.group || '—' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="操作" width="120" align="center">
          <template #default="{ row }">
            <el-button
              v-if="has('hosts:edit')"
              link
              type="primary"
              :icon="EditPen"
              :disabled="!writable"
              @click="openEdit(row as EntryItem)"
            >编辑</el-button>
            <el-button
              v-if="has('hosts:edit')"
              link
              type="danger"
              :icon="Delete"
              :disabled="!writable"
              @click="removeEntry(row as EntryItem)"
            >删除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无条目，点击右上角「添加条目」创建" :image-size="80" />
        </template>
      </el-table>

      <!-- 底部说明 -->
      <div class="footer-bar">
        <span>共 {{ entries.length }} 条 · 已启用 {{ enabledCount }} 条</span>
        <span v-if="otherLineCount > 0">保留 {{ otherLineCount }} 行手工注释/空行（保存时原样写回）</span>
        <span>修改即时生效，每次写入前自动备份（保留最近 20 份）</span>
        <span class="dns-tip">如未立即生效：执行 ipconfig /flushdns 或重启浏览器</span>
      </div>
    </template>

    <!-- 添加/编辑弹窗 -->
    <el-dialog
      v-model="editVisible"
      :title="editMode === 'add' ? '添加条目' : '编辑条目'"
      width="480px"
      destroy-on-close
    >
      <el-form label-width="80px">
        <el-form-item label="IP 地址" required>
          <el-input v-model="form.ip" placeholder="如 127.0.0.1 或 192.168.1.10" />
        </el-form-item>
        <el-form-item label="域名" required>
          <el-input
            v-model="form.domains"
            type="textarea"
            :rows="3"
            placeholder="支持多个域名，用逗号、空格或换行分隔，如：&#10;api.example.com&#10;admin.example.com"
          />
        </el-form-item>
        <el-form-item label="备注">
          <el-input v-model="form.comment" placeholder="可选，如：测试环境" />
        </el-form-item>
        <el-form-item v-if="editMode === 'add'" label="插入到">
          <el-select
            v-model="form.group"
            filterable
            allow-create
            default-first-option
            placeholder="选择已有分组或输入新分组名；「文件末尾」直接追加"
            class="group-select"
          >
            <el-option v-for="g in addGroups" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>
        <el-form-item v-else label="所属分组">
          <el-select v-model="form.group" placeholder="「顶层」表示不属于任何分组" class="group-select">
            <el-option label="顶层（未分组）" value="" />
            <el-option v-for="g in groups" :key="g" :label="g" :value="g" />
          </el-select>
        </el-form-item>
      </el-form>
      <div class="form-tip">分组是 hosts 文件中的注释标记（# ===== 名称 =====），仅用于归类摆放条目，不影响域名解析生效。</div>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitEdit">保存并生效</el-button>
      </template>
    </el-dialog>

    <!-- 分组管理弹窗 -->
    <el-dialog v-model="groupDlgVisible" title="分组管理" width="480px">
      <div class="group-new">
        <el-input
          v-model="newGroupName"
          placeholder="新分组名称，如：测试环境"
          maxlength="50"
          @keyup.enter="createGroup"
        />
        <el-button type="primary" :disabled="!writable" @click="createGroup">新建分组</el-button>
      </div>
      <el-table :data="groupStats" size="small" max-height="320">
        <el-table-column label="分组名称" min-width="170">
          <template #default="{ row }">
            <div v-if="renaming === row.name" class="rename-row">
              <el-input v-model="renameInput" size="small" maxlength="50" @keyup.enter="submitRename" />
              <el-button link type="primary" size="small" @click="submitRename">确定</el-button>
              <el-button link size="small" @click="renaming = ''">取消</el-button>
            </div>
            <span v-else>{{ row.name }}</span>
          </template>
        </el-table-column>
        <el-table-column label="条目数" width="80" align="center">
          <template #default="{ row }">{{ row.count }}</template>
        </el-table-column>
        <el-table-column label="操作" width="120" align="center">
          <template #default="{ row }">
            <el-button
              v-if="has('hosts:edit')"
              link
              type="primary"
              :disabled="!writable"
              @click="startRename(row.name)"
            >重命名</el-button>
            <el-button
              v-if="has('hosts:edit')"
              link
              type="danger"
              :disabled="!writable"
              @click="deleteGroup(row.name)"
            >删除</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无分组，在上方输入名称创建第一个分组" :image-size="70" />
        </template>
      </el-table>
      <div class="form-tip">删除分组不会删除组内条目，它们会保留并归入顶层；分组标记只是注释，不影响域名解析。</div>
    </el-dialog>

    <!-- 备份管理弹窗 -->
    <el-dialog v-model="backupVisible" title="备份管理" width="560px">
      <el-table v-loading="backupLoading" :data="backups" size="small" max-height="380">
        <el-table-column prop="name" label="备份文件" min-width="200" show-overflow-tooltip />
        <el-table-column prop="modified" label="备份时间" width="150" />
        <el-table-column label="大小" width="90">
          <template #default="{ row }">{{ formatSize(row.size) }}</template>
        </el-table-column>
        <el-table-column label="操作" width="90" align="center">
          <template #default="{ row }">
            <el-button
              v-if="has('hosts:edit')"
              link
              type="primary"
              :disabled="!writable"
              @click="restoreBackup(row.name)"
            >还原</el-button>
          </template>
        </el-table-column>
        <template #empty>
          <el-empty description="暂无备份（首次修改 hosts 时自动创建）" :image-size="70" />
        </template>
      </el-table>
    </el-dialog>
  </div>
</template>

<style scoped>
.hosts-page { padding: 16px 20px; }
.page-header { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.title-wrap { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
.page-title { margin: 0; font-size: 18px; font-weight: 600; }
.path-text { font-size: 12px; color: var(--el-text-color-secondary); word-break: break-all; }
.header-actions { display: flex; gap: 8px; flex-shrink: 0; }
.toolbar { display: flex; gap: 10px; margin: 14px 0; }
.group-select { width: 180px; }
.search-input { width: 260px; }
.entry-table { width: 100%; }
.domain-tag { margin-right: 6px; margin-bottom: 2px; }
.ip-text { font-family: Consolas, 'Courier New', monospace; }
.comment-text, .group-text { color: var(--el-text-color-secondary); font-size: 13px; }
.footer-bar {
  display: flex; flex-wrap: wrap; gap: 6px 18px; margin-top: 12px;
  font-size: 12px; color: var(--el-text-color-secondary);
}
.dns-tip { color: var(--el-color-warning); }
.form-tip {
  font-size: 12px;
  color: var(--el-text-color-secondary);
  line-height: 1.6;
  margin-top: 8px;
}
.group-new { display: flex; gap: 8px; margin-bottom: 12px; }
.rename-row { display: flex; gap: 6px; align-items: center; }
</style>
