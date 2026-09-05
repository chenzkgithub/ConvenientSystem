using ConvenientSystem.Shared.Common.Exceptions;
using ConvenientSystem.Shared.Entity.YunHan;
using ConvenientSystem.Shared.Model.YunHan;
using Newtonsoft.Json;
using System.Linq.Expressions;

namespace ConvenientSystem.Service.YunHan
{
    /// <summary>
    /// 考勤查询业务服务实现（读取业务库）。
    /// </summary>
    public class AttendanceService : IAttendanceService
    {
        private readonly IFreeSql _fsql;
        private readonly ILogger<AttendanceService> _logger;

        public AttendanceService(IFreeSql fsql, ILogger<AttendanceService> logger)
        {
            _fsql = fsql;
            _logger = logger;
        }

        public async Task<List<DeptView>> GetDeptTreeAsync()
        {
            _logger.LogInformation("查询部门数据开始");
            string sql = @"SELECT corpId,corpName,dept_id,dept_name,parent_id,full_name,full_code,lvl,deptIsDel FROM DeptView order by corpId";
            var data = await _fsql.Ado.QueryAsync<DeptView>(sql);
            _logger.LogInformation("查询部门数据结束，共{Count}条", data.Count);
            return data;
        }

        public async Task<List<AttendanceSumDto>> GetAttendanceAsync(RequestDto request, CancellationToken ct)
        {
            _logger.LogInformation("查询考勤数据开始");
            _logger.LogDebug("查询考勤数据请求参数：{Request}", JsonConvert.SerializeObject(request));

            var data = await QueryAttendanceSumAsync(request, null, ct);

            _logger.LogInformation("查询考勤数据结束，共{Count}条", data.Count);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("查询考勤数据结果：{Result}", JsonConvert.SerializeObject(data));
            return data;
        }

        public async Task<List<AttendanceDto>> GetAttendanceDtlAsync(RequestDto request)
        {
            _logger.LogInformation("查询考勤明细数据开始");
            _logger.LogDebug("查询考勤明细请求参数：{Request}", JsonConvert.SerializeObject(request));
            string sql = @"
                SELECT 
                    duser.avatar,
                    buv.full_code,
                    buv.dept_name as deptName,
                    duser.UserName,
                    duser.DingCode as dingCode,
                    duser.hired_date as HiredDate,
                    bu.WorkDuration,
                    bu.LeaveDuration,
                    bu.TravelDuration,
                    bu.OvertimeDuration,
                    bu.ActualDuration,
                    bu.workTime,
                    bu.restTime,
                    FORMAT(bu.WorkDate, 'yyyy-MM-dd') WorkDate,
                    duser.employeeStatus
                FROM bu_attendance bu 
                INNER JOIN dingtalkuser duser ON bu.UserId = duser.DDUserId  
                INNER JOIN DeptView buv ON duser.DefaultDeptId = buv.dept_id";
            List<string> whereParts = new List<string>();

            // 固定条件：排除已删除人员
            whereParts.Add("duser.IsDelete = 0");

            // 条件1：部门编码
            if (!string.IsNullOrWhiteSpace(request.fullCode))
            {
                whereParts.Add("buv.full_code like @FullCode+'%'");
            }
            // 条件2：姓名
            if (!string.IsNullOrWhiteSpace(request.userName))
            {
                whereParts.Add("duser.UserName  like '%'+@UserName+'%'");
            }
            // 条件2.1：钉钉用户ID（精确定位到具体人员，避免同名混淆）
            if (!string.IsNullOrWhiteSpace(request.DDUserId))
            {
                whereParts.Add("duser.DDUserId = @DDUserId");
            }
            // 条件3：年月（用日期区间代替 FORMAT，走 WorkDate 索引，大数据量下不再全表扫描）
            bool hasMonth = TryGetMonthRange(request.month, out DateTime mStart, out DateTime mEnd);
            if (hasMonth)
            {
                whereParts.Add(@"bu.WorkDate >= @Start AND bu.WorkDate < @End");
            }

            // 拼接完整SQL
            if (whereParts.Any())
            {
                sql += " WHERE " + string.Join(" AND ", whereParts);
            }
            var data = await _fsql.Ado.QueryAsync<AttendanceDto>(sql,
                new { FullCode = request.fullCode, UserName = request.userName, DDUserId = request.DDUserId, Start = mStart, End = mEnd });
            _logger.LogInformation("查询考勤明细数据结束，共{Count}条", data.Count);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("查询考勤明细数据结果：{Result}", JsonConvert.SerializeObject(data));
            return data;
        }

        public async Task<List<AttendanceSumDto>> GetDailyRankingAsync(RequestDto request, CancellationToken ct)
        {
            _logger.LogInformation("查询当月排行数据开始");
            _logger.LogDebug("查询当月排行请求参数：{Request}", JsonConvert.SerializeObject(request));

            if (!TryGetMonthRange(request.month, out _, out _))
            {
                throw new BadRequestException("月份参数格式不正确，应为 yyyy-MM");
            }

            // 按当前部门口径排行：使用请求的 fullCode 筛选部门，复用取数逻辑取前 100
            var rankReq = new RequestDto { fullCode = request.fullCode ?? "", userName = "", month = request.month, orderby = request.orderby };
            var data = await QueryAttendanceSumAsync(rankReq, 100, ct);

            _logger.LogInformation("查询当月排行数据结束，共{Count}条", data.Count);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("查询当月排行数据结果：{Result}", JsonConvert.SerializeObject(data));
            return data;
        }

        /// <summary>
        /// 按人分组汇总考勤数据（GetAttendance / GetDailyRanking 共用取数逻辑）。
        /// 用日期区间代替 FORMAT 函数，保证 WorkDate 索引可用；top 有值时只取前 N 条。
        /// </summary>
        private async Task<List<AttendanceSumDto>> QueryAttendanceSumAsync(RequestDto requet, int? top, CancellationToken ct)
        {
            bool hasMonth = TryGetMonthRange(requet.month, out DateTime mStart, out DateTime mEnd);

            Expression<Func<AttendanceSumDto, object>> orderExp = (requet.orderby ?? "").ToLower() switch
            {
                "workduration" => a => a.WorkDuration,
                "leaveduration" => a => a.LeaveDuration,
                "travelduration" => a => a.TravelDuration,
                "overtimeduration" => a => a.OvertimeDuration,
                "actualduration" => a => a.ActualDuration,
                _ => a => a.OvertimeDuration //默认
            };
            var query = _fsql.Select<BuAttendanceEntity, DingtalkUserEntity, DeptView>().
                InnerJoin((a, b, c) => a.UserId == b.DDUserId).
                InnerJoin((a, b, c) => b.DefaultDeptId == c.dept_id)
                .Where((a, b, c) => b.IsDelete == false)
                .WhereIf(!string.IsNullOrWhiteSpace(requet.fullCode), (a, b, c) => c.full_code.StartsWith(requet.fullCode!))
                .WhereIf(!string.IsNullOrWhiteSpace(requet.userName), (a, b, c) => b.UserName != null && b.UserName.Contains(requet.userName!))
                .WhereIf(hasMonth, (a, b, c) => a.WorkDate >= mStart && a.WorkDate < mEnd)
                .GroupBy((a, b, c) => new { b.DDUserId, b.UserName, c.full_code })
                .WithTempQuery(t => new AttendanceSumDto
                {
                    avatar = t.Max(t.Value.Item2.avatar),
                    DDUserId = t.Max(t.Value.Item2.DDUserId),
                    dingCode = t.Max(t.Value.Item2.DingCode),
                    corpId = t.Max(t.Value.Item2.corpId),
                    employeeStatus = t.Max(t.Value.Item2.EmployeeStatus),
                    UserName = t.Value.Item2.UserName,
                    deptId = t.Value.Item3.full_code,
                    deptName = t.Max(t.Value.Item3.dept_name),
                    fullDeptName = t.Max(t.Value.Item3.full_name),
                    HiredDate = t.Max(t.Value.Item2.hired_date.Value).ToString("yyyy-MM-dd"),
                    WorkDuration = t.Sum(t.Value.Item1.WorkDuration),
                    LeaveDuration = t.Sum(t.Value.Item1.LeaveDuration),
                    TravelDuration = t.Sum(t.Value.Item1.TravelDuration),
                    OvertimeDuration = t.Sum(t.Value.Item1.OvertimeDuration),
                    ActualDuration = t.Sum(t.Value.Item1.ActualDuration)
                })
                .OrderByDescending(orderExp);

            if (top.HasValue)
                query = query.Take(top.Value);

            return await query.CommandTimeout(120).ToListAsync(ct);
        }

        /// <summary>
        /// 将 "yyyy-MM" 月份字符串解析为 [start, end) 半开区间（end 为下月1号）。
        /// 用日期区间替代 SQL 中的 FORMAT 函数，可命中 WorkDate 索引，避免全表扫描。
        /// </summary>
        private static bool TryGetMonthRange(string? month, out DateTime start, out DateTime end)
        {
            start = default;
            end = default;
            if (string.IsNullOrWhiteSpace(month))
            {
                return false;
            }
            if (DateTime.TryParse(month + "-01", out DateTime first))
            {
                start = new DateTime(first.Year, first.Month, 1);
                end = start.AddMonths(1);
                return true;
            }
            return false;
        }
    }
}
