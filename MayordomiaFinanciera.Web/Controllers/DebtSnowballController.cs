using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.DebtSnowball;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class DebtSnowballController : Controller
    {
        private readonly IDebtSnowballRepository _repository;

        public DebtSnowballController(IDebtSnowballRepository repository)
        {
            _repository = repository;
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
            var viewModel = await _repository.GetUserDebtsAndConfigAsync(userId);
            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveDebt([FromBody] SaveDebtRequest request)
        {
            try
            {
                Guid userId = GetCurrentUserId();
                int debtId = await _repository.SaveOrUpdateDebtAsync(userId, request);
                return Json(new { success = true, message = "¡Deuda guardada exitosamente!", id = debtId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al guardar: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDebt(int id)
        {
            try
            {
                Guid userId = GetCurrentUserId();
                bool deleted = await _repository.DeleteDebtAsync(userId, id);
                return Json(new { success = deleted, message = deleted ? "Deuda eliminada." : "No se encontró la deuda." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al eliminar: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveConfig([FromBody] SaveSnowballConfigRequest request)
        {
            try
            {
                Guid userId = GetCurrentUserId();
                await _repository.SaveConfigAsync(userId, request);
                return Json(new { success = true, message = "Plan de aceleración actualizado." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al guardar configuración: {ex.Message}" });
            }
        }
    }
}