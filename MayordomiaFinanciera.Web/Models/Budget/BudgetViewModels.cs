using System;
using System.Collections.Generic;

namespace MayordomiaFinanciera.Web.Models.Budget
{
    public class BudgetViewModels
    {
        public int SelectedYear { get; set; }
        public int SelectedMonth { get; set; }

        public List<CatalogYearDto> AvailableYears { get; set; } = new List<CatalogYearDto>();
        public List<CatalogMonthDto> AvailableMonths { get; set; } = new List<CatalogMonthDto>();

        public UserBudgetDto Budget { get; set; } = new UserBudgetDto();
    }

    public class CatalogYearDto
    {
        public int YearValue { get; set; }
        public bool IsActive { get; set; }
    }

    public class CatalogMonthDto
    {
        public int MonthNumber { get; set; }
        public string MonthName { get; set; } = string.Empty;
    }

    public class UserBudgetDto
    {
        public int Id { get; set; }
        public Guid UserId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }

        public decimal GrossIncome { get; set; }
        public decimal GivingAmount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal NetAvailableIncome { get; set; }
        public decimal TotalLivingExpenses { get; set; }
        public decimal SurplusDeficit { get; set; }

        public Dictionary<string, decimal> Items { get; set; } = new Dictionary<string, decimal>();
    }

    public class SaveBudgetRequest
    {
        public int Year { get; set; }
        public int Month { get; set; }

        // Ingresos
        public decimal IncomeEsposa { get; set; }
        public decimal IncomeEsposo { get; set; }
        public decimal IncomeNegocios { get; set; }
        public decimal IncomeOtros { get; set; }

        // Menos
        public decimal Giving { get; set; }
        public decimal Taxes { get; set; }

        // 3. Vivienda
        public decimal ViviendaHipoteca { get; set; }
        public decimal ViviendaSeguro { get; set; }
        public decimal ViviendaImpuestos { get; set; }
        public decimal ViviendaServicios { get; set; }
        public decimal ViviendaTelecom { get; set; }
        public decimal ViviendaMantenimiento { get; set; }

        // 4 a 6
        public decimal ComidaTotal { get; set; }
        public decimal TransporteTotal { get; set; }
        public decimal SegurosTotal { get; set; }

        // 7. Deudas
        public decimal DeudasTarjetas { get; set; }
        public decimal DeudasPrestamo { get; set; }
        public decimal DeudasOtro { get; set; }

        // 8 a 14
        public decimal EntretenimientoTotal { get; set; }
        public decimal RopaTotal { get; set; }
        public decimal AhorrosTotal { get; set; }
        public decimal MedicosTotal { get; set; }
        public decimal MiscelaneosTotal { get; set; }
        public decimal InversionesTotal { get; set; }
        public decimal EscuelaTotal { get; set; }
    }

    public class BudgetComparisonItemDto
    {
        public string CategoryName { get; set; } = string.Empty;
        public decimal PreviousMonthAmount { get; set; }
        public decimal CurrentMonthAmount { get; set; }
        public decimal VariationAmount { get; set; }
        public decimal VariationPercentage { get; set; }
        public bool IsAlert { get; set; }
    }
}