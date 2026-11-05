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
    public class ChurchController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public ChurchController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinByCode([FromBody] JoinChurchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Json(new { success = false, message = "Por favor ingrese un código de invitación válido." });
            }

            try
            {
                Guid userId = GetCurrentUserId();
                bool linked = await _churchRepository.LinkUserToChurchByCodeAsync(userId, request.Code);

                if (linked)
                {
                    return Json(new { success = true, message = "¡Te has vinculado exitosamente a tu iglesia! Se han desbloqueado tus beneficios." });
                }

                return Json(new { success = false, message = "El código ingresado no existe o no se encuentra activo." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al vincular: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveChurch()
        {
            try
            {
                Guid userId = GetCurrentUserId();
                await _churchRepository.UnlinkUserFromChurchAsync(userId);
                return Json(new { success = true, message = "Has desvinculado tu cuenta de la iglesia." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al desvincular: {ex.Message}" });
            }
        }
    }
}