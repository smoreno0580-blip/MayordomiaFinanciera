using System;

namespace MayordomiaFinanciera.Web.Models.Church
{
    public class ChurchDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? LogoUrl { get; set; }
        public string PrimaryColor { get; set; } = "#1B365D";
        public string SecondaryColor { get; set; } = "#00A8CC";
        public string SubscriptionPlan { get; set; } = "SEED";
        public string SubscriptionStatus { get; set; } = "ACTIVE";
        public int MaxMembers { get; set; }
        public string? PaymentReference { get; set; }
    }

    // Configuración Personalizada de Datos de Pago Eclesiales
    public class ChurchPaymentSettingsDto
    {
        public Guid Id { get; set; }
        public Guid ChurchId { get; set; }

        // Datos ACH / Transferencia
        public string AchBankName { get; set; } = "Banco General";
        public string AchAccountType { get; set; } = "Cuenta Corriente";
        public string AchAccountNumber { get; set; } = string.Empty;
        public string AchAccountHolder { get; set; } = string.Empty;

        // Datos Yappy
        public string YappyPhone { get; set; } = string.Empty;
        public string YappyAlias { get; set; } = string.Empty;

        // Datos PayPal
        public string PaypalEmail { get; set; } = string.Empty;

        // Flags de Activación
        public bool IsAchEnabled { get; set; } = true;
        public bool IsYappyEnabled { get; set; } = true;
        public bool IsPaypalEnabled { get; set; } = true;
    }

    public class JoinChurchRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public class CreateChurchInvitationRequest
    {
        public string Code { get; set; } = string.Empty;
    }

    public class GlobalPaymentSettingsDto
    {
        public Guid Id { get; set; }
        public string SaasAchBankName { get; set; } = string.Empty;
        public string SaasAchAccountType { get; set; } = string.Empty;
        public string SaasAchAccountNumber { get; set; } = string.Empty;
        public string SaasAchAccountHolder { get; set; } = string.Empty;
        public string SaasYappyAlias { get; set; } = string.Empty;
        public string SaasPaypalEmail { get; set; } = string.Empty;

        // Credenciales de API para Verificación Real en Servidor
        public string SaasPaypalClientId { get; set; } = "test";
        public string SaasPaypalSecret { get; set; } = string.Empty; // Secret Key de PayPal
        public string SaasPaypalMode { get; set; } = "sandbox"; // "sandbox" o "live"

        public string SaasStripeSecretKey { get; set; } = string.Empty; // Secret Key de Pasarela Tarjeta
    }
}