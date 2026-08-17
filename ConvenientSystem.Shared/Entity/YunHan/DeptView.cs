using FreeSql.DataAnnotations;
namespace ConvenientSystem.Shared.Entity.YunHan
{
    /// <summary>
    /// 部门视图
    /// 排序建议使用：order by (case when full_code like '153322298%' then 1 else 0 end), full_code;
    /// </summary> 
    [Table(Name = "DeptView")]
    public class DeptView
    {

        /// <summary>
        /// 部门id
        /// </summary>
        [Column(IsPrimary = true)]
        public int dept_id { get; set; }


        /// <summary>
        /// 企业ID
        /// </summary>
        [Column(StringLength = 100)]
        public string corpId { get; set; }

        /// <summary>
        /// 企业名称
        /// </summary>
        [Column(StringLength = 100)]
        public string corpName { get; set; }

        /// <summary>
        /// 部门名称
        /// </summary>
        [Column(StringLength = 100)]
        public string dept_name { get; set; }

        /// <summary>
        /// 上级部门id
        /// </summary>
        public int parent_id { get; set; }

        /// <summary>
        /// 部门全名  南昌分公司-运营部-拼多多组***
        /// </summary>
        [Column(StringLength = 1000)]
        public string full_name { get; set; }

        /// <summary>
        /// 部门id组成的编码 599262021.503495368.503561371
        /// </summary>
        [Column(StringLength = 1000)]
        public string full_code { get; set; }
        /// <summary>
        ///删除状态判断
        /// </summary>
        public bool deptIsDel { get; set; }

        /// <summary>
        /// 层级
        /// </summary>
        public int lvl { get; set; }
    }
}
