namespace MayordomiaFinanciera.Web.Models.Church
{
    public class VisitorPipelineDto
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string Stage { get; set; } = "Bienvenida"; // Bienvenida, Crecimiento, Consolidado, Servidor
        public string? AssignedTo { get; set; }
        public string? Notes { get; set; }
        public DateTime VisitDate { get; set; } = DateTime.Today;
    }
}