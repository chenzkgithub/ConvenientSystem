<script setup lang="ts">
/**
 * 官网通告 · 全国中奖情况 弹窗（共享组件）
 * 有官方通告 PDF（体彩大乐透/排列五）：pdf.js 逐页渲染成图片直接展示
 * 无 PDF（双色球/福彩3D）：展示采自官网的通告数据表格（奖级/销量/奖池/中奖地区）
 * 用法：const ref = ref(); ref.value?.open(type, issueNumber)
 */
import { ref } from 'vue'
import { ElMessage } from 'element-plus'
import CommonDialog from '@/common/components/CommonDialog.vue'
import * as pdfjsLib from 'pdfjs-dist'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import { fullscreenElement } from '@/common/utils/fullscreen'
import {
  getLotteryDrawNotice, isPositional,
  type LotteryDrawNotice,
} from '@/common/lottery'

// pdf.js 渲染官方通告 PDF 用（worker 单独加载）
pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl

const visible = ref(false)
const noticeData = ref<LotteryDrawNotice | null>(null)
/** 该彩种是否位置型（号码球格式化口径） */
const positional = ref(false)
/** 通告 PDF 逐页渲染图（base64）与加载态 */
const pdfImages = ref<string[]>([])
const pdfLoading = ref(false)

async function open(type: string, issue: string) {
  try {
    const data = await getLotteryDrawNotice(type, issue)
    noticeData.value = data
    positional.value = isPositional(type)
    pdfImages.value = []
    visible.value = true
    if (data.noticeUrl) renderNoticePdf(data.noticeUrl)
  } catch (err: any) {
    ElMessage.error({ message: err?.message || '获取官网通告失败', appendTo: fullscreenElement() })
  }
}

/** 通告 PDF 逐页渲染成图片（直接展示，避免 iframe 内嵌 PDF 查看器的深色背景边） */
async function renderNoticePdf(url: string) {
  pdfLoading.value = true
  try {
    const doc = await pdfjsLib.getDocument(url).promise
    for (let i = 1; i <= doc.numPages; i++) {
      const page = await doc.getPage(i)
      const viewport = page.getViewport({ scale: 2 })
      const canvas = document.createElement('canvas')
      canvas.width = viewport.width
      canvas.height = viewport.height
      const ctx = canvas.getContext('2d')
      if (!ctx) break
      // 先铺白底：PDF 页面未绘制背景时透明，转 JPEG 会发黑
      ctx.fillStyle = '#ffffff'
      ctx.fillRect(0, 0, canvas.width, canvas.height)
      await page.render({ canvas, canvasContext: ctx, viewport } as any).promise
      pdfImages.value.push(canvas.toDataURL('image/jpeg', 0.92))
    }
    await doc.destroy()
  } catch {
    ElMessage.error({ message: '通告 PDF 渲染失败', appendTo: fullscreenElement() })
  } finally {
    pdfLoading.value = false
  }
}

/** 号码球文本：位置型不补零 */
function fmtBall(n: number): string {
  return positional.value ? String(n) : String(n).padStart(2, '0')
}

/** 千分位金额/注数 */
function fmtMoney(n: number): string {
  return n.toLocaleString('zh-CN')
}

defineExpose({ open })
</script>

<template>
  <CommonDialog
    v-model="visible"
    :title="`官网通告 · 全国中奖情况 · 第 ${noticeData?.issueNumber ?? ''} 期`"
    :width="noticeData?.noticeUrl ? '720px' : '620px'"
    destroy-on-close
  >
    <template v-if="noticeData">
      <!-- 有官方通告 PDF（体彩大乐透/排列五）：逐页渲染成图片直接展示，无查看器黑边 -->
      <div v-if="noticeData.noticeUrl" class="notice-pdf-pages" v-loading="pdfLoading">
        <img v-for="(src, i) in pdfImages" :key="'pdf' + i" :src="src" class="notice-pdf-img" alt="官方通告" />
      </div>
      <!-- 无 PDF（双色球/福彩3D）：展示采自官网的通告数据表格 -->
      <template v-else>
        <div class="notice-block">
          <div class="notice-label">
            开奖号码
            <span class="notice-sub">{{ noticeData.drawDate?.substring(0, 10) }}</span>
          </div>
          <div class="notice-balls">
            <span v-for="(n, i) in noticeData.front" :key="'nf' + i"
              class="notice-ball front-ball">{{ fmtBall(n) }}</span>
            <template v-if="noticeData.back.length > 0">
              <span class="notice-sep">+</span>
              <span v-for="(n, i) in noticeData.back" :key="'nb' + i"
                class="notice-ball back-ball">{{ fmtBall(n) }}</span>
            </template>
          </div>
        </div>

        <div class="notice-block">
          <div class="notice-label">全国中奖情况</div>
          <el-table v-if="noticeData.grades.length > 0" :data="noticeData.grades" size="small" border>
            <el-table-column prop="grade" label="奖级" width="120" align="center" />
            <el-table-column label="全国中奖注数" align="center">
              <template #default="{ row }">{{ row.count != null ? fmtMoney(row.count) : '—' }}</template>
            </el-table-column>
            <el-table-column label="单注奖金（元）" align="center">
              <template #default="{ row }">{{ row.money != null ? fmtMoney(row.money) : '—' }}</template>
            </el-table-column>
          </el-table>
          <el-alert
            v-else
            type="info"
            :closable="false"
            title="该期暂无全国中奖明细（历史期未采集，新开奖期将自动采集）"
          />
          <div v-if="noticeData.salesAmount != null || noticeData.poolBalance != null" class="notice-meta">
            <span v-if="noticeData.salesAmount != null">当期销量：¥{{ fmtMoney(noticeData.salesAmount) }}</span>
            <span v-if="noticeData.poolBalance != null">奖池滚存：¥{{ fmtMoney(noticeData.poolBalance) }}</span>
          </div>
          <!-- 一等奖中奖地区（福彩双色球官网通告口径） -->
          <div v-if="noticeData.prizeArea" class="notice-area">
            <span class="notice-area-label">一等奖中奖地区：</span>{{ noticeData.prizeArea }}
          </div>
        </div>
      </template>
    </template>
  </CommonDialog>
</template>

<style scoped>
.notice-pdf-pages {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 10px;
  max-height: 75vh;
  overflow: auto;
  background: #fff;
}
.notice-pdf-img {
  width: 100%;
  display: block;
}
.notice-block {
  margin-bottom: 18px;
}
.notice-block:last-child {
  margin-bottom: 0;
}
.notice-label {
  font-size: 13px;
  font-weight: 600;
  color: #606266;
  margin-bottom: 8px;
}
.notice-sub {
  font-weight: 400;
  color: #909399;
  margin-left: 8px;
}
.notice-balls {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: nowrap;
}
.notice-ball {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 30px;
  height: 30px;
  border-radius: 50%;
  font-size: 13px;
  font-weight: 700;
  color: #fff;
  flex-shrink: 0;
}
.notice-ball.front-ball { background: #e6393a; }
.notice-ball.back-ball { background: #2563eb; }
.notice-sep { color: #c0c4cc; font-size: 14px; margin: 0 2px; flex-shrink: 0; }
.notice-meta {
  display: flex;
  gap: 24px;
  margin-top: 10px;
  font-size: 13px;
  color: #909399;
}
.notice-area {
  margin-top: 10px;
  font-size: 13px;
  color: #606266;
  line-height: 1.6;
}
.notice-area-label {
  font-weight: 600;
  color: #e6393a;
}
</style>
