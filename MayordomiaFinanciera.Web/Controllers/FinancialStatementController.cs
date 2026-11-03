using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.FinancialStatement;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class FinancialStatementController : Controller
    {
        private readonly IFinancialStatementRepository _repository;

        public FinancialStatementController(IFinancialStatementRepository repository)
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
            var model = await _repository.GetLatestStatementAsync(userId) ?? new FinancialStatementViewModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] SaveFinancialStatementRequest request)
        {
            try
            {
                Guid userId = GetCurrentUserId();
                int id = await _repository.SaveOrUpdateStatementAsync(userId, request);
                return Json(new { success = true, message = "¡Estado Financiero guardado exitosamente!", id = id });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al guardar: {ex.Message}" });
            }
        }
    }
}