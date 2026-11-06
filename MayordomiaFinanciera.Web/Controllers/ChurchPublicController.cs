using System;
using System.Linq;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [AllowAnonymous]
    public class ChurchPublicController : Controller
    {
        private readonly IChurchRepository _churchRepository;
        private readonly IPaymentVerificationService _paymentVerificationService;

        public ChurchPublicController(
            IChurchRepository churchRepository,
            IPaymentVerificationService paymentVerificationService)
        {
            _churchRepository = churchRepository;
            _paymentVerificationService = paymentVerificationService;
        }

        [HttpGet]
        public async Task<IActionResult> Checkout(string plan = "DEMO_30")
        {
            var model = new RegisterChurchViewModel
            {
                SubscriptionPlan = string.IsNullOrWhiteSpace(plan) ? "DEMO_30" : plan.ToUpper()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessSubscription(
            RegisterChurchViewModel model,
            string paymentMethod,
            string? cardNumber,
            string? cardExpiry,
            string? cardCvc,
            string? cardHolder,
            string? manualReference,
            string? paypalOrderId)
        {
            var globalSettings = await _churchRepository.GetGlobalPaymentSettingsAsync();
            ViewBag.GlobalPaymentSettings = globalSettings;

            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.AdminEmail) || string.IsNullOrWhiteSpace(model.AdminPassword))
            {
                ModelState.AddModelError(string.Empty, "Por favor completa todos los campos obligatorios.");
                return View("Checkout", model);
            }

            string paymentReferenceGenerated = string.Empty;
            string initialSubscriptionStatus = "ACTIVE";

            // 0. MANEJO ESPECIAL PARA EL PLAN DEMO (30 DÍAS GRATIS) - SE VALIDA PRIMERO
            if (model.SubscriptionPlan == "DEMO_30")
            {
                paymentReferenceGenerated = "DEMO-30-TRIAL";
                initialSubscriptionStatus = "trial"; // Asigna el estado de prueba sin requerir pago
            }
            // 1. PROCESAMIENTO Y VERIFICACIÓN REAL DE TARJETA DE CRÉDITO
            else if (paymentMethod == "CREDIT_CARD")
            {
                decimal expectedAmount = model.SubscriptionPlan == "CATHEDRAL" ? 199.00m : (model.SubscriptionPlan == "MINISTERIAL" ? 79.00m : 29.00m);
                var cardResult = await _paymentVerificationService.ProcessCreditCardChargeAsync(
                    cardNumber ?? "", cardExpiry ?? "", cardCvc ?? "", cardHolder ?? "", expectedAmount, globalSettings);

                if (!cardResult.IsSuccess)
                {
                    ModelState.AddModelError(string.Empty, $"Error al procesar el pago con tarjeta: {cardResult.ErrorMessage}");
                    return View("Checkout", model);
                }

                paymentReferenceGenerated = cardResult.TransactionId;
                initialSubscriptionStatus = "ACTIVE";
            }
            // 2. VERIFICACIÓN REAL EN SERVIDORES DE PAYPAL
            else if (paymentMethod == "PAYPAL")
            {
                decimal expectedAmount = model.SubscriptionPlan == "CATHEDRAL" ? 199.00m : (model.SubscriptionPlan == "MINISTERIAL" ? 79.00m : 29.00m);
                if (string.IsNullOrWhiteSpace(paypalOrderId))
                {
                    ModelState.AddModelError(string.Empty, "No se recibió la referencia de PayPal. Por favor autoriza el pago en la ventana de PayPal.");
                    return View("Checkout", model);
                }

                var paypalResult = await _paymentVerificationService.VerifyPaypalOrderAsync(paypalOrderId, expectedAmount, globalSettings);

                if (!paypalResult.IsSuccess)
                {
                    ModelState.AddModelError(string.Empty, $"Verificación de PayPal fallida: {paypalResult.ErrorMessage}");
                    return View("Checkout", model);
                }

                paymentReferenceGenerated = $"PAYPAL-{paypalResult.TransactionId}";
                initialSubscriptionStatus = "ACTIVE";
            }
            // 3. PAGOS MANUALES (ACH Y YAPPY) -> REVISIÓN POR EL ADMINISTRADOR
            else if (paymentMethod == "ACH_TRANSFER" || paymentMethod == "YAPPY")
            {
                string refClean = (manualReference ?? "").Trim();
                if (string.IsNullOrWhiteSpace(refClean) || refClean.Length < 6)
                {
                    ModelState.AddModelError(string.Empty, $"El número de comprobante o referencia de {paymentMethod} debe tener al menos 6 caracteres.");
                    return View("Checkout", model);
                }

                paymentReferenceGenerated = $"{paymentMethod}-{refClean.ToUpper()}";
                initialSubscriptionStatus = "PENDING_VERIFICATION";
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Selecciona un método de pago válido.");
                return View("Checkout", model);
            }

            try
            {
                model.PaymentReference = paymentReferenceGenerated;
                model.CreatedByLanding = true;

                await _churchRepository.RegisterNewChurchAndAdminAsync(model, initialSubscriptionStatus);

                if (initialSubscriptionStatus == "PENDING_VERIFICATION")
                {
                    TempData["SuccessMessage"] = $"¡Registro recibido! Tu comprobante ({paymentReferenceGenerated}) está PENDIENTE DE VERIFICACIÓN por el administrador.";
                }
                else if (initialSubscriptionStatus == "trial")
                {
                    TempData["SuccessMessage"] = "¡Iglesia registrada exitosamente bajo el Plan Demo por 30 días! Ya puedes iniciar sesión.";
                }
                else
                {
                    TempData["SuccessMessage"] = $"¡Pago verificado y confirmado exitosamente! Transacción #{paymentReferenceGenerated}. Ya puedes acceder a la plataforma con tu cuenta pastoral.";
                }

                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Ocurrió un error al registrar la iglesia: {ex.Message}");
                return View("Checkout", model);
            }
        }

        private bool IsValidLuhnNumber(string cardNumber)
        {
            if (string.IsNullOrWhiteSpace(cardNumber) || !cardNumber.All(char.IsDigit) || cardNumber.Length < 13 || cardNumber.Length > 19)
                return false;

            int sum = 0;
            bool alternate = false;
            for (int i = cardNumber.Length - 1; i >= 0; i--)
            {
                int n = int.Parse(cardNumber[i].ToString());
                if (alternate)
                {
                    n *= 2;
                    if (n > 9) n -= 9;
                }
                sum += n;
                alternate = !alternate;
            }
            return (sum % 10 == 0);
        }
    }
}