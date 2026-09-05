<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { ElMessage, type UploadFile } from 'element-plus'
import { UploadFilled } from '@element-plus/icons-vue'
import {
  listPackages,
  uploadPackage,
  activatePackage,
  deactivatePackage,
  deletePackage,
  updatePackage,
  listDesktopPackages,
  uploadDesktopPackage,
  activateDesktopPackage,
  deactivateDesktopPackage,
  deleteDesktopPackage,
  updateDesktopPackage,
  type WebPackageDto,
  type DesktopPackageDto,
} from '@/common/api/webPackage'
import { confirmAndRun } from '@/common/utils/confirm'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import CommonDialog from '@/common/components/CommonDialog.vue'
import { usePermission } from '@/common/composables/usePermission'

type PackageTab = 'web' | 'desktop'

// 两个页签各自独立的查看权限（控制页签显隐与对应 List 接口；都无权时进不来本页）
const { has } = usePermission()
const canViewWeb = computed(() => has('web-package'))
const canViewDesktop = computed(() => has('desktop-package'))

const activeTab = ref<PackageTab>(canViewWeb.value ? 'web' : 'desktop')

// ========== Web 前端版本 ==========
const webLoading = ref(false)
const webList = ref<WebPackageDto[]>([])
const activeWebPackage = computed(() => webList.value.find((p) => p.isActive) ?? null)

const webColumns: DataTableColumn<WebPackageDto>[] = [
  { prop: 'version', label: '版本号', minWidth: 120, sortable: true },
  { prop: 'fileSize', label: '文件大小', width: 120, custom: true, sortable: true },
  { prop: 'description', label: '更新说明 / 服务器路径', minWidth: 240, custom: true, sortable: true },
  { prop: 'isActive', label: '状态', width: 100, custom: true, sortable: true },
  { prop: 'createTime', label: '上传时间', width: 170, type: 'datetime', sortable: true },
]

async function loadWebData() {
  webLoading.value = true
  try {
    webList.value = await listPackages()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    webLoading.value = false
  }
}

// ========== 桌面安装包 ==========
const desktopLoading = ref(false)
const desktopList = ref<DesktopPackageDto[]>([])
const activeDesktopPackage = computed(() => desktopList.value.find((p) => p.isActive) ?? null)

const desktopColumns: DataTableColumn<DesktopPackageDto>[] = [
  { prop: 'version', label: '版本号', minWidth: 120, sortable: true },
  { prop: 'fileSize', label: '文件大小', width: 120, custom: true, sortable: true },
  { prop: 'description', label: '更新说明 / 服务器路径', minWidth: 240, custom: true, sortable: true },
  { prop: 'isActive', label: '状态', width: 100, custom: true, sortable: true },
  { prop: 'createTime', label: '上传时间', width: 170, type: 'datetime', sortable: true },
]

async function loadDesktopData() {
  desktopLoading.value = true
  try {
    desktopList.value = await listDesktopPackages()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    desktopLoading.value = false
  }
}

function loadCurrentTab() {
  if (activeTab.value === 'web') loadWebData()
  else loadDesktopData()
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

/** 描述拆行（服务器路径行后端动态拼接，前端渲染为灰色小字） */
function splitDescLines(description: string | null | undefined): string[] {
  if (!description || !description.trim()) return ['—']
  return description.split('\n')
}

/** 剥离动态拼接的服务器路径行（编辑弹窗只编辑用户说明部分，避免保存回数据库） */
function stripServerPath(description: string | null | undefined): string {
  if (!description) return ''
  return description
    .split('\n')
    .filter((line) => !line.startsWith('服务器路径：'))
    .join('\n')
    .trim()
}

// ========== 上传弹窗（Web / 桌面共用） ==========
const uploadVisible = ref(false)
const uploadLoading = ref(false)
const uploadForm = reactive({
  version: '',
  description: '',
  file: null as File | null,
})

function openUpload() {
  uploadForm.version = ''
  uploadForm.description = ''
  uploadForm.file = null
  uploadVisible.value = true
}

function onFileChange(file: UploadFile) {
  uploadForm.file = file.raw ?? null
  if (!uploadForm.version && file.name) {
    const match = file.name.match(/(\d+\.\d+\.\d+)/)
    if (match) uploadForm.version = match[1]
  }
}

function onFileRemove() {
  uploadForm.file = null
}

const uploadAccept = computed(() => activeTab.value === 'web' ? '.zip' : '.exe')
const uploadFileTip = computed(() =>
  activeTab.value === 'web'
    ? '仅支持 .zip 格式，包含前端 dist 产物（index.html + assets/）'
    : '仅支持 .exe 格式，即 Inno Setup 打包生成的安装程序'
)

async function submitUpload() {
  if (!uploadForm.version.trim()) return ElMessage.warning('请填写版本号')
  if (!uploadForm.file) return ElMessage.warning('请选择文件')

  uploadLoading.value = true
  try {
    if (activeTab.value === 'web') {
      await uploadPackage(uploadForm.version.trim(), uploadForm.file, uploadForm.description.trim() || undefined)
      ElMessage.success('上传成功，已自动激活为新版本')
    } else {
      await uploadDesktopPackage(uploadForm.version.trim(), uploadForm.file, uploadForm.description.trim() || undefined)
      ElMessage.success('上传成功，已自动激活为新版本')
    }
    uploadVisible.value = false
    loadCurrentTab()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    uploadLoading.value = false
  }
}

// ========== 激活 / 停用 / 删除 ==========
async function onActivate(row: WebPackageDto | DesktopPackageDto) {
  if (row.isActive) return
  const isWeb = activeTab.value === 'web'
  const ok = await confirmAndRun(
    `确定激活版本「${row.version}」？桌面客户端下次启动时将下载此版本。`,
    () => isWeb ? activatePackage(row.id) : activateDesktopPackage(row.id),
    { successText: '已激活' },
  )
  if (ok) loadCurrentTab()
}

async function onDeactivate(row: WebPackageDto | DesktopPackageDto) {
  if (!row.isActive) return
  const isWeb = activeTab.value === 'web'
  const ok = await confirmAndRun(
    `确定停用版本「${row.version}」？停用后桌面客户端启动时将不会提示更新。`,
    () => isWeb ? deactivatePackage(row.id) : deactivateDesktopPackage(row.id),
    { successText: '已停用' },
  )
  if (ok) loadCurrentTab()
}

async function onDelete(row: WebPackageDto | DesktopPackageDto) {
  if (row.isActive) return ElMessage.warning('不能删除当前激活版本')
  const isWeb = activeTab.value === 'web'
  const ok = await confirmAndRun(
    `确定删除版本「${row.version}」？此操作不可恢复。`,
    () => isWeb ? deletePackage(row.id) : deleteDesktopPackage(row.id),
    { successText: '已删除' },
  )
  if (ok) loadCurrentTab()
}

// ========== 编辑弹窗（Web / 桌面共用） ==========
const editVisible = ref(false)
const editLoading = ref(false)
const editForm = reactive({
  id: 0,
  version: '',
  description: '',
})

function openEdit(row: WebPackageDto | DesktopPackageDto) {
  editForm.id = row.id
  editForm.version = row.version
  editForm.description = stripServerPath(row.description)
  editVisible.value = true
}

async function submitEdit() {
  if (!editForm.version.trim()) return ElMessage.warning('请填写版本号')
  const isWeb = activeTab.value === 'web'
  editLoading.value = true
  try {
    if (isWeb) {
      await updatePackage(editForm.id, editForm.version.trim(), editForm.description.trim() || undefined)
    } else {
      await updateDesktopPackage(editForm.id, editForm.version.trim(), editForm.description.trim() || undefined)
    }
    ElMessage.success('已更新')
    editVisible.value = false
    loadCurrentTab()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    editLoading.value = false
  }
}


// 进入页面只加载有权限页签的数据，避免无权页签发起 List 请求报 403 弹窗
onMounted(() => {
  if (canViewWeb.value) loadWebData()
  else if (canViewDesktop.value) loadDesktopData()
})
</script>

<template>
  <div class="web-package-page">
    <el-tabs v-model="activeTab" type="border-card" @tab-change="loadCurrentTab">
      <!-- Web 前端版本（页签由 web-package 查看权限控制显隐） -->
      <el-tab-pane v-if="canViewWeb" label="Web 前端版本" name="web">
        <div v-if="activeWebPackage" class="active-version-card">
          <div class="avc-left">
            <el-tag type="success" size="small" effect="dark" round>当前激活</el-tag>
            <span class="avc-version">v{{ activeWebPackage.version }}</span>
            <span class="avc-meta">{{ formatSize(activeWebPackage.fileSize) }} · {{ activeWebPackage.createTime?.replace('T', ' ').slice(0, 16) }}</span>
          </div>
          <div v-if="activeWebPackage.description" class="avc-desc">{{ activeWebPackage.description }}</div>
        </div>

        <CommonDataTable
          show-refresh
          show-column-toggle
          table-key="web-package"
          @load="loadWebData"
          :columns="webColumns"
          :data="webList"
          :loading="webLoading"
          :total="webList.length"
          :show-pagination="false"
          :actions-width="220"
          empty-text="暂无版本包"
        >
          <template #filters>
            <span class="hint">上传前端压缩包（zip 格式），桌面客户端启动时检查并下载激活版本。上传后自动激活为新版本。</span>
          </template>
          <template #toolbar>
            <el-button v-if="$has('web-package:upload')" type="primary" @click="openUpload">上传版本包</el-button>
          </template>
          <template #cell-fileSize="{ row }">
            {{ formatSize((row as WebPackageDto).fileSize) }}
          </template>
          <template #cell-description="{ row }">
            <div class="desc-cell">
              <div
                v-for="(line, i) in splitDescLines((row as WebPackageDto).description)"
                :key="i"
                :class="{ 'desc-path': line.startsWith('服务器路径：') }"
              >{{ line }}</div>
            </div>
          </template>
          <template #cell-isActive="{ row }">
            <el-tag v-if="(row as WebPackageDto).isActive" type="success" size="small">激活中</el-tag>
            <el-tag v-else type="info" size="small">未激活</el-tag>
          </template>
          <template #actions="{ row }">
            <el-button
              v-if="$has('web-package:edit')"
              link
              type="primary"
              size="small"
              @click="openEdit(row as WebPackageDto)"
            >
              编辑
            </el-button>
            <template v-if="$has('web-package:activate')">
              <el-button
                v-if="(row as WebPackageDto).isActive"
                link
                type="warning"
                size="small"
                @click="onDeactivate(row as WebPackageDto)"
              >
                停用
              </el-button>
              <el-button
                v-else
                link
                type="primary"
                size="small"
                @click="onActivate(row as WebPackageDto)"
              >
                激活
              </el-button>
            </template>
            <el-button
              v-if="$has('web-package:delete')"
              link
              type="danger"
              size="small"
              :disabled="(row as WebPackageDto).isActive"
              @click="onDelete(row as WebPackageDto)"
            >
              删除
            </el-button>
          </template>
        </CommonDataTable>
      </el-tab-pane>

      <!-- 桌面安装包（页签由 desktop-package 查看权限控制显隐） -->
      <el-tab-pane v-if="canViewDesktop" label="桌面安装包" name="desktop">
        <div v-if="activeDesktopPackage" class="active-version-card desktop">
          <div class="avc-left">
            <el-tag type="success" size="small" effect="dark" round>当前激活</el-tag>
            <span class="avc-version">v{{ activeDesktopPackage.version }}</span>
            <span class="avc-meta">{{ formatSize(activeDesktopPackage.fileSize) }} · {{ activeDesktopPackage.createTime?.replace('T', ' ').slice(0, 16) }}</span>
          </div>
          <div v-if="activeDesktopPackage.description" class="avc-desc">{{ activeDesktopPackage.description }}</div>
        </div>

        <CommonDataTable
          show-refresh
          show-column-toggle
          table-key="desktop-package"
          @load="loadDesktopData"
          :columns="desktopColumns"
          :data="desktopList"
          :loading="desktopLoading"
          :total="desktopList.length"
          :show-pagination="false"
          :actions-width="220"
          empty-text="暂无安装包"
        >
          <template #filters>
            <span class="hint">上传 Inno Setup 生成的安装程序（exe 格式），桌面客户端启动时检查并下载安装。上传后自动激活为新版本。</span>
          </template>
          <template #toolbar>
            <el-button v-if="$has('desktop-package:upload')" type="primary" @click="openUpload">上传安装包</el-button>
          </template>
          <template #cell-fileSize="{ row }">
            {{ formatSize((row as DesktopPackageDto).fileSize) }}
          </template>
          <template #cell-description="{ row }">
            <div class="desc-cell">
              <div
                v-for="(line, i) in splitDescLines((row as DesktopPackageDto).description)"
                :key="i"
                :class="{ 'desc-path': line.startsWith('服务器路径：') }"
              >{{ line }}</div>
            </div>
          </template>
          <template #cell-isActive="{ row }">
            <el-tag v-if="(row as DesktopPackageDto).isActive" type="success" size="small">激活中</el-tag>
            <el-tag v-else type="info" size="small">未激活</el-tag>
          </template>
          <template #actions="{ row }">
            <el-button
              v-if="$has('desktop-package:edit')"
              link
              type="primary"
              size="small"
              @click="openEdit(row as DesktopPackageDto)"
            >
              编辑
            </el-button>
            <template v-if="$has('desktop-package:activate')">
              <el-button
                v-if="(row as DesktopPackageDto).isActive"
                link
                type="warning"
                size="small"
                @click="onDeactivate(row as DesktopPackageDto)"
              >
                停用
              </el-button>
              <el-button
                v-else
                link
                type="primary"
                size="small"
                @click="onActivate(row as DesktopPackageDto)"
              >
                激活
              </el-button>
            </template>
            <el-button
              v-if="$has('desktop-package:delete')"
              link
              type="danger"
              size="small"
              :disabled="(row as DesktopPackageDto).isActive"
              @click="onDelete(row as DesktopPackageDto)"
            >
              删除
            </el-button>
          </template>
        </CommonDataTable>
      </el-tab-pane>
    </el-tabs>

    <!-- 上传弹窗 -->
    <CommonDialog v-model="uploadVisible" :title="activeTab === 'web' ? '上传版本包' : '上传桌面安装包'" width="560px">
      <el-form :model="uploadForm" label-width="90px">
        <el-form-item label="版本号" required>
          <el-input v-model="uploadForm.version" placeholder="如 1.0.0" maxlength="50" />
        </el-form-item>
        <el-form-item label="更新说明">
          <el-input
            v-model="uploadForm.description"
            type="textarea"
            :rows="3"
            placeholder="可选"
            maxlength="500"
          />
        </el-form-item>
        <el-form-item label="文件" required>
          <el-upload
            :auto-upload="false"
            :limit="1"
            :accept="uploadAccept"
            :on-change="onFileChange"
            :on-remove="onFileRemove"
            drag
          >
            <el-icon class="el-icon--upload"><upload-filled /></el-icon>
            <div class="el-upload__text">将文件拖到此处，或<em>点击选择</em></div>
            <template #tip>
              <div class="upload-tip">{{ uploadFileTip }}</div>
            </template>
          </el-upload>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="uploadVisible = false">取消</el-button>
        <el-button type="primary" :loading="uploadLoading" @click="submitUpload">上传</el-button>
      </template>
    </CommonDialog>

    <!-- 编辑弹窗 -->
    <CommonDialog v-model="editVisible" :title="activeTab === 'web' ? '编辑版本信息' : '编辑安装包信息'" width="480px">
      <el-form :model="editForm" label-width="90px">
        <el-form-item label="版本号" required>
          <el-input v-model="editForm.version" placeholder="如 1.0.0" maxlength="50" />
        </el-form-item>
        <el-form-item label="更新说明">
          <el-input
            v-model="editForm.description"
            type="textarea"
            :rows="3"
            placeholder="可选"
            maxlength="500"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="editVisible = false">取消</el-button>
        <el-button type="primary" :loading="editLoading" @click="submitEdit">保存</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.web-package-page {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.web-package-page :deep(.el-tabs) {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.web-package-page :deep(.el-tabs__content) {
  flex: 1;
  min-height: 0;
  overflow: auto;
}
.web-package-page :deep(.el-tab-pane) {
  height: 100%;
  display: flex;
  flex-direction: column;
}
.hint {
  font-size: 13px;
  color: #6b7280;
}
.upload-tip {
  font-size: 12px;
  color: #909399;
  margin-top: 4px;
}

/* 激活版本摘要卡片 */
.active-version-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 16px;
  background: linear-gradient(135deg, #f0fdf4 0%, #ecfdf5 100%);
  border: 1px solid #bbf7d0;
  border-radius: 10px;
  margin-bottom: 12px;
}
.active-version-card.desktop {
  background: linear-gradient(135deg, #eff6ff 0%, #dbeafe 100%);
  border-color: #bfdbfe;
}
.avc-left {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}
.avc-version {
  font-size: 16px;
  font-weight: 700;
  color: #166534;
}
.active-version-card.desktop .avc-version {
  color: #1e40af;
}
.avc-meta {
  font-size: 12px;
  color: #6b7280;
}
.avc-desc {
  font-size: 13px;
  color: #4b5563;
  white-space: pre-line;
  word-break: break-all;
  min-width: 0;
}

/* 表格描述列：多行显示（说明正文 + 服务器路径行） */
.desc-cell {
  line-height: 1.6;
  min-width: 0;
  word-break: break-all;
  white-space: pre-line;
}

/* 服务器路径行：灰色小字与说明正文区分 */
.desc-cell .desc-path {
  color: #909399;
  font-size: 12px;
}
</style>
