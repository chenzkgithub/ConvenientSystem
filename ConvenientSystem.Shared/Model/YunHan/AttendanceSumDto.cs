namespace ConvenientSystem.Shared.Model.YunHan
{
    using System.ComponentModel.DataAnnotations.Schema;
    using System.Text.Json.Serialization;

    public class AttendanceSumDto
    {
        public string avatar { get; set; }
        [JsonPropertyName("ddUserId")]
        public string DDUserId { get; set; }
        public string UserName { get; set; }
        public int? employeeStatus { get; set; }
        

        public string HiredDate { get; set; }
        public string deptId { get; set; }
        public string deptName { get; set; }
        /// <summary>部门全称（如"南昌分公司-运营部-拼多多组"）</summary>
        public string fullDeptName { get; set; }

        public decimal WorkDuration { get; set; }
        public decimal LeaveDuration { get; set; }
        public decimal TravelDuration { get; set; }
        public decimal OvertimeDuration { get; set; }
        public decimal ActualDuration { get; set; } 
        
    }

}
