using FreeSql.DataAnnotations;
using System.Runtime.InteropServices;

namespace ConvenientSystem.Shared.Entity.YunHan
{
    /// <summary>
    /// 考勤信息
    /// </summary>
    [Table(Name = "bu_attendance")]
    public class BuAttendanceEntity
    {

        [Column(IsPrimary = true)]
        public DateTime WorkDate { get; set; }

        [Column(DbType = "varchar(50)", IsPrimary = true)]
        public string corpId { get; set; }

        [Column(DbType = "varchar(50)", IsPrimary = true)]
        public string UserId { get; set; }

        [Column(DbType = "decimal(3, 1)")]
        public decimal WorkDuration { get; set; }

        /// <summary>
        /// 工作时间段 09:00-12:00,13:00-18:00
        /// </summary>
        [Column(DbType = "varchar(100)")]
        public string WorkTime { get; set; }

        /// <summary>
        /// 休息时间段 09:00-12:00,13:00-18:00
        /// </summary>
        [Column(DbType = "varchar(100)")]
        public string RestTime { get; set; }

        /// <summary>
        /// 同步时间
        /// </summary>
        public DateTime? CreateTime { get; set; }

        /// <summary>
        /// 班次内休息时间段 09:00-12:00,13:00-18:00
        /// </summary>
        [Column(DbType = "varchar(100)")]
        public string ClassTimes { get; set; }

        /// <summary>
        /// 加班时长
        /// </summary>
        [Column(DbType = "decimal(3, 1)")]
        public decimal OvertimeDuration { get; set; }

        /// <summary>
        /// 出差/外出时长
        /// </summary>
        [Column(DbType = "decimal(3, 1)")]
        public decimal TravelDuration { get; set; }

        /// <summary>
        /// 请假时长
        /// </summary>
        [Column(DbType = "decimal(3, 1)")]
        public decimal LeaveDuration { get; set; }

        /// <summary>
        /// 实际工作时长
        /// </summary>
        [Column(DbType = "decimal(3, 1)")]
        public decimal ActualDuration { get; set; }
    }
}
