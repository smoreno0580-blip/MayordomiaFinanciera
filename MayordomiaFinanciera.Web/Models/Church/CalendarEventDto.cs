using System;
using System.ComponentModel.DataAnnotations;

namespace MayordomiaFinanciera.Web.Models.Church
{
    public class CalendarEventDto
    {
        public Guid? Id { get; set; }        // 👈 Cambiado a Guid? (nullable) para aceptar cadenas vacías al crear
        public Guid? ChurchId { get; set; }   // 👈 Cambiado a Guid? (nullable)

        [Required(ErrorMessage = "El título del evento es obligatorio.")]
        [StringLength(150, ErrorMessage = "El título no puede superar los 150 caracteres.")]
        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required(ErrorMessage = "La categoría es obligatoria.")]
        public string Category { get; set; } = "General";

        [Required(ErrorMessage = "La fecha y hora de inicio son obligatorias.")]
        public DateTime StartDate { get; set; } = DateTime.Now;

        [Required(ErrorMessage = "La fecha y hora de fin son obligatorias.")]
        public DateTime EndDate { get; set; } = DateTime.Now.AddHours(1.5);

        public string? Location { get; set; }
        public bool IsVirtual { get; set; }
        public string? MeetingLink { get; set; }

        public Guid? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}