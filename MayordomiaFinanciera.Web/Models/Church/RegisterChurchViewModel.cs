using System.ComponentModel.DataAnnotations;

namespace MayordomiaFinanciera.Web.Models.Church
{
    public class RegisterChurchViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
        public string AdminPassword { get; set; } = string.Empty;

        // Planes soportados: DEMO_30, SEED, MINISTERIAL, CATHEDRAL
        public string SubscriptionPlan { get; set; } = "DEMO_30";

        // Propiedades de Pago y Registro Landing
        public string? PaymentReference { get; set; }
        public string? GatewaySubscriptionId { get; set; }
        public bool CreatedByLanding { get; set; } = false;
    }
}