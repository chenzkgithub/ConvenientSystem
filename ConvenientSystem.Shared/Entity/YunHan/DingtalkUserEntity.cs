using FreeSql.DataAnnotations;

namespace ConvenientSystem.Shared.Entity.YunHan
{
    [Table(Name = "dingtalkuser")]
    public class DingtalkUserEntity
    {
        [Column(IsPrimary = true, DbType = "varchar(50)")]
        public string DDUserId { get; set; }

        [Column(IsPrimary = true, DbType = "varchar(50)")]
        public string corpId { get; set; }

        //[Column(DbType = "varchar(60)")]
        //public string Dingid { get; set; }

        [Column(DbType = "varchar(60)")]
        public string DingCode { get; set; }

        public int Userid { get; set; }

        //[Column(DbType = "nvarchar(10)")]
        //public string Dingnick { get; set; }

        //[Column(DbType = "varchar(60)")]
        //public string Openid { get; set; }

        [Column(DbType = "varchar(100)")]
        public string Unionid { get; set; }

        public DateTime Createtime { get; set; }

        public DateTime? Updatetime { get; set; }

        [Column(DbType = "nvarchar(20)")]
        public string UserName { get; set; }

        [Column(DbType = "nvarchar(20)")]
        public string title { get; set; }

        [Column(DbType = "varchar(200)")]
        public string avatar { get; set; }

        [Column(DbType = "varchar(20)")]
        public string mobile { get; set; }

        [Column(DbType = "nvarchar(50)")]
        public string work_place { get; set; }

        [Column(DbType = "varchar(50)")]
        public string email { get; set; }

        [Column(DbType = "varchar(200)")]
        public string dept_id_list { get; set; }

        private int _DefaultDeptId = 0;

        /// <summary>
        /// 默认部门id,从dept_id_list中取第一个。不用手动赋值
        /// </summary>
        public int DefaultDeptId
        {
            get
            {
                //默认只有一个部门时，就是他的默认部门。多个部门的人员
                if(!string.IsNullOrWhiteSpace(dept_id_list) && dept_id_list.Split(',').Length==1 && dept_id_list.Split(',').FirstOrDefault()!=_DefaultDeptId.ToString())
                {
                    if (int.TryParse(dept_id_list.Split(',').FirstOrDefault(), out int tempDeptIdDeft))
                        _DefaultDeptId = tempDeptIdDeft;
                }
                //多个部门的人员，如果默认组织已经不在归属组织中时，也取一个。
                else if (!string.IsNullOrWhiteSpace(dept_id_list) && _DefaultDeptId>0 && !dept_id_list.Split(',').Contains(_DefaultDeptId.ToString()))
                {
                    if (int.TryParse(dept_id_list.Split(',').FirstOrDefault(), out int tempDeptIdDeft))
                        _DefaultDeptId = tempDeptIdDeft;
                }

                if (_DefaultDeptId > 1)
                    return _DefaultDeptId;

                if (string.IsNullOrWhiteSpace(dept_id_list))
                    return 597959144;

                var firstId = dept_id_list.Split(',').FirstOrDefault();
                if (int.TryParse(firstId, out int tempDeptId))
                    return tempDeptId;

                return 597959144;
            }
            set 
            {
                _DefaultDeptId = value;
            }
        }

        /// <summary>
        /// 入职时间
        /// </summary>
        public DateTime? hired_date { get; set; }

        /// <summary>
        /// 员工工号
        /// </summary>
        [Column(DbType = "varchar(20)")]
        public string job_number { get; set; }

        /// <summary>
        /// 员工直属主管
        /// </summary>
        [Column(DbType = "varchar(50)")]
        public string manager_userid { get; set; }

        /// <summary>
        /// 版本批次号
        /// </summary>
        public long? batchNumber { get; set; }

        /// <summary>
        /// 是否删除
        /// </summary>
        public bool IsDelete { get; set; }

        /// <summary>
        /// 积分
        /// </summary>
        public decimal Integral { get; set; }

        /// <summary>
        /// 员工状态  3.正式 2.实习 空.离职
        /// </summary>
        public int? EmployeeStatus { get; set; }
        
        /// <summary>
        /// 历史批次
        /// </summary>
        public string HistoryBatch { get; set; }
    }
}
