// 创建人展示工具：后端已移除 CreatedBy 冗余账号字段，列表通过 CreatedById 关联 SysUser
// 返回创建人账号与姓名，前端统一按“姓名（账号）”格式展示。

/** 携带创建人信息的行对象（邮件/短信任务、模板、日志等列表通用） */
export interface CreatorInfo {
  createdByAccount?: string | null
  createdByName?: string | null
}

/** 创建人展示文本：有姓名时“姓名（账号）”，否则回退账号或“-” */
export function formatCreator(row: CreatorInfo): string {
  const account = row.createdByAccount || ''
  const name = row.createdByName || ''
  if (name && account) return `${name}（${account}）`
  return name || account || '-'
}
