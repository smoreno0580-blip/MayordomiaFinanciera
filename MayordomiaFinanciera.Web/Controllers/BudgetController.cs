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

        public BudgetController(IBudgetRepository budgetRepository)
        {
            _budgetRepository = budgetRepository;
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
            var comparison = await _budgetRepository.GetMonthlyComparisonAsync(userId, year, month);
            return Json(new { success = true, data = comparison });
        }
    }
}