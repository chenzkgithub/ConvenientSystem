<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ElMessage } from 'element-plus'
import CommonDataTable, { type DataTableColumn } from '@/common/components/CommonDataTable.vue'
import { formatCreator } from '@/common/formatCreator'
import { getNoticeList, saveNotice, deleteNotice, NOTICE_LEVELS, type NoticeDto } from '@/common/api/notice'
import { listUsers } from '@/common/api/userManage'
import { listRoles } from '@/common/api/roleManage'
import { confirmDelete } from '@/common/utils/confirm'
import CommonDialog from '@/common/components/CommonDialog.vue'

// ========== 列表 ==========
const loading = ref(false)
const notices = ref<NoticeDto[]>([])

async function loadData() {
  loading.value = true
  try {
    notices.value = await getNoticeList()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    loading.value = false
  }
}

const columns: DataTableColumn<NoticeDto>[] = [
  { prop: 'title', label: '标题', minWidth: 180, showOverflowTooltip: true },
  { prop: 'level', label: '级别', width: 80, align: 'center', custom: true },
  { prop: 'targets', label: '发送范围', width: 150, custom: true },
  { prop: 'channels', label: '联动推送', width: 170, custom: true },
  { prop: 'content', label: '内容', minWidth: 220, showOverflowTooltip: true },
  { prop: 'expireTime', label: '有效期', width: 160, align: 'center', custom: true },
  { prop: 'enabled', label: '状态', width: 80, align: 'center', custom: true },
  { prop: 'createdBy', label: '发布人', width: 150, formatter: (row) => formatCreator(row), showOverflowTooltip: true },
  { prop: 'createTime', label: '发布时间', width: 170 },
]

/** 联动推送渠道标签（未勾选任何渠道表示仅站内通知） */
function channelTags(row: NoticeDto): { label: string; type: 'primary' | 'success' | 'warning' }[] {
  const tags: { label: string; type: 'primary' | 'success' | 'warning' }[] = []
  if (row.sendEmail) tags.push({ label: '邮件', type: 'primary' })
  if (row.sendSms) tags.push({ label: '短信', type: 'success' })
  if (row.sendWebhook) tags.push({ label: '群机器人', type: 'warning' })
  return tags
}

/** 有效期是否已过期（无有效期=永久有效） */
function isExpired(row: NoticeDto): boolean {
  return !!row.expireTime && new Date(row.expireTime).getTime() <= Date.now()
}
function expireText(row: NoticeDto): string {
  if (!row.expireTime) return '永久有效'
  return row.expireTime.replace('T', ' ').slice(0, 16)
}

// ========== 发布/编辑弹层 ==========
const dialogVisible = ref(false)
const isEdit = ref(false)
const saving = ref(false)
const formRef = ref()
const form = ref<NoticeDto>(emptyForm())

function emptyForm(): NoticeDto {
  return {
    id: 0,
    title: '',
    content: '',
    level: 1,
    sendEmail: false,
    sendSms: false,
    sendWebhook: false,
    enabled: true,
    expireTime: null,
    targetUserIds: [],
    targetRoleIds: [],
  }
}

// ========== 发送范围候选（用户/角色） ==========
const userOptions = ref<{ id: string; label: string }[]>([])
const roleOptions = ref<{ id: number; label: string }[]>([])

/** 打开发布/编辑弹层时懒加载候选（首次加载后缓存；接口无权限时静默降级为空列表） */
async function loadTargetOptions() {
  if (userOptions.value.length === 0) {
    try {
      const users = await listUsers()
      userOptions.value = users
        .filter((u) => u.enabled)
        .map((u) => ({ id: u.id, label: `${u.displayName || u.account}（${u.account}）` }))
    } catch { /* 无用户管理权限时不阻断发布，仅不能定向选人 */ }
  }
  if (roleOptions.value.length === 0) {
    try {
      const roles = await listRoles()
      roleOptions.value = roles.filter((r) => r.enabled).map((r) => ({ id: r.id, label: r.name }))
    } catch { /* 同上 */ }
  }
}

const rules = {
  title: [{ required: true, message: '请输入通知标题', trigger: 'blur' }],
  content: [{ required: true, message: '请输入通知内容', trigger: 'blur' }],
}

/** 新建时勾选了联动推送渠道，保存即推送；提示用户确认 */
const pushHint = computed(() => {
  if (isEdit.value) return ''
  const channels: string[] = []
  if (form.value.sendEmail) channels.push('邮件')
  if (form.value.sendSms) channels.push('短信')
  if (form.value.sendWebhook) channels.push('群机器人')
  return channels.length > 0 ? `发布后将立即联动推送：${channels.join('、')}` : ''
})

function openCreate() {
  isEdit.value = false
  form.value = emptyForm()
  dialogVisible.value = true
  void loadTargetOptions()
}

function openEdit(row: NoticeDto) {
  isEdit.value = true
  form.value = { ...row, targetUserIds: [...(row.targetUserIds || [])], targetRoleIds: [...(row.targetRoleIds || [])] }
  dialogVisible.value = true
  void loadTargetOptions()
}

async function submitForm() {
  try {
    await formRef.value?.validate()
  } catch {
    return
  }
  saving.value = true
  try {
    await saveNotice(form.value)
    ElMessage.success(isEdit.value ? '通知已更新' : '通知已发布')
    dialogVisible.value = false
    await loadData()
  } catch { /* 错误已由 request.ts 弹出提示 */ } finally {
    saving.value = false
  }
}

async function handleDelete(row: NoticeDto) {
  const ok = await confirmDelete(row.title, () => deleteNotice(row.id))
  if (ok) await loadData()
}

onMounted(loadData)
</script>

<template>
  <div class="notice-manage-page">
    <!-- 通知列表（按钮行封装进列表组件插槽，与表格统一对齐） -->
    <div class="notice-list">
      <CommonDataTable
        :columns="columns"
        :data="notices"
        :loading="loading"
        :total="notices.length"
        empty-text="暂无通知"
        :actions-width="150"
        @load="loadData"
      >
        <template #toolbar>
          <el-button v-if="$has('notice:publish')" type="success" @click="openCreate">+ 发布通知</el-button>
          <el-button @click="loadData">刷新</el-button>
        </template>
        <template #cell-level="{ row }">
          <el-tag :type="NOTICE_LEVELS[(row as NoticeDto).level]?.type || 'info'" size="small">
            {{ NOTICE_LEVELS[(row as NoticeDto).level]?.label || '普通' }}
          </el-tag>
        </template>
        <template #cell-targets="{ row }">
          <template v-if="((row as NoticeDto).targetUserCount ?? 0) === 0 && ((row as NoticeDto).targetRoleCount ?? 0) === 0">
            <el-tag type="success" size="small">全员</el-tag>
          </template>
          <template v-else>
            <el-tag v-if="((row as NoticeDto).targetUserCount ?? 0) > 0" size="small" style="margin-right: 4px">
              用户×{{ (row as NoticeDto).targetUserCount }}
            </el-tag>
            <el-tag v-if="((row as NoticeDto).targetRoleCount ?? 0) > 0" type="warning" size="small">
              角色×{{ (row as NoticeDto).targetRoleCount }}
            </el-tag>
          </template>
        </template>
        <template #cell-channels="{ row }">
          <template v-if="channelTags(row as NoticeDto).length > 0">
            <el-tag v-for="tag in channelTags(row as NoticeDto)" :key="tag.label" :type="tag.type" size="small" style="margin-right: 4px">
              {{ tag.label }}
            </el-tag>
          </template>
          <span v-else style="color: var(--el-text-color-secondary)">仅站内</span>
        </template>
        <template #cell-expireTime="{ row }">
          <el-tag v-if="!(row as NoticeDto).expireTime" type="info" size="small" effect="plain">永久有效</el-tag>
          <el-tag v-else-if="isExpired(row as NoticeDto)" type="danger" size="small" effect="plain">
            已过期（{{ expireText(row as NoticeDto) }}）
          </el-tag>
          <el-tag v-else type="warning" size="small" effect="plain">至 {{ expireText(row as NoticeDto) }}</el-tag>
        </template>
        <template #cell-enabled="{ row }">
          <el-tag :type="row.enabled ? 'success' : 'info'" size="small">{{ row.enabled ? '启用' : '停用' }}</el-tag>
        </template>
        <template #actions="{ row }">
          <el-button link type="primary" @click="openEdit(row as NoticeDto)">编辑</el-button>
          <el-button v-if="$has('notice:delete')" link type="danger" @click="handleDelete(row as NoticeDto)">删除</el-button>
        </template>
      </CommonDataTable>
    </div>

    <!-- 发布/编辑弹层 -->
    <CommonDialog v-model="dialogVisible" :title="isEdit ? '编辑通知' : '发布通知'" width="640px">
      <el-form ref="formRef" :model="form" :rules="rules" label-width="90px">
        <el-form-item label="标题" prop="title">
          <el-input v-model="form.title" maxlength="200" placeholder="如：系统维护通知" />
        </el-form-item>
        <el-form-item label="级别">
          <el-radio-group v-model="form.level">
            <el-radio :value="1">普通</el-radio>
            <el-radio :value="2">重要</el-radio>
            <el-radio :value="3">紧急</el-radio>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="内容" prop="content">
          <el-input v-model="form.content" type="textarea" :rows="6" placeholder="通知内容" />
        </el-form-item>
        <el-form-item label="发送范围">
          <div class="target-selects">
            <el-select
              v-model="form.targetUserIds"
              multiple
              filterable
              clearable
              collapse-tags
              collapse-tags-tooltip
              placeholder="指定人员（默认全员）"
            >
              <el-option v-for="u in userOptions" :key="u.id" :label="u.label" :value="u.id" />
            </el-select>
            <el-select
              v-model="form.targetRoleIds"
              multiple
              filterable
              clearable
              collapse-tags
              collapse-tags-tooltip
              placeholder="指定角色（默认全员）"
            >
              <el-option v-for="r in roleOptions" :key="r.id" :label="r.label" :value="r.id" />
            </el-select>
          </div>
          <div class="push-hint" style="margin-left: 0">人员与角色均未指定时默认发送给全部人员；指定后仅范围内人员可见</div>
        </el-form-item>
        <el-form-item label="联动推送">
          <el-checkbox v-model="form.sendEmail">邮件</el-checkbox>
          <el-checkbox v-model="form.sendSms">短信</el-checkbox>
          <el-checkbox v-model="form.sendWebhook">群机器人</el-checkbox>
          <div class="push-hint">
            <template v-if="pushHint">{{ pushHint }}</template>
            <template v-else-if="!isEdit">未勾选时仅作站内通知，用户在顶栏铃铛查看</template>
            <template v-else>编辑不会重新触发推送</template>
          </div>
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
          <span class="push-hint">停用后用户端不再展示该通知</span>
        </el-form-item>
        <el-form-item label="有效期">
          <el-date-picker
            v-model="form.expireTime"
            type="datetime"
            placeholder="选填：过期后用户端不再展示"
            value-format="YYYY-MM-DDTHH:mm:ss"
            clearable
            style="width: 240px"
          />
          <span class="push-hint">不填则永久有效</span>
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="submitForm">{{ isEdit ? '保存' : '发布' }}</el-button>
      </template>
    </CommonDialog>
  </div>
</template>

<style scoped>
.notice-manage-page {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.notice-list {
  flex: 1;
  min-height: 0;
}
.push-hint {
  display: inline-block;
  margin-left: 12px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.target-selects {
  display: flex;
  gap: 8px;
  width: 100%;
}
.target-selects .el-select {
  flex: 1;
}
</style>
