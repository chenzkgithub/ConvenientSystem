import { onMounted, reactive, ref, toRaw, type Ref } from 'vue'

/** 分页接口的返回结构（后端统一 { list, total }） */
export interface PagedResult<T> {
  list: T[]
  total: number
}

/**
 * 数据源：既支持分页接口 { list, total }，也支持一次性返回全量数组的接口。
 * 参数用 any：各 api 函数的参数类型各不相同，统一成 Record<string, unknown> 会因逆变而无法直接传入。
 */
export type DataTableFetcher<T> = (params: any) => Promise<PagedResult<T> | T[]>

export interface UseDataTableOptions<F extends Record<string, any>> {
  /**
   * 筛选条件对象。字段名直接按接口参数名命名，load 时会自动把非空字段并入请求参数
   * （空串 / null / undefined 视为未填，不下发），省掉各页面手写的一串 if 判断。
   */
  filters?: F
  /** 是否分页（默认 true）。false 时不下发 page/size，total 取列表长度 */
  paged?: boolean
  /** 每页条数初值 */
  pageSize?: number
  /** 是否在 onMounted 时自动加载一次（默认 true） */
  immediate?: boolean
  /**
   * 追加/改写请求参数。用于 filters 无法直接映射的场景，
   * 典型如日期区间需要拆成 startTime / endTime 两个参数。
   * 返回值里显式给某个键 undefined，表示该参数不下发（可用来剔除仅供界面绑定的 filters 字段）。
   */
  extraParams?: (filters: F) => Record<string, unknown>
}

/**
 * 列表页数据装载的统一封装：收敛 loading / 分页 / 筛选参数拼装 / try-catch-finally 这套样板代码。
 *
 * 请求失败不再向上抛出，也不重复提示——错误提示由 request.ts 全局处理，
 * 与既有列表页 catch 块留空注释的做法保持一致。
 *
 * 用法（必须解构：嵌套在普通对象里的 ref 在模板中不会自动解包）：
 *   const filters = reactive({ account: '', success: undefined as boolean | undefined })
 *   const { loading, list, total, page, size, load, search, reset } = useDataTable(listAuditLogs, { filters })
 *   // 模板：:data="list" :loading="loading" :total="total"
 *   //       v-model:page="page" v-model:pageSize="size"
 *   //       searchable @load="load" @search="search" @reset="reset"
 */
export function useDataTable<T, F extends Record<string, any> = Record<string, never>>(
  fetcher: DataTableFetcher<T>,
  options: UseDataTableOptions<F> = {}
) {
  const { paged = true, pageSize = 20, immediate = true, extraParams } = options

  const loading = ref(false)
  const list = ref<T[]>([]) as Ref<T[]>
  const total = ref(0)
  const page = ref(1)
  const size = ref(pageSize)

  const filters = (options.filters ? reactive(options.filters) : reactive({})) as F
  // 初始筛选值快照：reset 时逐字段还原。
  // 只做一层复制（数组另开一份），筛选值都是原始类型或由控件整体替换的数组，够用且能保住 undefined。
  const initialFilters = snapshot(options.filters ?? ({} as F))

  function snapshot(source: F): Record<string, unknown> {
    const result: Record<string, unknown> = {}
    for (const key of Object.keys(toRaw(source))) {
      const value = (source as Record<string, unknown>)[key]
      result[key] = Array.isArray(value) ? [...value] : value
    }
    return result
  }

  /** 拼装请求参数：分页字段 + 非空筛选字段 + extraParams 覆盖 */
  function buildParams(): Record<string, unknown> {
    const params: Record<string, unknown> = {}
    if (paged) {
      params.page = page.value
      params.size = size.value
    }
    for (const [key, value] of Object.entries(filters as Record<string, unknown>)) {
      if (value === '' || value === null || value === undefined) continue
      params[key] = value
    }
    if (extraParams) {
      // extraParams 里显式给 undefined 表示"不下发该参数"，
      // 用于 filters 中仅供界面绑定、不直接作为接口参数的字段（如日期区间）。
      for (const [key, value] of Object.entries(extraParams(filters))) {
        if (value === undefined) delete params[key]
        else params[key] = value
      }
    }
    return params
  }

  async function load() {
    loading.value = true
    try {
      const res = await fetcher(buildParams())
      if (Array.isArray(res)) {
        list.value = res
        total.value = res.length
      } else {
        list.value = res.list ?? []
        total.value = res.total ?? 0
      }
    } catch {
      /* 错误已由 request.ts 弹出提示 */
    } finally {
      loading.value = false
    }
  }

  /** 查询：筛选条件变化后必须回到第一页，否则会停在越界页导致"查不到" */
  async function search() {
    page.value = 1
    await load()
  }

  /** 重置：还原筛选条件初值后重新查询 */
  async function reset() {
    for (const [key, value] of Object.entries(initialFilters)) {
      ;(filters as Record<string, unknown>)[key] = Array.isArray(value) ? [...value] : value
    }
    await search()
  }

  if (immediate) onMounted(load)

  return { loading, list, total, page, size, filters, load, search, reset }
}
