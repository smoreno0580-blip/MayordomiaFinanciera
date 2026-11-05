using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class ChurchAdminController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public ChurchAdminController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid userId = GetCurrentUserId();
            var church = await _churchRepository.GetChurchByUserIdAsync(userId);

            if (church == null)
            {
                TempData["ErrorMessage"] = "No tienes una iglesia o ministerio asociado a tu cuenta.";
                return RedirectToAction("Index", "Dashboard");
            }

            var members = await _churchRepository.GetMembersByChurchIdAsync(church.Id);
            var invitationCodes = await _churchRepository.GetInvitationCodesByChurchIdAsync(church.Id);

            ViewBag.Members = members;
            ViewBag.InvitationCodes = invitationCodes;

            return View(church);
        }

        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,teacher,TEACHER")]
        [HttpGet]
        public async Task<IActionResult> PaymentSettings()
        {
            Guid churchId = GetCurrentChurchId(); // Método helper que obtiene la iglesia del Claim actual
            var settings = await _churchRepository.GetPaymentSettingsByChurchIdAsync(churchId);
            return View(settings ?? new ChurchPaymentSettingsDto { ChurchId = churchId });
        }

        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,teacher,TEACHER")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PaymentSettings(ChurchPaymentSettingsDto model)
        {
            Guid churchId = GetCurrentChurchId();
            model.ChurchId = churchId;

            await _churchRepository.SavePaymentSettingsAsync(model);
            TempData["SuccessMessage"] = "Tus datos de cuenta bancaria, Yappy y PayPal fueron actualizados exitosamente para las donaciones de tu congregación.";
            return View(model);
        }

        private Guid GetCurrentChurchId()
        {
            // 1. Intentar leer desde la Claim de la cookie de autenticación
            var churchClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;

            if (!string.IsNullOrEmpty(churchClaim) && Guid.TryParse(churchClaim, out Guid churchId))
            {
                return churchId;
            }

            // 2. Si no viene en los claims pero el usuario es admin o se almacena en el ID de usuario:
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrEmpty(userIdClaim) && Guid.TryParse(userIdClaim, out Guid parsedUserId))
            {
                // De ser necesario, se puede hacer fallback o retornar Guid por defecto
                return parsedUserId;
            }

            throw new InvalidOperationException("No se encontró la iglesia asociada al usuario autenticado.");
        }
        
    }
}