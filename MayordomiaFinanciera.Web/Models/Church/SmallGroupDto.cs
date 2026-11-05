namespace MayordomiaFinanciera.Web.Models.Church
{
    public class SmallGroupDto
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? MeetingDay { get; set; }
        public TimeSpan? MeetingTime { get; set; }
        public string? Location { get; set; }
        public Guid? LeaderId { get; set; }
        public string? LeaderName { get; set; }
        public bool IsActive { get; set; } = true;

        // Propiedad auxiliar para reportes recientes
        public int TotalReports { get; set; }
    }

    public class GroupAttendanceDto
    {
        public Guid Id { get; set; }
        public Guid GroupId { get; set; }
        public Guid ChurchId { get; set; }
        public DateTime MeetingDate { get; set; } = DateTime.Today;
        public int AttendeeCount { get; set; }
        public string? Notes { get; set; }
        public string? ReportedBy { get; set; }
    }
}