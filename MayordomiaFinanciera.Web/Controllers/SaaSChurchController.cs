using System;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize(Roles = "admin,ADMIN")]
    public class SaaSChurchController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public SaaSChurchController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Register(string plan = "DEMO_30")
        {
            ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();

            // Recibimos el parámetro de la landing page y lo asignamos a SubscriptionPlan
            var model = new RegisterChurchViewModel
            {
                SubscriptionPlan = string.IsNullOrWhiteSpace(plan) ? "DEMO_30" : plan.ToUpper()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterChurchViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SubscriptionPlan))
            {
                model.SubscriptionPlan = "DEMO_30";
            }

            if (!ModelState.IsValid)
            {
                ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();
                return View(model);
            }

            try
            {
                await _churchRepository.RegisterNewChurchAndAdminAsync(model);

                if (model.SubscriptionPlan == "DEMO_30")
                {
                    TempData["SuccessMessage"] = "¡Iglesia registrada exitosamente bajo el Plan Demo por 30 días! Ya puedes iniciar sesión.";
                }
                else
                {
                    TempData["SuccessMessage"] = "¡Iglesia registrada exitosamente! Pendiente de activación por pago.";
                }

                return RedirectToAction("Register");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error al guardar: {ex.Message}");
                ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();
                return View(model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateChurch(Guid churchId)
        {
            try
            {
                await _churchRepository.ActivateChurchAsync(churchId);
                TempData["SuccessMessage"] = "¡La iglesia ha sido activada exitosamente! El pastor ya puede iniciar sesión en la plataforma.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al activar la iglesia: {ex.Message}";
            }

            return RedirectToAction("Register");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPaymentAndActivate(Guid churchId, string newPlan)
        {
            try
            {
                // Método para actualizar el plan, limpiar la fecha de trial, marcar activo y remover la baja por vencimiento
                await _churchRepository.ConfirmPaymentAndActivateAsync(churchId, newPlan);
                TempData["SuccessMessage"] = "¡Pago validado y suscripción activada exitosamente con el nuevo plan!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al confirmar el pago y activar la iglesia: {ex.Message}";
            }

            return RedirectToAction("Register");
        }
    }
}