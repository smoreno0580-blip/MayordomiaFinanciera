using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MayordomiaFinanciera.Web.Models.Budget;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MayordomiaFinanciera.Web.Services
{
    public interface IBudgetRepository
    {
        Task<List<CatalogYearDto>> GetActiveYearsAsync();
        Task<List<CatalogMonthDto>> GetActiveMonthsAsync();
        Task<UserBudgetDto?> GetBudgetByUserPeriodAsync(Guid userId, int year, int month);
        Task<int> SaveOrUpdateBudgetAsync(Guid userId, SaveBudgetRequest request);
        Task<List<BudgetComparisonItemDto>> GetMonthlyComparisonAsync(Guid userId, int currentYear, int currentMonth);
    }

    public class BudgetRepository : IBudgetRepository
    {
        private readonly string _connectionString;

        public BudgetRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        public async Task<List<CatalogYearDto>> GetActiveYearsAsync()
        {
            using var connection = CreateConnection();
            string sql = "SELECT year_value AS YearValue, is_active AS IsActive FROM catalog_years WHERE is_active = TRUE ORDER BY year_value DESC;";
            var result = await connection.QueryAsync<CatalogYearDto>(sql);
            return result.ToList();
        }

        public async Task<List<CatalogMonthDto>> GetActiveMonthsAsync()
        {
            using var connection = CreateConnection();
            string sql = "SELECT month_number AS MonthNumber, month_name AS MonthName FROM catalog_months WHERE is_active = TRUE ORDER BY month_number ASC;";
            var result = await connection.QueryAsync<CatalogMonthDto>(sql);
            return result.ToList();
        }

        public async Task<UserBudgetDto?> GetBudgetByUserPeriodAsync(Guid userId, int year, int month)
        {
            using var connection = CreateConnection();
            string budgetSql = @"
                SELECT id, user_id AS UserId, year, month, 
                       gross_income AS GrossIncome, giving_amount AS GivingAmount, 
                       tax_amount AS TaxAmount, net_available_income AS NetAvailableIncome, 
                       total_living_expenses AS TotalLivingExpenses, surplus_deficit AS SurplusDeficit
                FROM user_budgets 
                WHERE user_id = @UserId AND year = @Year AND month = @Month;";

            var budget = await connection.QueryFirstOrDefaultAsync<UserBudgetDto>(budgetSql, new { UserId = userId, Year = year, Month = month });

            if (budget == null) return null;

            string itemsSql = "SELECT item_name, estimated_amount FROM user_budget_items WHERE budget_id = @BudgetId;";
            var items = await connection.QueryAsync(itemsSql, new { BudgetId = budget.Id });

            foreach (var item in items)
            {
                budget.Items[(string)item.item_name] = (decimal)item.estimated_amount;
            }

            return budget;
        }

        public async Task<int> SaveOrUpdateBudgetAsync(Guid userId, SaveBudgetRequest request)
        {
            using var connection = CreateConnection();
            connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                decimal grossIncome = Math.Max(0, request.IncomeEsposa) + Math.Max(0, request.IncomeEsposo) +
                                      Math.Max(0, request.IncomeNegocios) + Math.Max(0, request.IncomeOtros);

                decimal giving = Math.Max(0, request.Giving);
                decimal taxes = Math.Max(0, request.Taxes);
                decimal netAvailable = grossIncome - giving - taxes;

                decimal livingExpenses = Math.Max(0, request.ViviendaHipoteca) + Math.Max(0, request.ViviendaSeguro) +
                                         Math.Max(0, request.ViviendaImpuestos) + Math.Max(0, request.ViviendaServicios) +
                                         Math.Max(0, request.ViviendaTelecom) + Math.Max(0, request.ViviendaMantenimiento) +
                                         Math.Max(0, request.ComidaTotal) + Math.Max(0, request.TransporteTotal) +
                                         Math.Max(0, request.SegurosTotal) + Math.Max(0, request.DeudasTarjetas) +
                                         Math.Max(0, request.DeudasPrestamo) + Math.Max(0, request.DeudasOtro) +
                                         Math.Max(0, request.EntretenimientoTotal) + Math.Max(0, request.RopaTotal) +
                                         Math.Max(0, request.AhorrosTotal) + Math.Max(0, request.MedicosTotal) +
                                         Math.Max(0, request.MiscelaneosTotal) + Math.Max(0, request.InversionesTotal) +
                                         Math.Max(0, request.EscuelaTotal);

                decimal surplusDeficit = netAvailable - livingExpenses;

                string upsertSql = @"
                    INSERT INTO user_budgets (user_id, year, month, gross_income, giving_amount, tax_amount, net_available_income, total_living_expenses, surplus_deficit, updated_at)
                    VALUES (@UserId, @Year, @Month, @GrossIncome, @GivingAmount, @TaxAmount, @NetAvailableIncome, @TotalLivingExpenses, @SurplusDeficit, CURRENT_TIMESTAMP)
                    ON CONFLICT (user_id, year, month) 
                    DO UPDATE SET 
                        gross_income = EXCLUDED.gross_income,
                        giving_amount = EXCLUDED.giving_amount,
                        tax_amount = EXCLUDED.tax_amount,
                        net_available_income = EXCLUDED.net_available_income,
                        total_living_expenses = EXCLUDED.total_living_expenses,
                        surplus_deficit = EXCLUDED.surplus_deficit,
                        updated_at = CURRENT_TIMESTAMP
                    RETURNING id;";

                int budgetId = await connection.ExecuteScalarAsync<int>(upsertSql, new
                {
                    UserId = userId,
                    Year = request.Year,
                    Month = request.Month,
                    GrossIncome = grossIncome,
                    GivingAmount = giving,
                    TaxAmount = taxes,
                    NetAvailableIncome = netAvailable,
                    TotalLivingExpenses = livingExpenses,
                    SurplusDeficit = surplusDeficit
                }, transaction);

                await connection.ExecuteAsync("DELETE FROM user_budget_items WHERE budget_id = @BudgetId;", new { BudgetId = budgetId }, transaction);

                var itemsToInsert = new List<dynamic>
                {
                    new { BudgetId = budgetId, Section = "INCOME", CatId = 0, CatName = "Ingreso", Name = "incomeEsposa", Amount = Math.Max(0, request.IncomeEsposa) },
                    new { BudgetId = budgetId, Section = "INCOME", CatId = 0, CatName = "Ingreso", Name = "incomeEsposo", Amount = Math.Max(0, request.IncomeEsposo) },
                    new { BudgetId = budgetId, Section = "INCOME", CatId = 0, CatName = "Ingreso", Name = "incomeNegocios", Amount = Math.Max(0, request.IncomeNegocios) },
                    new { BudgetId = budgetId, Section = "INCOME", CatId = 0, CatName = "Ingreso", Name = "incomeOtros", Amount = Math.Max(0, request.IncomeOtros) },

                    new { BudgetId = budgetId, Section = "GIVING", CatId = 1, CatName = "1. Dar", Name = "giving", Amount = Math.Max(0, request.Giving) },
                    new { BudgetId = budgetId, Section = "TAX", CatId = 2, CatName = "2. Impuestos", Name = "taxes", Amount = Math.Max(0, request.Taxes) },

                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaHipoteca", Amount = Math.Max(0, request.ViviendaHipoteca) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaSeguro", Amount = Math.Max(0, request.ViviendaSeguro) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaImpuestos", Amount = Math.Max(0, request.ViviendaImpuestos) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaServicios", Amount = Math.Max(0, request.ViviendaServicios) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaTelecom", Amount = Math.Max(0, request.ViviendaTelecom) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 3, CatName = "3. Vivienda", Name = "viviendaMantenimiento", Amount = Math.Max(0, request.ViviendaMantenimiento) },

                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 4, CatName = "4. Comida", Name = "comidaTotal", Amount = Math.Max(0, request.ComidaTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 5, CatName = "5. Transporte", Name = "transporteTotal", Amount = Math.Max(0, request.TransporteTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 6, CatName = "6. Seguros", Name = "segurosTotal", Amount = Math.Max(0, request.SegurosTotal) },

                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 7, CatName = "7. Deudas", Name = "deudasTarjetas", Amount = Math.Max(0, request.DeudasTarjetas) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 7, CatName = "7. Deudas", Name = "deudasPrestamo", Amount = Math.Max(0, request.DeudasPrestamo) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 7, CatName = "7. Deudas", Name = "deudasOtro", Amount = Math.Max(0, request.DeudasOtro) },

                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 8, CatName = "8. Entretenimiento", Name = "entretenimientoTotal", Amount = Math.Max(0, request.EntretenimientoTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 9, CatName = "9. Ropa", Name = "ropaTotal", Amount = Math.Max(0, request.RopaTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 10, CatName = "10. Ahorros", Name = "ahorrosTotal", Amount = Math.Max(0, request.AhorrosTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 11, CatName = "11. Gastos Médicos", Name = "medicosTotal", Amount = Math.Max(0, request.MedicosTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 12, CatName = "12. Misceláneos", Name = "miscelaneosTotal", Amount = Math.Max(0, request.MiscelaneosTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 13, CatName = "13. Inversiones", Name = "inversionesTotal", Amount = Math.Max(0, request.InversionesTotal) },
                    new { BudgetId = budgetId, Section = "EXPENSE", CatId = 14, CatName = "14. Escuela", Name = "escuelaTotal", Amount = Math.Max(0, request.EscuelaTotal) }
                };

                string insertItemSql = @"
                    INSERT INTO user_budget_items (budget_id, section_code, category_id, category_name, item_name, estimated_amount)
                    VALUES (@BudgetId, @Section, @CatId, @CatName, @Name, @Amount);";

                await connection.ExecuteAsync(insertItemSql, itemsToInsert, transaction);

                transaction.Commit();
                return budgetId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<List<BudgetComparisonItemDto>> GetMonthlyComparisonAsync(Guid userId, int currentYear, int currentMonth)
        {
            int prevYear = currentMonth == 1 ? currentYear - 1 : currentYear;
            int prevMonth = currentMonth == 1 ? 12 : currentMonth - 1;

            var currBudget = await GetBudgetByUserPeriodAsync(userId, currentYear, currentMonth);
            var prevBudget = await GetBudgetByUserPeriodAsync(userId, prevYear, prevMonth);

            // Mapeo de identificadores técnicos a nombres legibles para la UI
            var categoryMapping = new List<(string KeyPrefix, string DisplayName)>
    {
        ("vivienda", "3. Vivienda"),
        ("comidaTotal", "4. Comida"),
        ("transporteTotal", "5. Transporte"),
        ("segurosTotal", "6. Seguros"),
        ("deudas", "7. Deudas"),
        ("entretenimientoTotal", "8. Entretenimiento"),
        ("ropaTotal", "9. Ropa"),
        ("ahorrosTotal", "10. Ahorros"),
        ("medicosTotal", "11. Gastos Médicos"),
        ("miscelaneosTotal", "12. Misceláneos"),
        ("inversionesTotal", "13. Inversiones"),
        ("escuelaTotal", "14. Escuela")
    };

            var comparisons = new List<BudgetComparisonItemDto>();

            foreach (var cat in categoryMapping)
            {
                decimal currentAmt = currBudget?.Items.Where(k => k.Key.StartsWith(cat.KeyPrefix, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Value) ?? 0;
                decimal prevAmt = prevBudget?.Items.Where(k => k.Key.StartsWith(cat.KeyPrefix, StringComparison.OrdinalIgnoreCase)).Sum(x => x.Value) ?? 0;

                decimal varAmt = currentAmt - prevAmt;
                decimal varPct = prevAmt > 0 ? (varAmt / prevAmt) * 100 : (currentAmt > 0 ? 100 : 0);

                comparisons.Add(new BudgetComparisonItemDto
                {
                    CategoryName = cat.DisplayName,
                    PreviousMonthAmount = prevAmt,
                    CurrentMonthAmount = currentAmt,
                    VariationAmount = varAmt,
                    VariationPercentage = Math.Round(varPct, 2),
                    IsAlert = varPct > 10
                });
            }

            return comparisons;
        }
    }
}