using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Budget;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class BudgetController : Controller
    {
        private readonly IBudgetRepository _budgetRepository;
        private readonly ISubscriptionPolicyService _subscriptionPolicy;

        public BudgetController(IBudgetRepository budgetRepository, ISubscriptionPolicyService subscriptionPolicy)
        {
            _budgetRepository = budgetRepository;
            _subscriptionPolicy = subscriptionPolicy;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId))
            {
                return userId;
            }
            throw new UnauthorizedAccessException("Usuario no autenticado o ID no válido.");
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? year, int? month)
        {
            Guid userId = GetCurrentUserId();
            int selectedYear = year ?? DateTime.Now.Year;
            int selectedMonth = month ?? DateTime.Now.Month;

            // Verificar si el usuario tiene acceso a funciones avanzadas (B2B o pago)
            bool isAdvancedAllowed = await _subscriptionPolicy.CanAccessAdvancedFeaturesAsync(userId);
            ViewData["IsAdvancedAllowed"] = isAdvancedAllowed;

            var viewModel = new BudgetViewModels
            {
                SelectedYear = selectedYear,
                SelectedMonth = selectedMonth,
                AvailableYears = await _budgetRepository.GetActiveYearsAsync(),
                AvailableMonths = await _budgetRepository.GetActiveMonthsAsync(),
                Budget = await _budgetRepository.GetBudgetByUserPeriodAsync(userId, selectedYear, selectedMonth) ?? new UserBudgetDto()
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save([FromBody] SaveBudgetRequest request)
        {
            try
            {
                Guid userId = GetCurrentUserId();
                int budgetId = await _budgetRepository.SaveOrUpdateBudgetAsync(userId, request);
                return Json(new { success = true, message = "¡Presupuesto guardado exitosamente!", id = budgetId });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al guardar: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetComparison(int year, int month)
        {
            Guid userId = GetCurrentUserId();

            // Validar límites del Plan Gratuito B2C
            bool isAllowed = await _subscriptionPolicy.CanAccessAdvancedFeaturesAsync(userId);
            if (!isAllowed)
            {
                return Json(new
                {
                    success = false,
                    isRestricted = true,
                    message = "El comparativo histórico avanzado requiere pertenecer a una iglesia con suscripción activa."
                });
            }

            var comparison = await _budgetRepository.GetMonthlyComparisonAsync(userId, year, month);
            return Json(new { success = true, data = comparison });
        }
    }
}