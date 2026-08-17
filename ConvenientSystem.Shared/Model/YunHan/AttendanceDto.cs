namespace ConvenientSystem.Shared.Model.YunHan
{
    using System.ComponentModel.DataAnnotations.Schema;

    public class AttendanceDto
    {
        public string avatar { get; set; }
        public string full_code { get; set; }

        public string deptName { get; set; }

        public string UserName { get; set; }

        public string HiredDate { get; set; }

        public decimal WorkDuration { get; set; }
        public decimal LeaveDuration { get; set; }
        public decimal TravelDuration { get; set; }
        public decimal OvertimeDuration { get; set; }
        public decimal ActualDuration { get; set; }
        public string workTime { get; set; }
        public string restTime { get; set; }
        public string WorkDate { get; set; }
        public int? employeeStatus { get; set; }
        
    }

}
