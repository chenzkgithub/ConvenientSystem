using PdfiumViewer.Standard;
using System.Drawing;
using System.Drawing.Imaging;

namespace ConvenientSystem.Shared.Common
{
    /// <summary>
    /// PDF 逐页渲染为 JPEG 图片（PdfiumViewer 引擎，白底渲染）。
    /// 供开奖结果邮件将官网通告 PDF 以图片形式内嵌正文使用（与前端弹窗 pdf.js 渲染口径一致）。
    /// </summary>
    public static class PdfImageRenderer
    {
        /// <summary>
        /// 将 PDF 字节流逐页渲染为 JPEG：
        /// 按 144 DPI（2 倍）渲染保证清晰度，宽度超 maxWidth 时按比例缩小防止邮件过大；
        /// 最多渲染 maxPages 页；加载或渲染失败返回空列表（调用方回退为外链展示）
        /// </summary>
        public static List<byte[]> RenderToJpeg(byte[] pdfBytes, int maxWidth = 1200, int maxPages = 6)
        {
            var result = new List<byte[]>();
            try
            {
                using var ms = new MemoryStream(pdfBytes);
                using var doc = PdfDocument.Load(ms);

                var pages = Math.Min(doc.PageCount, maxPages);
                for (var i = 0; i < pages; i++)
                {
                    var size = doc.PageSizes[i]; // PDF 页面尺寸（点）
                    var scale = Math.Min(2f, maxWidth / size.Width);
                    using var img = doc.Render(i,
                        (int)(size.Width * scale), (int)(size.Height * scale),
                        144f, 144f, PdfRenderFlags.ForPrinting);

                    using var jpeg = new MemoryStream();
                    ((Bitmap)img).Save(jpeg, ImageFormat.Jpeg);
                    result.Add(jpeg.ToArray());
                }
            }
            catch
            {
                // 通告 PDF 下载损坏或格式异常时不影响邮件主体，回退为外链展示
                return new List<byte[]>();
            }
            return result;
        }
    }
}
