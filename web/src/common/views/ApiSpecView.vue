<template>
  <div class="api-spec-view">
    <div class="spec-header">
      <div class="spec-header-text">
        <h2 class="spec-title">API 文档生成器</h2>
        <p class="spec-sub">选择 C# 项目的 Controller 源文件，生成 OpenAPI / Postman 等格式的 API 数据文件，导入 Apifox / Postman 等工具即用</p>
      </div>
      <!-- 清理 Apifox 与本地扫描无关（只操作远端项目），放顶部始终可见 -->
      <el-button
        type="danger"
        plain
        :disabled="taskRunning"
        @click="openApifoxDelete"
      >清理 Apifox</el-button>
    </div>

    <!-- 工具带：格式 pills + 数据源（原格式大卡收为分段选择器，节省纵向空间） -->
    <div class="card toolbar">
      <div class="tb-row">
        <span class="tb-label">格式</span>
        <div class="pills">
          <button
            v-for="f in formats"
            :key="f.format"
            class="pill"
            :class="{ active: selectedFormat === f.format }"
            :title="f.description"
            @click="selectedFormat = f.format"
          >{{ f.displayName }}</button>
        </div>
      </div>
      <div class="tb-row">
        <span class="tb-label">解决方案</span>
        <el-input v-model="solutionPath" placeholder="解决方案文件（.sln/.slnx）或项目根目录" clearable class="tb-input" @keyup.enter="scanSolution" />
        <el-button :loading="picking" @click="pickSolution">浏览...</el-button>
        <el-button type="primary" :loading="scanning" :disabled="specTaskRunning" @click="scanSolution">扫描</el-button>
        <span class="tb-divider" />
        <el-button v-if="$has('api-spec:debug')" @click="openDebug()">⚡ 接口调试</el-button>
      </div>
    </div>

    <!-- 扫描进度条（后台任务 + 轮询，替代全局加载遮罩：进度实时可见，页面可自由操作） -->
    <div
      v-if="specTask && specTask.kind === 'apispec-scan' && specTask.status !== 'succeeded'"
      class="spec-task-strip"
      :class="{ 'is-failed': specTask.status === 'failed' }"
    >
      <el-progress
        :percentage="specPercentage"
        :status="specTask.status === 'failed' ? 'exception' : undefined"
        :indeterminate="specTask.total === 0"
        striped
        striped-flow
      />
      <div class="spec-task-line">
        <span class="spec-task-phase">{{ specCountText }}</span>
        <span class="spec-task-current">{{ specTask.status === 'failed' ? (specTask.error || '任务失败') : specTask.current }}</span>
        <el-button v-if="specTask.status === 'failed'" link size="small" class="spec-task-dismiss" @click="specTaskId = ''">关闭</el-button>
      </div>
    </div>

    <!-- 统计条：命名空间 / Controller / 接口 / 已选 + 扫描时间 -->
    <div v-if="scanned" class="card stats">
      <div v-for="(s, i) in statsItems" :key="s.label" class="stat" :class="{ accent: i === 3 }">
        <span class="stat-num">{{ s.num }}</span>
        <span class="stat-lbl">{{ s.label }}</span>
      </div>
      <span class="grow" />
      <span class="scan-time">✓ 已扫描 · {{ scannedAt }}</span>
    </div>

    <!-- 主区：左接口树 / 右工作台（设置 + 操作 + 预览一屏完成主流程） -->
    <div v-if="solutionEndpoints.length > 0" class="main">
      <!-- 左：接口树 -->
      <div class="card tree-card" :style="{ width: treeWidth + 'px' }">
        <div class="tree-head">
          <el-input v-model="searchKeyword" placeholder="搜索 Controller、方法、路径或说明..." clearable size="small" />
        </div>
        <div class="tree-ops">
          <el-button link type="primary" size="small" @click="selectAll">全选</el-button>
          <el-button link type="primary" size="small" @click="selectInvert">反选</el-button>
          <el-button link type="primary" size="small" @click="selectFiltered">仅选筛选</el-button>
          <span class="grow" />
          <el-button link size="small" @click="expandAll">全部展开</el-button>
          <el-button link size="small" @click="collapseAll">全部折叠</el-button>
        </div>

        <div class="tree">
          <div v-for="ns in filteredNsGroups" :key="ns.name" class="ns-block">
            <!-- 第一层：命名空间（整组勾选/折叠） -->
            <div class="ns-header" @click="toggleCollapseNs(ns.name)">
              <el-checkbox
                :model-value="isGroupSelected(ns.items)"
                :indeterminate="isGroupIndeterminate(ns.items)"
                @click.stop
                @update:model-value="() => toggleGroup(ns.items)"
              />
              <span class="group-arrow">{{ collapsedNamespaces.has(ns.name) ? '▶' : '▼' }}</span>
              <span class="ns-name">{{ ns.name }}</span>
              <span class="group-count">{{ ns.groups.length }} 个 Controller · {{ ns.items.length }} 个接口</span>
            </div>
            <!-- v-if 惰性渲染：折叠时不建 DOM（几千行全量实例化会冻结页面），展开才渲染 -->
            <div v-if="!collapsedNamespaces.has(ns.name)">
              <div v-for="g in ns.groups" :key="g.name" class="endpoint-group">
                <!-- 第二层：Controller 分组 -->
                <div class="group-header" @click="toggleCollapse(g.name)">
                  <el-checkbox
                    :model-value="isGroupSelected(g.items)"
                    :indeterminate="isGroupIndeterminate(g.items)"
                    @click.stop
                    @update:model-value="() => toggleGroup(g.items)"
                  />
                  <span class="group-arrow">{{ collapsedGroups.has(g.name) ? '▶' : '▼' }}</span>
                  <span class="group-name">{{ g.label }}</span>
                  <span class="group-count">{{ g.items.length }} 个接口</span>
                </div>
                <div v-if="!collapsedGroups.has(g.name)" class="group-body">
                  <!-- 双行布局：第一行勾选+方法+路径（主角，不带服务器地址），悬停 title 可看完整 URL -->
                  <div
                    v-for="ep in g.items"
                    :key="keyOf(ep)"
                    class="endpoint-row"
                    :title="fullUrl(ep.path) + '\n' + (ep.summary || ep.actionName)"
                  >
                    <div class="ep-main">
                      <el-checkbox
                        :model-value="isSelected(ep)"
                        @update:model-value="() => toggleEndpoint(ep)"
                      />
                      <span class="ep-method" :class="'m-' + ep.method.toLowerCase()">{{ ep.method }}</span>
                      <span class="ep-path">{{ ep.path }}</span>
                      <el-button
                        v-if="$has('api-spec:debug')"
                        link
                        type="primary"
                        size="small"
                        class="ep-debug"
                        @click="openDebug(ep)"
                      >调试</el-button>
                    </div>
                    <div class="ep-sub">
                      <span class="ep-summary">{{ ep.summary || ep.actionName }}</span>
                      <el-tag v-if="ep.permission" size="small" type="warning" class="ep-perm">{{ ep.permission }}</el-tag>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <div v-if="filteredNsGroups.length === 0" class="tree-empty">无匹配接口</div>
        </div>
        <div class="tree-foot">
          显示 {{ filteredEndpoints.length }} / {{ solutionEndpoints.length }} 个接口 · 已选 {{ selectedEndpoints.length }} 个
        </div>
      </div>

      <!-- 左栏拖拽分割条：调宽接口树（420~700px），松手持久化 -->
      <div class="tree-resizer" @mousedown="startTreeResize" />

      <!-- 右：工作台 -->
      <div class="workbench">

        <!-- 生成设置 + 主操作 -->
        <div class="card set-card">
          <div class="set-grid">
            <span class="tb-label">文档标题</span>
            <el-input v-model="docTitle" placeholder="默认 ConvenientSystem API" />
            <span class="tb-label">服务器地址</span>
            <el-input v-model="baseUrl" placeholder="默认 http://localhost:8030" />
          </div>
          <div class="cta-row">
            <el-button
              type="primary"
              :disabled="!canGenerate"
              :loading="generating"
              @click="generate"
            >解析并生成</el-button>
            <el-button
              v-if="$has('api-spec:export') && preview"
              @click="download"
            >下载 {{ preview.fileName }}</el-button>
            <el-button v-if="preview" @click="copyContent">复制内容</el-button>
            <el-button
              type="success"
              plain
              :disabled="selectedEndpoints.length === 0 || specTaskRunning"
              :loading="apifoxImporting"
              @click="openApifoxImport"
            >导入 Apifox</el-button>
            <span class="grow" />
            <el-button
              v-if="warnings.length"
              text
              type="warning"
              :icon="WarningFilled"
              @click="warnDialogVisible = true"
            >提示 {{ warnings.length }}</el-button>
          </div>
        </div>

        <!-- 生成进度条（后台任务 + 轮询，替代全局加载遮罩） -->
        <div
          v-if="specTask && specTask.kind === 'apispec-generate' && specTask.status !== 'succeeded'"
          class="spec-task-strip"
          :class="{ 'is-failed': specTask.status === 'failed' }"
        >
          <el-progress
            :percentage="specPercentage"
            :status="specTask.status === 'failed' ? 'exception' : undefined"
            :indeterminate="specTask.total === 0"
            striped
            striped-flow
          />
          <div class="spec-task-line">
            <span class="spec-task-phase">{{ specCountText }}</span>
            <span class="spec-task-current">{{ specTask.status === 'failed' ? (specTask.error || '任务失败') : specTask.current }}</span>
            <el-button v-if="specTask.status === 'failed'" link size="small" class="spec-task-dismiss" @click="specTaskId = ''">关闭</el-button>
          </div>
        </div>

        <!-- 预览：生成内容（深色代码风）/ 已选接口摘要 双 Tab -->
        <div v-if="parsedDoc" class="card preview-card">
          <div class="pv-tabs">
            <span class="pv-tab" :class="{ active: pvTab === 'content' }" @click="pvTab = 'content'">生成内容</span>
            <span class="pv-tab" :class="{ active: pvTab === 'summary' }" @click="pvTab = 'summary'">已选接口摘要（{{ selectedEndpoints.length }}）</span>
            <span class="grow" />
            <span v-if="pvTab === 'content' && preview" class="pv-meta">{{ preview.fileName }}</span>
          </div>

          <template v-if="pvTab === 'content'">
            <div v-if="preview" class="pv-body">
              <!-- 行数在轻量高亮承受范围内：逐行高亮 + 行号；超限回退纯文本 pre（下载/复制仍全量） -->
              <template v-if="pvLines.length <= PV_LINE_LIMIT">
                <div v-for="(l, i) in pvLines" :key="i" class="pv-line">
                  <span class="pv-ln">{{ i + 1 }}</span>
                  <span class="pv-code" v-html="l.html" />
                </div>
              </template>
              <pre v-else class="pv-plain">{{ previewText }}</pre>
            </div>
            <div v-else class="pv-placeholder">点击「解析并生成」查看内容</div>
            <div v-if="preview" class="pv-status">
              <span class="dot ok" />
              <span>已生成 · {{ currentFormatName }} · {{ pvLines.length.toLocaleString() }} 行<span v-if="generatedAt"> · {{ generatedAt }}</span></span>
            </div>
          </template>

          <div v-else class="pv-summary">
            <div class="summary-list">
              <div v-for="g in selectedSummaryGroups" :key="g.name" class="summary-group">
                <div class="summary-group-name">{{ g.label }}（{{ g.items.length }}）</div>
                <div v-for="ep in g.items" :key="keyOf(ep)" class="summary-item" :title="ep.summary">
                  <span class="ep-method" :class="'m-' + ep.method.toLowerCase()">{{ ep.method }}</span>
                  <span class="ep-path">{{ ep.path }}</span>
                </div>
              </div>
              <div v-if="docTypes.length > 0" class="summary-group">
                <div class="summary-group-name">DTO 类型（{{ docTypes.length }} 个）</div>
                <div class="summary-item" v-for="t in docTypes.slice(0, TYPE_LIMIT)" :key="t.name" :title="typeTitle(t)">
                  <span class="ep-method" :class="t.isEnum ? 'm-enum' : 'm-dto'">{{ t.isEnum ? 'enum' : 'dto' }}</span>
                  <span class="ep-path">{{ t.name }}</span>
                </div>
                <div v-if="typeHiddenCount > 0" class="summary-more">…其余 {{ typeHiddenCount }} 个类型略（导出内容不受影响）</div>
              </div>
              <div v-if="summaryHiddenCount > 0" class="summary-more">…其余 {{ summaryHiddenCount }} 个接口略（生成与导出内容不受影响）</div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-else-if="scanned" class="empty-tip">
      未扫描到任何接口（需 public 方法带 [HttpGet] 等 HTTP 特性）
    </div>

    <div v-else class="empty-tip big">
      选择格式并填写数据源后，点击「扫描」解析接口
    </div>

    <el-dialog
      v-model="apifoxDialogVisible"
      :title="apifoxDialogTitle"
      width="560px"
      :close-on-click-modal="false"
      :close-on-press-escape="!apifoxBusy"
      :show-close="!apifoxBusy"
      :append-to-body="true"
      @close="onApifoxDialogClose"
    >
      <!-- 表单态：无任务在跑时展示导入/清理表单 -->
      <template v-if="!apifoxTask">
        <el-alert
          v-if="apifoxAction === 'import'"
          title="Access Token 请在「个人配置」中维护；以下项目和导入选项仅用于本次导入，不会保存。导入会按分组拆批执行，进度实时可见。"
          type="info"
          :closable="false"
          class="apifox-config-alert"
        />
        <el-alert
          v-else
          title="删除的接口进入 Apifox 回收站（30 天内可恢复）；指定目录时连同子目录一起删，删除完成后顺带清理空目录。"
          type="warning"
          :closable="false"
          class="apifox-config-alert"
        />
        <el-form label-width="108px">
          <el-form-item label="项目 ID" required>
            <el-input v-model="apifoxActiveForm.projectId" placeholder="如：4478210" />
          </el-form-item>
          <template v-if="apifoxAction === 'import'">
            <el-form-item label="接口目录 ID">
              <el-input v-model="apifoxImportForm.targetEndpointFolderId" placeholder="可选；不填导入项目根目录" />
            </el-form-item>
            <el-form-item label="分支 ID">
              <el-input v-model="apifoxImportForm.targetBranchId" placeholder="可选；不填导入主分支" />
            </el-form-item>
            <el-form-item label="重复接口处理">
              <el-select v-model="apifoxImportForm.endpointOverwriteBehavior" style="width: 100%">
                <el-option label="智能合并（推荐，保留已有手工配置）" value="AUTO_MERGE" />
                <el-option label="覆盖已有接口与数据模型" value="OVERWRITE_EXISTING" />
                <el-option label="保留已有内容，不更新" value="KEEP_EXISTING" />
                <el-option label="创建新接口，不匹配已有接口" value="CREATE_NEW" />
              </el-select>
            </el-form-item>
          </template>
          <el-form-item v-else label="接口目录 ID">
            <el-input v-model="apifoxDeleteForm.folderId" placeholder="可选；不填删除项目全部接口" />
          </el-form-item>
        </el-form>
      </template>
      <!-- 进度态：后台任务运行中或失败时展示进度条 -->
      <div v-else class="apifox-task">
        <el-progress
          :percentage="taskPercentage"
          :status="apifoxTask.status === 'failed' ? 'exception' : undefined"
          :indeterminate="apifoxTask.total === 0"
          striped
          striped-flow
        />
        <div class="apifox-task-count">{{ taskCountText }}</div>
        <div class="apifox-task-current">
          {{ apifoxTask.status === 'failed' ? (apifoxTask.error || '任务失败') : apifoxTask.current }}
        </div>
        <div v-if="apifoxTask.failed > 0" class="apifox-task-failed">
          已失败 {{ apifoxTask.failed }} 项，任务会继续处理剩余内容
        </div>
        <div v-if="taskRunning" class="apifox-task-bg-tip">
          任务已在后台执行，可关闭此窗口继续其他操作；进度见右上角异步任务列表
        </div>
      </div>
      <template #footer>
        <template v-if="!apifoxTask">
          <el-button :disabled="apifoxBusy" @click="apifoxDialogVisible = false">取消</el-button>
          <el-button
            v-if="apifoxAction === 'import'"
            type="primary"
            :loading="apifoxImporting"
            @click="importToApifox"
          >开始导入</el-button>
          <el-button
            v-else
            type="danger"
            :loading="apifoxDeleting"
            @click="deleteFromApifox"
          >开始删除</el-button>
        </template>
        <el-button v-else-if="taskRunning" @click="apifoxDialogVisible = false">转入后台运行</el-button>
        <el-button v-else-if="apifoxTask.status === 'failed'" type="primary" @click="apifoxDialogVisible = false">关闭</el-button>
      </template>
    </el-dialog>

    <!-- 生成提示弹窗：同构「类型未命中」警告归并为标签云，其余按行展示 -->
    <el-dialog v-model="warnDialogVisible" title="生成提示" width="620px" :append-to-body="true">
      <div class="warn-dialog-body">
        <template v-if="groupedTypeWarnings.length">
          <div class="warn-group-title">以下 {{ groupedTypeWarnings.length }} 个类型未在扫描范围内找到，已按 object 处理：</div>
          <div class="warn-type-cloud">
            <el-tag v-for="t in groupedTypeWarnings" :key="t" size="small" type="warning" effect="plain">{{ t }}</el-tag>
          </div>
        </template>
        <div v-for="(w, i) in otherWarnings" :key="i" class="warn-line">{{ w }}</div>
      </div>
      <template #footer>
        <el-button type="primary" @click="warnDialogVisible = false">关 闭</el-button>
      </template>
    </el-dialog>

    <!-- 接口调试抽屉：自由调试（init 为 null）或从接口行带入方法+地址+参数 -->
    <el-drawer v-model="debugVisible" title="接口调试" size="80%" :close-on-click-modal="true">
      <ApiDebugView :init="debugInit" />
    </el-drawer>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref, shallowRef, watch } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { WarningFilled } from '@element-plus/icons-vue'
import {
  getApiSpecFormats, pickApiSpecSolution,
  startApiSpecScan, startApiSpecGenerate, reExportApiSpec,
  startApifoxImport, startApifoxDelete, cancelApifoxRunningTask,
  type ApiSpecFormatDto, type ApiSpecSolutionEndpointDto, type ApiSpecDocumentDto, type ApiSpecExportDto, type ApiSpecTypeDto,
  type ApifoxImportRequest, type ApifoxImportResultDto, type ApifoxDeleteResultDto,
  type ApiSpecScanTaskResult, type ApiSpecGenerateTaskResult,
} from '@/common/api/apiSpec'
import { ApiError } from '@/api/request'
import type { AsyncTaskDto } from '@/common/api/asyncTask'
import { useAsyncTaskStore } from '@/common/stores/asyncTask'
import ApiDebugView from '@/common/views/ApiDebugView.vue'

const SOLUTION_KEY = 'api-spec:solution'
const BASE_URL_KEY = 'api-spec:baseUrl'
/** 接口树列宽持久化键与范围（px）：拖拽分割条调宽，路径较长时拉宽查看 */
const TREE_WIDTH_KEY = 'api-spec:tree-width'
const TREE_WIDTH_MIN = 420
const TREE_WIDTH_MAX = 1000

const formats = ref<ApiSpecFormatDto[]>([])
const selectedFormat = ref('')
const solutionPath = ref(localStorage.getItem(SOLUTION_KEY) || '')
const picking = ref(false)
const scanning = ref(false)
const scanned = ref(false)
// 大数据集用 shallowRef：接口清单/文档/导出内容可达数 MB，只整体替换不逐属性改，深代理白耗性能
const solutionEndpoints = shallowRef<ApiSpecSolutionEndpointDto[]>([])
const selectedKeys = shallowRef<Set<string>>(new Set())
const collapsedGroups = ref<Set<string>>(new Set())
/** 命名空间层折叠状态（两级分组第一层）；Controller 层默认折叠、命名空间层默认展开 */
const collapsedNamespaces = ref<Set<string>>(new Set())
/** 接口树列宽（px）：拖拽分割条调整，localStorage 持久化；非法存量值回退默认 420 */
const savedTreeWidth = Number(localStorage.getItem(TREE_WIDTH_KEY))
const treeWidth = ref(
  Number.isFinite(savedTreeWidth) && savedTreeWidth >= TREE_WIDTH_MIN && savedTreeWidth <= TREE_WIDTH_MAX
    ? savedTreeWidth
    : 420,
)
const searchKeyword = ref('')
const docTitle = ref('')
/** 服务器地址默认值；旧默认 http://localhost 视为未自定义，自动升级到新默认 */
const DEFAULT_BASE_URL = 'http://localhost:8030'
const storedBaseUrl = localStorage.getItem(BASE_URL_KEY)
const baseUrl = ref(!storedBaseUrl || storedBaseUrl === 'http://localhost' ? DEFAULT_BASE_URL : storedBaseUrl)
const generating = ref(false)
const parsedDoc = shallowRef<ApiSpecDocumentDto | null>(null)
const preview = shallowRef<ApiSpecExportDto | null>(null)
const warnings = ref<string[]>([])
/** 生成提示弹窗：解析生成后自动弹出一次，平时点工具栏「提示」入口打开 */
const warnDialogVisible = ref(false)
/** 「类型 X 未在扫描范围内找到，已按 object 处理」同构警告归并展示用 */
const TYPE_WARN_RE = /^类型\s+(.+?)\s+未在扫描范围内找到[，,]\s*已按\s*object\s*处理$/
const groupedTypeWarnings = computed(() =>
  warnings.value.map(w => TYPE_WARN_RE.exec(w)?.[1]).filter((t): t is string => !!t),
)
const otherWarnings = computed(() => warnings.value.filter(w => !TYPE_WARN_RE.test(w)))
/** 异步任务中心：任务进度由 store 统一跟踪（轮询 + SignalR 推送），页面只持有 taskId 做局部展示 */
const asyncTaskStore = useAsyncTaskStore()
/** 当前页内跟踪的扫描/生成任务 ID；空串表示无任务在跑（页内进度条隐藏） */
const specTaskId = ref('')
const specTask = computed(() => (specTaskId.value ? asyncTaskStore.getTask(specTaskId.value) ?? null : null))
const specTaskRunning = computed(() => specTask.value?.status === 'running')
/** 最近一次成功生成任务：taskId + 勾选指纹，供换格式 ReExport 与导入 Apifox 复用解析结果（不重新解析源码） */
let lastGenerateTaskId = ''
let lastGenerateKeys = ''

const apifoxDialogVisible = ref(false)
/** 当前弹窗动作：import（导入）/ delete（批量清理）；两若共用一个弹窗与进度展示 */
const apifoxAction = ref<'import' | 'delete'>('import')
const apifoxImporting = ref(false)
const apifoxDeleting = ref(false)
const apifoxImportForm = ref({
  projectId: '',
  targetEndpointFolderId: '',
  targetBranchId: '',
  // 默认覆盖：以生成内容为准全量更新，重复导入不残留旧定义
  endpointOverwriteBehavior: 'OVERWRITE_EXISTING',
})
const apifoxDeleteForm = ref({ projectId: '', folderId: '' })
/** 当前弹窗跟踪的 Apifox 任务 ID；空串表示表单态（无任务在跑） */
const apifoxTaskId = ref('')
const apifoxTask = computed(() => (apifoxTaskId.value ? asyncTaskStore.getTask(apifoxTaskId.value) ?? null : null))
/** 项目 ID 输入框直接绑定到当前动作的表单（computed 返回表单对象本身，.projectId 可写回原对象） */
const apifoxActiveForm = computed(() => (apifoxAction.value === 'import' ? apifoxImportForm.value : apifoxDeleteForm.value))
const taskRunning = computed(() => apifoxTask.value?.status === 'running')
/** 弹窗锁定：仅启动请求进行中不允许关闭（关窗会丢 taskId）；任务提交后进度由 store 轮询+推送双通道跟踪，弹窗随时可关（转入后台，重开入口自动回到进度态） */
const apifoxBusy = computed(() => apifoxImporting.value || apifoxDeleting.value)
const apifoxDialogTitle = computed(() => {
  // 任务态按任务 kind 判定，表单态按当前动作
  const importing = apifoxTask.value ? apifoxTask.value.kind === 'apifox-import' : apifoxAction.value === 'import'
  return importing ? '导入到 Apifox' : '清理 Apifox 接口'
})
const taskPercentage = computed(() => {
  const task = apifoxTask.value
  if (!task || task.total <= 0) return 0
  return Math.min(100, Math.round((task.completed / task.total) * 100))
})
const taskCountText = computed(() => {
  const task = apifoxTask.value
  if (!task) return ''
  // 列表拉取阶段总数未知，直接展示步骤文案
  if (task.total <= 0) return task.current
  const unit = task.kind === 'apifox-import' ? '批' : '个'
  return `${task.completed} / ${task.total} ${unit}`
})

/** 扫描/生成任务进度百分比；阶段未开始（总数未知）时 0，模板配合显示不确定进度 */
const specPercentage = computed(() => {
  const task = specTask.value
  if (!task || task.total <= 0) return 0
  return Math.min(100, Math.round((task.completed / task.total) * 100))
})

const SPEC_PHASE_TEXTS: Record<string, string> = {
  indexing: '建立类型索引',
  scanning: '扫描接口',
  parsing: '解析 Controller',
  exporting: '生成导出内容',
}

/** 进度条左侧的“阶段 + 计数”文案 */
const specCountText = computed(() => {
  const task = specTask.value
  if (!task) return ''
  const phaseText = SPEC_PHASE_TEXTS[task.phase] || (task.kind === 'apispec-scan' ? '扫描' : '生成')
  return task.total <= 0 ? `${phaseText}...` : `${phaseText} ${task.completed} / ${task.total} 个文件`
})

/** 接口调试抽屉：debugInit 非空表示从某个接口行带入（方法+完整地址+参数） */
const debugVisible = ref(false)
const debugInit = ref<{
  method: string
  url: string
  headers?: Record<string, string>
  body?: string
  params?: any[]
  types?: Record<string, any>
} | null>(null)

function openDebug(ep?: ApiSpecSolutionEndpointDto) {
  if (!ep) {
    debugInit.value = null
    debugVisible.value = true
    return
  }
  
  const targetUrl = fullUrl(ep.path)
  
  // 从已解析文档中查找接口参数（生成任务完成后才有数据）
  let params: any[] = []
  let types: Record<string, any> = {}
  if (parsedDoc.value) {
    const matched = parsedDoc.value.endpoints.find(
      e => e.path === ep.path && e.method.toUpperCase() === ep.method.toUpperCase()
    )
    if (matched) {
      params = matched.params || []
      types = parsedDoc.value.types || {}
    }
  }
  
  debugInit.value = {
    method: ep.method,
    url: targetUrl,
    params,
    types,
  }
  debugVisible.value = true
}

const selectedEndpoints = computed(() => solutionEndpoints.value.filter(e => selectedKeys.value.has(keyOf(e))))
const canGenerate = computed(() => selectedFormat.value && selectedEndpoints.value.length > 0 && !generating.value && !scanning.value && !specTaskRunning.value)
const currentFormatName = computed(() => formats.value.find(f => f.format === selectedFormat.value)?.displayName || selectedFormat.value)

/** 统计条：命名空间 / Controller / 接口 / 已选（已选高亮强调） */
const statsItems = computed(() => [
  { label: '命名空间', num: new Set(solutionEndpoints.value.map(e => e.namespace || '未分组')).size },
  { label: 'Controller', num: new Set(solutionEndpoints.value.map(e => e.group)).size },
  { label: '接口', num: solutionEndpoints.value.length },
  { label: '已选', num: selectedEndpoints.value.length },
])
/** 最近一次扫描/生成成功时间（统计条与预览状态条展示） */
const scannedAt = ref('')
const generatedAt = ref('')
/** 预览卡 Tab：生成内容 / 已选接口摘要 */
const pvTab = ref<'content' | 'summary'>('content')

const filteredEndpoints = computed(() => {
  const kw = searchKeyword.value.trim().toLowerCase()
  if (!kw) return solutionEndpoints.value
  return solutionEndpoints.value.filter(e =>
    e.group.toLowerCase().includes(kw) ||
    (e.namespace || '').toLowerCase().includes(kw) ||
    e.method.toLowerCase().includes(kw) ||
    e.path.toLowerCase().includes(kw) ||
    e.actionName.toLowerCase().includes(kw) ||
    (e.summary || '').toLowerCase().includes(kw)
  )
})

/** Controller 分组：name 为折叠键（带命名空间前缀，避免不同命名空间同名 Controller 冲突），label 为展示名 */
interface EndpointGroup {
  name: string
  label: string
  items: ApiSpecSolutionEndpointDto[]
}

/** 接口列表两级分组：命名空间 → Controller。后端已按 命名空间→Controller→路径 排序，Map 按插入序天然保序 */
const filteredNsGroups = computed(() => {
  const nsMap = new Map<string, Map<string, EndpointGroup>>()
  for (const ep of filteredEndpoints.value) {
    const nsName = ep.namespace || '未分组'
    let groups = nsMap.get(nsName)
    if (!groups) {
      groups = new Map()
      nsMap.set(nsName, groups)
    }
    let group = groups.get(ep.group)
    if (!group) {
      group = { name: `${nsName}/${ep.group}`, label: ep.group, items: [] }
      groups.set(ep.group, group)
    }
    group.items.push(ep)
  }
  return [...nsMap.entries()].map(([name, groups]) => ({
    name,
    groups: [...groups.values()],
    items: [...groups.values()].flatMap(g => g.items),
  }))
})

/** 摘要分组（单层）：label 带命名空间前缀，避免不同命名空间同名 Controller 混淆 */
const selectedGroupedEndpoints = computed<EndpointGroup[]>(() => {
  const map = new Map<string, EndpointGroup>()
  for (const ep of selectedEndpoints.value) {
    let group = map.get(ep.group)
    if (!group) {
      const ns = ep.namespace || ''
      group = { name: ep.group, label: ns ? `${ns} / ${ep.group}` : ep.group, items: [] }
      map.set(ep.group, group)
    }
    group.items.push(ep)
  }
  return [...map.values()]
})

// ==================== 渲染上限（几千条全量渲染会冻结页面，截断展示；下载/复制/导出仍是全量） ====================

/** 预览文本渲染上限：几 MB 文本一次性进 <pre> 单次排版耗时几十秒 */
const PREVIEW_LIMIT = 100_000
const previewText = computed(() => {
  const content = preview.value?.content || ''
  if (content.length <= PREVIEW_LIMIT) return content
  return content.slice(0, PREVIEW_LIMIT) + `\n…（内容过长已截断，全量 ${content.length.toLocaleString()} 字符，请下载或复制查看）`
})

/* ---- 生成内容轻量语法高亮（选轻量 pre 高亮而非 Monaco）：HTML 整段转义后仅 span 包裹，无注入风险 ---- */

/** 逐行 token 高亮的行数上限：超限行数回退纯文本 pre，避免一次性渲染上万 span 卡页面 */
const PV_LINE_LIMIT = 2000
const JSON_TOKEN_RE = /("(?:[^"\\]|\\.)*")(\s*:)?|\b(true|false|null)\b|-?\b\d+(?:\.\d+)?\b/g

function escapeHtml(s: string): string {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

/** 疑似 JSON（首非空白字符为 { 或 [）时逐行高亮，其余格式原样转义展示 */
function highlightJsonLine(line: string): string {
  return line.replace(JSON_TOKEN_RE, (m, str: string | undefined, colon: string | undefined) => {
    if (str !== undefined) {
      return colon
        ? `<span class="tk-k">${str}</span><span class="tk-p">${colon}</span>`
        : `<span class="tk-s">${str}</span>`
    }
    if (/^(?:true|false|null)$/.test(m)) return `<span class="tk-b">${m}</span>`
    return `<span class="tk-n">${m}</span>`
  })
}

/** 预览逐行 HTML（已转义）：行数供状态条展示与超限回退判断 */
const pvLines = computed(() => {
  const content = previewText.value
  if (!content) return [] as { html: string }[]
  const jsonLike = /^[\s]*[[{]/.test(content)
  return content.split('\n').map(raw => ({
    html: jsonLike ? highlightJsonLine(escapeHtml(raw)) : escapeHtml(raw),
  }))
})

/** 已选接口摘要渲染上限 */
const SUMMARY_LIMIT = 200
const selectedSummaryGroups = computed<EndpointGroup[]>(() => {
  let remain = SUMMARY_LIMIT
  const groups: EndpointGroup[] = []
  for (const g of selectedGroupedEndpoints.value) {
    if (remain <= 0) break
    groups.push(g.items.length <= remain ? g : { ...g, items: g.items.slice(0, remain) })
    remain -= Math.min(remain, g.items.length)
  }
  return groups
})
const summaryHiddenCount = computed(() => Math.max(0, selectedEndpoints.value.length - SUMMARY_LIMIT))

/** DTO 类型列表渲染上限 */
const TYPE_LIMIT = 100
const docTypes = computed(() => (parsedDoc.value ? Object.values(parsedDoc.value.types) : []))
const typeHiddenCount = computed(() => Math.max(0, docTypes.value.length - TYPE_LIMIT))

function keyOf(ep: ApiSpecSolutionEndpointDto): string {
  return ep.selectionKey
}

function isSelected(ep: ApiSpecSolutionEndpointDto): boolean {
  return selectedKeys.value.has(keyOf(ep))
}

function isGroupSelected(items: ApiSpecSolutionEndpointDto[]): boolean {
  return items.length > 0 && items.every(isSelected)
}

function isGroupIndeterminate(items: ApiSpecSolutionEndpointDto[]): boolean {
  const selectedCount = items.filter(isSelected).length
  return selectedCount > 0 && selectedCount < items.length
}

function toggleEndpoint(ep: ApiSpecSolutionEndpointDto) {
  const next = new Set(selectedKeys.value)
  const k = keyOf(ep)
  if (next.has(k)) next.delete(k)
  else next.add(k)
  selectedKeys.value = next
}

function toggleGroup(items: ApiSpecSolutionEndpointDto[]) {
  const allSelected = isGroupSelected(items)
  const next = new Set(selectedKeys.value)
  for (const ep of items) {
    const k = keyOf(ep)
    if (allSelected) next.delete(k)
    else next.add(k)
  }
  selectedKeys.value = next
}

function toggleCollapse(name: string) {
  const next = new Set(collapsedGroups.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  collapsedGroups.value = next
}

function toggleCollapseNs(name: string) {
  const next = new Set(collapsedNamespaces.value)
  if (next.has(name)) next.delete(name)
  else next.add(name)
  collapsedNamespaces.value = next
}

function selectAll() {
  selectedKeys.value = new Set(solutionEndpoints.value.map(keyOf))
}

function selectInvert() {
  const selected = selectedKeys.value
  selectedKeys.value = new Set(solutionEndpoints.value.filter(ep => !selected.has(keyOf(ep))).map(keyOf))
}

function selectFiltered() {
  selectedKeys.value = new Set(filteredEndpoints.value.map(keyOf))
}

/** 全部展开 / 全部折叠（键与列表分组一致：组键带命名空间前缀） */
function expandAll() {
  collapsedGroups.value = new Set()
  collapsedNamespaces.value = new Set()
}

function collapseAll() {
  collapsedGroups.value = new Set(solutionEndpoints.value.map(e => `${e.namespace || '未分组'}/${e.group}`))
  collapsedNamespaces.value = new Set(solutionEndpoints.value.map(e => e.namespace || '未分组'))
}

function openApifoxImport() {
  if (selectedEndpoints.value.length === 0) {
    ElMessage.warning('请至少勾选一个接口')
    return
  }
  // 已有同类型任务在跑（上次转入后台）：直接回到进度态，不清 taskId 不重置表单
  const runningImport = asyncTaskStore.list.find(t => t.kind === 'apifox-import' && t.status === 'running')
  if (runningImport) {
    apifoxAction.value = 'import'
    apifoxTaskId.value = runningImport.taskId
    apifoxDialogVisible.value = true
    return
  }
  apifoxAction.value = 'import'
  apifoxTaskId.value = ''
  apifoxImportForm.value = {
    projectId: '',
    targetEndpointFolderId: '',
    targetBranchId: '',
    endpointOverwriteBehavior: 'OVERWRITE_EXISTING',
  }
  apifoxDialogVisible.value = true
}

function openApifoxDelete() {
  // 已有同类型任务在跑（上次转入后台）：直接回到进度态，不清 taskId 不重置表单
  const runningDelete = asyncTaskStore.list.find(t => t.kind === 'apifox-delete' && t.status === 'running')
  if (runningDelete) {
    apifoxAction.value = 'delete'
    apifoxTaskId.value = runningDelete.taskId
    apifoxDialogVisible.value = true
    return
  }
  apifoxAction.value = 'delete'
  apifoxTaskId.value = ''
  apifoxDeleteForm.value = { projectId: '', folderId: '' }
  apifoxDialogVisible.value = true
}

function onApifoxDialogClose() {
  // 运行中关闭 = 转入后台：保留 taskId（重开入口自动回到进度态）；终态关闭才清任务展示
  if (!taskRunning.value) apifoxTaskId.value = ''
}

/** 等待 store 跟踪的任务到终态（succeeded/failed）：进度更新由 store 轮询 + SignalR 推送双通道负责，页面只等结果 */
function waitTask(taskId: string): Promise<AsyncTaskDto> {
  const current = asyncTaskStore.getTask(taskId)
  if (current && current.status !== 'running') return Promise.resolve(current)
  return new Promise(resolve => {
    const stop = watch(() => asyncTaskStore.getTask(taskId), task => {
      if (task && task.status !== 'running') {
        stop()
        resolve(task)
      }
    })
  })
}

function parseOptionalApifoxId(value: string, label: string): number | undefined | null {
  const text = value.trim()
  if (!text) return undefined
  if (!/^\d+$/.test(text) || !Number.isSafeInteger(Number(text)) || Number(text) <= 0) {
    ElMessage.warning(`${label}必须是正整数`)
    return null
  }
  return Number(text)
}

function buildApifoxImportRequest(content: string): ApifoxImportRequest | null {
  const projectId = apifoxImportForm.value.projectId.trim()
  if (!projectId) {
    ElMessage.warning('请填写 Apifox 项目 ID')
    return null
  }
  const endpointFolderId = parseOptionalApifoxId(apifoxImportForm.value.targetEndpointFolderId, '接口目录 ID')
  const branchId = parseOptionalApifoxId(apifoxImportForm.value.targetBranchId, '分支 ID')
  if (endpointFolderId === null || branchId === null) return null
  return {
    content,
    projectId,
    targetEndpointFolderId: endpointFolderId ?? null,
    targetBranchId: branchId ?? null,
    endpointOverwriteBehavior: apifoxImportForm.value.endpointOverwriteBehavior,
  }
}

/** 导入 Apifox 用的 OpenAPI 内容：勾选与上次生成一致时复用其解析结果秒级导出，否则现场跑一次生成任务 */
async function buildOpenApiForImport(): Promise<ApiSpecExportDto> {
  if (lastGenerateTaskId && lastGenerateKeys === selectionFingerprint()) {
    try {
      return await reExportApiSpec({
        taskId: lastGenerateTaskId,
        format: 'openapi3-json',
        title: docTitle.value.trim() || undefined,
        baseUrl: baseUrl.value.trim() || undefined,
      })
    } catch {
      /* 生成任务已过期（服务端仅保留 30 分钟）：回退现场生成 */
    }
  }
  const task = await asyncTaskStore.submit(() => startApiSpecGenerate({
    rootDir: solutionRootDir(),
    files: selectedControllerFiles().join(','),
    format: 'openapi3-json',
    title: docTitle.value.trim() || undefined,
    baseUrl: baseUrl.value.trim() || undefined,
    solutionPath: solutionPath.value.trim() || undefined,
    selectionKeys: selectedSelectionKeys(),
  }))
  specTaskId.value = task.taskId
  const final = await waitTask(task.taskId)
  // 终态后显式拉取完整结果（轮询快照不带 result）
  const full = await asyncTaskStore.fetchResult(task.taskId, final.kind)
  const result = full?.result as ApiSpecGenerateTaskResult | null
  if (final.status !== 'succeeded' || !result?.export) throw new Error(final.error || '生成 OpenAPI 内容失败')
  lastGenerateTaskId = task.taskId
  lastGenerateKeys = selectionFingerprint()
  specTaskId.value = ''
  return result.export
}

/** 并发拒绝（409 task-running：同类型旧任务仍在后台执行）时询问是否强制终止：确认则调取消端点并返回 true，调用方重试一次 */
async function offerForceCancel(err: unknown, kind: 'apifox-import' | 'apifox-delete'): Promise<boolean> {
  const body = (err as ApiError | undefined)?.responseBody
  if (!body || body.code !== 'task-running') return false
  const confirmed = await ElMessageBox.confirm(
    '同类型的上一个任务仍在后台执行（可能因上次请求超时，状态未同步）。强制终止旧任务并重新发起？',
    '强制终止确认',
    { type: 'warning', confirmButtonText: '强制终止并重试', cancelButtonText: '取消' },
  ).then(() => true).catch(() => false)
  if (!confirmed) return false
  try {
    await cancelApifoxRunningTask(kind)
    ElMessage.success('已强制终止旧任务，正在重新发起')
    return true
  } catch {
    return false /* 终止失败由 request.ts 弹错 */
  }
}

async function importToApifox(forced = false) {
  if (selectedEndpoints.value.length === 0) return ElMessage.warning('请至少勾选一个接口')
  const importRequest = buildApifoxImportRequest('')
  if (!importRequest) return

  apifoxImporting.value = true
  try {
    const openApi = await buildOpenApiForImport()
    importRequest.content = openApi.content
    const task = await asyncTaskStore.submit(() => startApifoxImport(importRequest))
    apifoxTaskId.value = task.taskId
    const final = await waitTask(task.taskId)
    if (final.status === 'failed') return // 失败态留在弹窗展示错误
    // 终态后显式拉取完整结果（轮询快照不带 result）
    const full = await asyncTaskStore.fetchResult(task.taskId, final.kind)
    // 弹窗仍展示本任务（未转入后台）才自动关窗；已转后台/重开其他表单时不打扰
    if (apifoxTaskId.value === task.taskId) {
      apifoxDialogVisible.value = false
      apifoxTaskId.value = ''
    }
    const result = full?.result as ApifoxImportResultDto | null
    if (!result) return ElMessage.error('导入结果获取失败，请到 Apifox 核对实际导入情况')
    const endpointChanges = result.endpointCreated + result.endpointUpdated
    const schemaChanges = result.schemaCreated + result.schemaUpdated
    if (result.errors.length > 0 || result.endpointFailed > 0 || result.schemaFailed > 0) {
      ElMessage.warning(`Apifox 已完成导入：接口新增/更新 ${endpointChanges}，模型新增/更新 ${schemaChanges}；${result.errors[0] || '部分资源导入失败'}`)
    } else {
      ElMessage.success(`Apifox 导入成功：接口新增/更新 ${endpointChanges}，模型新增/更新 ${schemaChanges}`)
    }
  } catch (err) {
    /* 启动失败由 request.ts 弹错（表单保留可重试）；OpenAPI 现场生成失败展示在页面进度条，被弹窗遮挡时再提示一次 */
    if (specTask.value?.status === 'failed') ElMessage.error(specTask.value.error || '生成 OpenAPI 内容失败')
    // 并发拒绝：询问强制终止上一个卡住的旧任务后重试一次（forced 防重复弹窗）
    if (!forced && await offerForceCancel(err, 'apifox-import')) return await importToApifox(true)
  } finally {
    apifoxImporting.value = false
  }
}

async function deleteFromApifox(forced = false) {
  const projectId = apifoxDeleteForm.value.projectId.trim()
  if (!projectId) return ElMessage.warning('请填写 Apifox 项目 ID')
  const folderId = parseOptionalApifoxId(apifoxDeleteForm.value.folderId, '接口目录 ID')
  if (folderId === null) return

  const tip = folderId
    ? '将删除该目录（含子目录）下的全部接口，删除后进入 Apifox 回收站（30 天内可恢复）。确定继续？'
    : '将删除该项目下的全部接口，删除后进入 Apifox 回收站（30 天内可恢复）。确定继续？'
  const confirmed = await ElMessageBox.confirm(tip, '批量删除确认', {
    type: 'warning',
    confirmButtonText: '删除',
    cancelButtonText: '取消',
  }).then(() => true).catch(() => false)
  if (!confirmed) return

  apifoxDeleting.value = true
  try {
    const task = await asyncTaskStore.submit(() => startApifoxDelete({ projectId, folderId: folderId ?? null }))
    apifoxTaskId.value = task.taskId
    const final = await waitTask(task.taskId)
    if (final.status === 'failed') return // 失败态留在弹窗展示错误
    // 终态后显式拉取完整结果（轮询快照不带 result）
    const full = await asyncTaskStore.fetchResult(task.taskId, final.kind)
    // 弹窗仍展示本任务（未转入后台）才自动关窗；已转后台/重开其他表单时不打扰
    if (apifoxTaskId.value === task.taskId) {
      apifoxDialogVisible.value = false
      apifoxTaskId.value = ''
    }
    const result = full?.result as ApifoxDeleteResultDto | null
    if (!result) return ElMessage.error('删除结果获取失败，请到 Apifox 核对实际删除情况')
    const folderTip = result.foldersRemoved > 0 ? `，清理空目录 ${result.foldersRemoved} 个` : ''
    if (result.failed > 0) {
      ElMessage.warning(`已删除 ${result.deleted} 个接口${folderTip}；${result.failed} 个失败：${result.errors[0] || '详见错误列表'}`)
    } else {
      ElMessage.success(`已删除 ${result.deleted} 个接口${folderTip}`)
    }
  } catch (err) {
    /* 启动失败由 request.ts 弹错；任务失败态（含查询失联由 store 标记）留在弹窗展示 */
    if (!forced && await offerForceCancel(err, 'apifox-delete')) return await deleteFromApifox(true)
  } finally {
    apifoxDeleting.value = false
  }
}

onMounted(async () => {
  try {
    formats.value = await getApiSpecFormats()
    if (formats.value.length > 0) selectedFormat.value = formats.value[0].format
  } catch {
    /* 错误已由 request.ts 弹出提示 */
  }
})

async function pickSolution() {
  picking.value = true
  try {
    const res = await pickApiSpecSolution()
    if (res?.path) {
      solutionPath.value = res.path
      ElMessage.success('已选择解决方案，点击「扫描」解析接口')
    }
  } catch {
    ElMessage.warning('当前环境不支持选择文件，请手动输入路径')
  } finally {
    picking.value = false
  }
}

function solutionRootDir(): string {
  const p = solutionPath.value.trim().replace(/^"|"$/g, '')
  if (!/\.slnx?$/i.test(p)) return p
  const idx = Math.max(p.lastIndexOf('\\'), p.lastIndexOf('/'))
  return idx > 0 ? p.slice(0, idx) : p
}

function selectedControllerFiles(): string[] {
  return [...new Set(selectedEndpoints.value.map(e => e.file))]
}

function selectedSelectionKeys(): string[] {
  return selectedEndpoints.value.map(keyOf)
}

/** 当前勾选指纹：与上次生成时的勾选一致才允许 ReExport 复用解析结果 */
function selectionFingerprint(): string {
  return selectedSelectionKeys().slice().sort().join('\n')
}

function fullUrl(path: string): string {
  return baseUrl.value.trim().replace(/\/+$/, '') + path
}

/** 拖拽左栏分割条调宽（范围内 clamp，松手持久化）；拖拽期间全窗口列光标 + 禁用文本选中 */
function startTreeResize(e: MouseEvent) {
  e.preventDefault()
  const startX = e.clientX
  const startWidth = treeWidth.value
  const onMove = (ev: MouseEvent) => {
    treeWidth.value = Math.min(TREE_WIDTH_MAX, Math.max(TREE_WIDTH_MIN, startWidth + ev.clientX - startX))
  }
  const onUp = () => {
    window.removeEventListener('mousemove', onMove)
    window.removeEventListener('mouseup', onUp)
    document.body.classList.remove('tree-resizing')
    try {
      localStorage.setItem(TREE_WIDTH_KEY, String(treeWidth.value))
    } catch {
      /* 存储不可用时忽略 */
    }
  }
  document.body.classList.add('tree-resizing')
  window.addEventListener('mousemove', onMove)
  window.addEventListener('mouseup', onUp)
}

function clearScanState() {
  solutionEndpoints.value = []
  selectedKeys.value = new Set()
  collapsedGroups.value = new Set()
  collapsedNamespaces.value = new Set()
  parsedDoc.value = null
  preview.value = null
  warnings.value = []
  searchKeyword.value = ''
  lastGenerateTaskId = '' // 旧生成任务对应的文件清单已失效，不再允许复用
  lastGenerateKeys = ''
}

async function scanSolution() {
  const scanPath = solutionPath.value.trim()
  if (!scanPath) return ElMessage.warning('请填写解决方案文件路径或项目根目录')
  if (specTaskRunning.value) return ElMessage.warning('已有进行中的扫描/生成任务，请等待完成')
  scanning.value = true
  scanned.value = false
  clearScanState()
  try {
    // 后台任务经 asyncTask store 统一跟踪（页内进度条 + 顶栏任务面板双展示，进度实时可见且页面可自由操作）
    const task = await asyncTaskStore.submit(() => startApiSpecScan(scanPath))
    specTaskId.value = task.taskId
    const final = await waitTask(task.taskId)
    if (final.status !== 'succeeded') return // 失败态留在进度条展示错误
    if (scanPath !== solutionPath.value.trim()) return // 扫描期间路径已改，丢弃过期结果
    // 终态后显式拉取完整结果（轮询快照不带 result）
    const full = await asyncTaskStore.fetchResult(task.taskId, final.kind)
    const endpoints = (full?.result as ApiSpecScanTaskResult | null)?.endpoints
    if (!endpoints) return ElMessage.error('扫描结果获取失败，请重试')
    solutionEndpoints.value = endpoints
    scanned.value = true
    scannedAt.value = new Date().toLocaleString('zh-CN', { hour12: false })
    localStorage.setItem(SOLUTION_KEY, scanPath)
    selectedKeys.value = new Set(solutionEndpoints.value.map(keyOf))
    // 默认折叠到一级：命名空间层、Controller 层全部折叠；键与列表分组一致（组键带命名空间前缀）
    collapsedGroups.value = new Set(solutionEndpoints.value.map(e => `${e.namespace || '未分组'}/${e.group}`))
    collapsedNamespaces.value = new Set(solutionEndpoints.value.map(e => e.namespace || '未分组'))
    specTaskId.value = '' // 成功即收起进度条
  } catch {
    /* 启动失败由 request.ts 弹错；任务失败态（含查询失联由 store 标记）留在进度条展示 */
  } finally {
    scanning.value = false
  }
}

async function generate() {
  if (selectedEndpoints.value.length === 0) return ElMessage.warning('请至少勾选一个接口')
  if (specTaskRunning.value) return ElMessage.warning('已有进行中的扫描/生成任务，请等待完成')
  generating.value = true
  parsedDoc.value = null
  preview.value = null
  warnings.value = []
  try {
    // 一次后台任务同时产出 IR 文档与导出内容（替代旧 Parse+Preview 双请求各全量解析一次的组合）
    const task = await asyncTaskStore.submit(() => startApiSpecGenerate({
      rootDir: solutionRootDir(),
      files: selectedControllerFiles().join(','),
      format: selectedFormat.value,
      title: docTitle.value.trim() || undefined,
      baseUrl: baseUrl.value.trim() || undefined,
      solutionPath: solutionPath.value.trim() || undefined,
      selectionKeys: selectedSelectionKeys(),
    }))
    specTaskId.value = task.taskId
    const final = await waitTask(task.taskId)
    if (final.status !== 'succeeded') return // 失败态留在进度条展示错误
    // 终态后显式拉取完整结果（轮询快照不带 result）
    const full = await asyncTaskStore.fetchResult(task.taskId, final.kind)
    const result = full?.result as ApiSpecGenerateTaskResult | null
    if (!result?.document || !result.export) return ElMessage.error('生成结果获取失败，请重试')
    parsedDoc.value = result.document
    preview.value = result.export
    generatedAt.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    warnings.value = [...new Set([...(result.document.warnings || []), ...(result.export.warnings || [])])]
    // 解析生成后首次展示：有警告自动弹一次，之后手动点「提示」入口查看
    if (warnings.value.length > 0) warnDialogVisible.value = true
    lastGenerateTaskId = task.taskId // 记录任务供换格式 ReExport / 导入 Apifox 复用解析结果
    lastGenerateKeys = selectionFingerprint()
    specTaskId.value = '' // 成功即收起进度条
  } catch {
    /* 启动失败由 request.ts 弹错；任务失败态（含查询失联由 store 标记）留在进度条展示 */
  } finally {
    generating.value = false
  }
}

watch(selectedFormat, async () => {
  if (!preview.value || !lastGenerateTaskId || specTaskRunning.value) return
  // 勾选已变化时保持当前预览（其内容属于旧勾选，换格式需重新点「解析并生成」）
  if (lastGenerateKeys !== selectionFingerprint()) return
  try {
    // 复用已完成任务的解析结果换格式导出，不重新解析源码（秒级）
    const res = await reExportApiSpec({
      taskId: lastGenerateTaskId,
      format: selectedFormat.value,
      title: docTitle.value.trim() || undefined,
      baseUrl: baseUrl.value.trim() || undefined,
    })
    preview.value = res
    generatedAt.value = new Date().toLocaleTimeString('zh-CN', { hour12: false })
    warnings.value = [...new Set([...(parsedDoc.value?.warnings || []), ...(res.warnings || [])])]
  } catch {
    /* silent 失败（如任务过期）仅轻提示，保持当前预览不变 */
    ElMessage.warning('切换格式失败（生成任务可能已过期），请重新点击「解析并生成」')
  }
})

watch(solutionPath, () => {
  if (solutionEndpoints.value.length === 0 && !scanned.value) return
  scanned.value = false
  clearScanState()
})

watch(baseUrl, v => localStorage.setItem(BASE_URL_KEY, v))

async function download() {
  if (!preview.value) return
  const blob = new Blob([preview.value.content], { type: preview.value.contentType })
  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = preview.value.fileName
  a.click()
  URL.revokeObjectURL(url)
}

async function copyContent() {
  if (!preview.value) return
  try {
    await navigator.clipboard.writeText(preview.value.content)
    ElMessage.success('已复制到剪贴板')
  } catch {
    ElMessage.error('复制失败：浏览器未授权剪贴板')
  }
}

function typeTitle(t: ApiSpecTypeDto) {
  return t.comment || (t.isEnum ? `枚举：${t.enumValues.join(' | ')}` : t.fields.map(f => `${f.name}: ${f.typeText}`).join('\n'))
}
</script>

<style scoped>
.api-spec-view {
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
  padding: 18px 22px 24px;
  max-width: 1600px;
  margin: 0 auto;
  /* 纵向 flex：.main 占满视口剩余高度实现双列固定 + 内部滚动；
     窗口过矮时根容器照常整体滚动（独立窗口 standalone 兼容） */
  display: flex;
  flex-direction: column;
}

.spec-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  margin-bottom: 20px;
}

.spec-header-text {
  min-width: 0;
}

.spec-title {
  margin: 0 0 6px;
  font-size: 22px;
  font-weight: 600;
}

.spec-sub {
  margin: 0;
  color: var(--text-sub, #909399);
  font-size: 13px;
}

/* 通用卡片 */
.card {
  background: var(--el-bg-color);
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 10px;
  box-shadow: 0 1px 3px rgba(17, 24, 39, 0.04);
}

.grow {
  flex: 1;
  min-width: 0;
}

/* 工具带：格式 pills + 数据源 */
.toolbar {
  padding: 12px 16px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 12px;
}

.tb-row {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.tb-label {
  font-size: 13px;
  font-weight: 600;
  white-space: nowrap;
  color: var(--text-main, #303133);
}

.tb-input {
  flex: 1;
  min-width: 220px;
}

.tb-divider {
  width: 1px;
  height: 20px;
  background: var(--el-border-color);
  margin: 0 2px;
  flex: none;
}

/* 格式分段选择器 */
.pills {
  display: inline-flex;
  gap: 4px;
  background: var(--el-fill-color-light);
  padding: 3px;
  border-radius: 8px;
}

.pill {
  border: none;
  background: transparent;
  padding: 5px 14px;
  font-size: 13px;
  border-radius: 6px;
  cursor: pointer;
  color: var(--el-text-color-regular);
  white-space: nowrap;
}

.pill:hover {
  color: var(--el-color-primary);
}

.pill.active {
  background: var(--el-bg-color);
  color: var(--el-color-primary);
  font-weight: 600;
  box-shadow: 0 1px 3px rgba(17, 24, 39, 0.08);
}

/* 统计条 */
.stats {
  display: flex;
  align-items: center;
  gap: 22px;
  padding: 10px 16px;
  margin-bottom: 12px;
}

.stat {
  display: flex;
  align-items: baseline;
  gap: 6px;
}

.stat-num {
  font-size: 20px;
  font-weight: 700;
  font-variant-numeric: tabular-nums;
}

.stat-lbl {
  font-size: 12px;
  color: var(--text-sub, #909399);
}

.stat.accent .stat-num {
  color: var(--el-color-primary);
}

.scan-time {
  font-size: 12px;
  color: var(--text-sub, #909399);
  white-space: nowrap;
}

/* 扫描/生成后台任务进度条（页内展示，替代全局加载遮罩） */
.spec-task-strip {
  margin-bottom: 16px;
  padding: 12px 14px;
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  background: var(--el-fill-color-light);
}

.spec-task-strip.is-failed {
  border-color: var(--el-color-danger-light-5);
}

.spec-task-line {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-top: 8px;
  font-size: 12px;
}

.spec-task-phase {
  flex: none;
  font-weight: 600;
  color: var(--text-sub, #606266);
}

.spec-task-current {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-sub, #909399);
  font-family: Consolas, Monaco, monospace;
}

.spec-task-strip.is-failed .spec-task-current {
  color: var(--el-color-danger);
  font-family: inherit;
}

.spec-task-dismiss {
  flex: none;
}

/* 主区：左树右工作台（占满视口剩余高度，双列等高固定，各自内部滚动） */
.main {
  flex: 1 1 auto;
  min-height: 420px;
  display: flex;
  gap: 14px;
  align-items: stretch;
}

/* 左：接口树（列高固定 = 主区高度，树体内部自滚动） */
.tree-card {
  width: 420px;
  flex: none;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.tree-head {
  padding: 10px 12px 6px;
}

.tree-ops {
  display: flex;
  align-items: center;
  gap: 2px;
  padding: 0 10px 8px;
  border-bottom: 1px solid var(--el-border-color-lighter);
  flex-wrap: wrap;
}

.tree {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 6px;
  /* 树内文本可选中复制（路径/Controller 名等）；截断路径复制得到的是完整文本。
     折叠头拖选不会误触折叠：拖动时 mouseup 与 mousedown 位置不同，浏览器不派发 click */
  user-select: text;
}

.tree-empty {
  padding: 28px 0;
  text-align: center;
  font-size: 12px;
  color: var(--text-sub, #909399);
}

.tree-foot {
  padding: 8px 14px;
  border-top: 1px solid var(--el-border-color-lighter);
  font-size: 12px;
  color: var(--text-sub, #909399);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* 两级分组（命名空间 → Controller） */
.ns-block + .ns-block {
  border-top: 1px solid var(--el-border-color-lighter);
}

.ns-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 9px 10px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
}

.ns-header:hover {
  background: var(--el-fill-color);
}

.ns-name {
  flex: 1;
  min-width: 0;
  font-weight: 600;
  color: var(--el-color-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.ns-block .group-header {
  padding-left: 24px;
}

.ns-block .endpoint-row {
  padding-left: 52px;
}

.group-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 7px 10px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 13px;
}

.group-header:hover {
  background: var(--el-fill-color-light);
}

.group-arrow {
  font-size: 10px;
  color: var(--text-sub, #909399);
  width: 12px;
  flex: none;
}

.group-name {
  flex: 1;
  min-width: 0;
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.group-count {
  margin-left: auto;
  flex: none;
  font-size: 12px;
  color: var(--text-sub, #909399);
}

.group-body {
  padding: 2px 0;
}

/* 双行行卡：无内部 flex（两行由子容器各自排布），保留层级缩进 40px */
.endpoint-row {
  padding: 6px 10px 6px 40px;
  border-radius: 6px;
  transition: background 0.1s;
}

.endpoint-row:hover {
  background: var(--el-fill-color-light);
}

/* 方法徽章：柔和浅底深字（暗色模式加深底，见文件底部 html.dark 覆盖） */
.ep-method {
  flex: none;
  width: 48px;
  text-align: center;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 700;
  padding: 2px 0;
  font-family: Consolas, Monaco, monospace;
}

.m-get { color: #2f6fdd; background: #e8f0fe; }
.m-post { color: #16a34a; background: #e6f6ec; }
.m-put { color: #b45309; background: #fdf3e3; }
.m-delete { color: #dc2626; background: #fdecec; }
.m-patch { color: #6d28d9; background: #f1ebfd; }
.m-head, .m-options { color: #64748b; background: #eef1f5; }
.m-dto { color: #7c3aed; background: #f1ebfd; }
.m-enum { color: #0e7490; background: #e6f7fb; }

/* 第一行：勾选 + 方法 + 路径（占满行宽，主角，不带服务器地址）+ 调试按钮 */
.ep-main {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
}

/* 第二行：说明 + 权限标签；起点对齐第一行 URL（勾选 14 + gap 8 + 方法徽章 48 + gap 8） */
.ep-sub {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 2px;
  padding-left: 78px;
}

/* 路径占满剩余宽度，超长悬停 title 看全量（含服务器地址） */
.ep-path {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  font-family: Consolas, Monaco, monospace;
  font-size: 12px;
}

.ep-summary {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--text-sub, #606266);
  font-size: 12px;
}

.ep-perm {
  flex: none;
}

.ep-debug {
  flex: none;
}

/* 左栏拖拽分割条：负 margin 横跨列间隙不占布局空间，hover/拖拽中显示主题色细线 */
.tree-resizer {
  flex: none;
  width: 14px;
  margin: 0 -7px;
  cursor: col-resize;
  position: relative;
  z-index: 2;
}

.tree-resizer::after {
  content: '';
  position: absolute;
  left: 6.5px;
  top: 0;
  bottom: 0;
  width: 1px;
  background: transparent;
  transition: background 0.15s;
}

.tree-resizer:hover::after,
body.tree-resizing .tree-resizer::after {
  background: var(--el-color-primary);
}

/* 右：工作台 */
.workbench {
  flex: 1;
  min-width: 0;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.set-card {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.set-grid {
  display: grid;
  grid-template-columns: auto 1fr auto 1fr;
  gap: 10px;
  align-items: center;
}

.set-grid .el-input {
  width: 100%;
}

.cta-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  border-top: 1px dashed var(--el-border-color-lighter);
  padding-top: 12px;
}

/* ── 生成提示弹窗 ── */
.warn-dialog-body {
  max-height: 50vh;
  overflow-y: auto;
}

.warn-group-title {
  font-size: 13px;
  color: #303133;
  margin-bottom: 8px;
}

.warn-type-cloud {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.warn-line {
  font-size: 13px;
  color: #606266;
  line-height: 1.8;
}

.warn-dialog-body > * + * {
  margin-top: 12px;
}

/* 预览卡（占满工作台剩余高度，内容区内部滚动，状态条常驻底部） */
.preview-card {
  flex: 1;
  min-height: 240px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.pv-tabs {
  display: flex;
  align-items: center;
  gap: 18px;
  padding: 0 16px;
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.pv-tab {
  padding: 10px 2px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  user-select: none;
}

.pv-tab:hover {
  color: var(--el-color-primary);
}

.pv-tab.active {
  color: var(--el-color-primary);
  font-weight: 600;
  border-bottom-color: var(--el-color-primary);
}

.pv-meta {
  font-size: 12px;
  color: var(--text-sub, #909399);
  font-family: Consolas, Monaco, monospace;
}

/* 生成内容：深色代码区（两主题统一深色，对齐配置文件编辑器体验） */
.pv-body {
  flex: 1;
  min-height: 0;
  background: #1e1e1e;
  padding: 10px 0 12px;
  font-family: 'Cascadia Code', Consolas, Monaco, monospace;
  font-size: 12.5px;
  line-height: 1.65;
  overflow: auto;
  color: #d4d4d4;
}

.pv-line {
  display: flex;
  min-width: max-content;
}

.pv-line:hover {
  background: rgba(255, 255, 255, 0.03);
}

.pv-ln {
  width: 44px;
  flex: none;
  text-align: right;
  padding-right: 14px;
  color: #6e7681;
  user-select: none;
}

.pv-code {
  white-space: pre;
}

/* 行数超限时回退：纯文本 pre（无行号） */
.pv-plain {
  margin: 0;
  padding: 2px 16px 6px 44px;
  white-space: pre;
  word-break: break-all;
}

.pv-placeholder {
  flex: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 48px 0;
  font-size: 13px;
  color: var(--text-sub, #909399);
  background: var(--el-fill-color-light);
}

.pv-status {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  font-size: 12px;
  color: var(--text-sub, #909399);
  border-top: 1px solid var(--el-border-color-lighter);
  background: var(--el-fill-color-light);
}

.pv-summary {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  padding: 10px 12px;
}

.pv-summary .summary-list {
  border: none;
  max-height: none;
}

.dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  display: inline-block;
  flex: none;
}

.dot.ok {
  background: #22c55e;
}

.summary-list {
  border: 1px solid var(--el-border-color-lighter);
  border-radius: 6px;
  max-height: 520px;
  overflow: auto;
  padding: 8px;
}

.summary-group {
  margin-bottom: 10px;
}

.summary-group-name {
  font-size: 12px;
  font-weight: 600;
  color: var(--el-color-primary);
  padding: 4px 0;
}

.summary-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 3px 0;
  font-size: 12px;
}

.summary-item .ep-path {
  width: auto;
  flex: 1;
}

/* 截断提示行（摘要/类型列表超渲染上限时） */
.summary-more {
  padding: 4px 0;
  font-size: 12px;
  color: var(--text-sub, #909399);
}

/* 暗色模式：树行悬浮 + 方法徽章加深底 */
html.dark .endpoint-row:hover {
  background: #1e293b;
}

html.dark .group-header:hover {
  background: #1e293b;
}

html.dark .ns-header:hover {
  background: #1e293b;
}

html.dark .m-get { color: #8ab4f8; background: rgba(47, 111, 221, 0.25); }
html.dark .m-post { color: #6ee7a0; background: rgba(22, 163, 74, 0.22); }
html.dark .m-put { color: #fbbf24; background: rgba(180, 83, 9, 0.25); }
html.dark .m-delete { color: #f87171; background: rgba(220, 38, 38, 0.22); }
html.dark .m-patch { color: #c4b5fd; background: rgba(109, 40, 217, 0.25); }
html.dark .m-head, html.dark .m-options { color: #94a3b8; background: rgba(100, 116, 139, 0.22); }
html.dark .m-dto { color: #c4b5fd; background: rgba(124, 58, 237, 0.25); }
html.dark .m-enum { color: #67e8f9; background: rgba(14, 116, 144, 0.28); }

.apifox-config-alert {
  margin-bottom: 18px;
}

/* 后台任务进度态（导入分批/批量删除共用） */
.apifox-task {
  padding: 8px 4px 4px;
}

.apifox-task-count {
  margin-top: 12px;
  font-weight: 600;
}

.apifox-task-current {
  margin-top: 6px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
  word-break: break-all;
}

.apifox-task-failed {
  margin-top: 8px;
  font-size: 13px;
  color: var(--el-color-warning);
}

.apifox-task-bg-tip {
  margin-top: 10px;
  font-size: 13px;
  color: var(--el-text-color-secondary);
}

.clear-token-button {
  margin-left: 10px;
}

.empty-tip {
  color: var(--text-sub, #909399);
  font-size: 13px;
  padding: 40px 0;
  text-align: center;
}

.empty-tip.big {
  padding: 80px 0;
  font-size: 14px;
}
</style>

<style>
/* JSON 语法高亮 token 配色（VS Code 暗色）：v-html 注入内容不携带 scoped 属性，
   需全局样式；tk- 前缀仅本页使用。深色代码区在两主题下均为深色，配色不变 */

/* 拖拽接口树分割条期间：全窗口列光标 + 禁止文本选中（scoped 无法选中 body） */
body.tree-resizing {
  cursor: col-resize;
  user-select: none;
}
.tk-k { color: #9cdcfe; }
.tk-s { color: #ce9178; }
.tk-b { color: #569cd6; }
.tk-n { color: #b5cea8; }
.tk-p { color: #d4d4d4; }
</style>
