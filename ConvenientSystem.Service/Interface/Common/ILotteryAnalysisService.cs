using ConvenientSystem.Shared.Model.Common;

namespace ConvenientSystem.Service.Common
{
    /// <summary>
    /// 彩票智能分析服务：基于历史开奖数据的多维度评分与号码推荐。
    /// </summary>
    public interface ILotteryAnalysisService
    {
        /// <summary>
        /// 生成智能分析报告：对每个号码进行 5 维评分，输出推荐号码、热/冷号池、AI 组合与摘要。
        /// </summary>
        /// <param name="type">彩种代码（DLT/SSQ/PL5/FC3D）</param>
        /// <param name="periods">分析期数（默认 100）</param>
        LotteryAnalysisDto Predict(string type, int periods = 100);
    }
}
