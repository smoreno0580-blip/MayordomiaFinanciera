using System;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using MayordomiaFinanciera.Web.Models.FinancialStatement;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MayordomiaFinanciera.Web.Services
{
    public interface IFinancialStatementRepository
    {
        Task<FinancialStatementViewModel?> GetLatestStatementAsync(Guid userId);
        Task<int> SaveOrUpdateStatementAsync(Guid userId, SaveFinancialStatementRequest request);
    }

    public class FinancialStatementRepository : IFinancialStatementRepository
    {
        private readonly string _connectionString;

        public FinancialStatementRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        public async Task<FinancialStatementViewModel?> GetLatestStatementAsync(Guid userId)
        {
            using var connection = CreateConnection();
            string sql = @"
                SELECT id, statement_date AS StatementDate,
                       liquid_cash AS LiquidCash, liquid_savings AS LiquidSavings, liquid_other AS LiquidOther,
                       real_estate_primary_home AS RealEstatePrimaryHome, real_estate_other AS RealEstateOther,
                       investments_retirement AS InvestmentsRetirement, investments_stocks AS InvestmentsStocks,
                       vehicles_value AS VehiclesValue, personal_property_value AS PersonalPropertyValue,
                       mortgage_balance AS MortgageBalance, car_loans_balance AS CarLoansBalance,
                       credit_cards_balance AS CreditCardsBalance, personal_loans_balance AS PersonalLoansBalance,
                       other_debts_balance AS OtherDebtsBalance,
                       total_assets AS TotalAssets, total_liabilities AS TotalLiabilities, net_worth AS NetWorth
                FROM user_financial_statements
                WHERE user_id = @UserId
                ORDER BY statement_date DESC
                LIMIT 1;";

            return await connection.QueryFirstOrDefaultAsync<FinancialStatementViewModel>(sql, new { UserId = userId });
        }

        public async Task<int> SaveOrUpdateStatementAsync(Guid userId, SaveFinancialStatementRequest req)
        {
            using var connection = CreateConnection();

            decimal totalAssets = Math.Max(0, req.LiquidCash) + Math.Max(0, req.LiquidSavings) + Math.Max(0, req.LiquidOther) +
                                 Math.Max(0, req.RealEstatePrimaryHome) + Math.Max(0, req.RealEstateOther) +
                                 Math.Max(0, req.InvestmentsRetirement) + Math.Max(0, req.InvestmentsStocks) +
                                 Math.Max(0, req.VehiclesValue) + Math.Max(0, req.PersonalPropertyValue);

            decimal totalLiabilities = Math.Max(0, req.MortgageBalance) + Math.Max(0, req.CarLoansBalance) +
                                      Math.Max(0, req.CreditCardsBalance) + Math.Max(0, req.PersonalLoansBalance) +
                                      Math.Max(0, req.OtherDebtsBalance);

            decimal netWorth = totalAssets - totalLiabilities;

            string sql = @"
                INSERT INTO user_financial_statements (
                    user_id, statement_date, liquid_cash, liquid_savings, liquid_other,
                    real_estate_primary_home, real_estate_other, investments_retirement, investments_stocks,
                    vehicles_value, personal_property_value, mortgage_balance, car_loans_balance,
                    credit_cards_balance, personal_loans_balance, other_debts_balance,
                    total_assets, total_liabilities, net_worth, updated_at
                )
                VALUES (
                    @UserId, CURRENT_DATE, @LiquidCash, @LiquidSavings, @LiquidOther,
                    @RealEstatePrimaryHome, @RealEstateOther, @InvestmentsRetirement, @InvestmentsStocks,
                    @VehiclesValue, @PersonalPropertyValue, @MortgageBalance, @CarLoansBalance,
                    @CreditCardsBalance, @PersonalLoansBalance, @OtherDebtsBalance,
                    @TotalAssets, @TotalLiabilities, @NetWorth, CURRENT_TIMESTAMP
                )
                ON CONFLICT (user_id, statement_date)
                DO UPDATE SET
                    liquid_cash = EXCLUDED.liquid_cash,
                    liquid_savings = EXCLUDED.liquid_savings,
                    liquid_other = EXCLUDED.liquid_other,
                    real_estate_primary_home = EXCLUDED.real_estate_primary_home,
                    real_estate_other = EXCLUDED.real_estate_other,
                    investments_retirement = EXCLUDED.investments_retirement,
                    investments_stocks = EXCLUDED.investments_stocks,
                    vehicles_value = EXCLUDED.vehicles_value,
                    personal_property_value = EXCLUDED.personal_property_value,
                    mortgage_balance = EXCLUDED.mortgage_balance,
                    car_loans_balance = EXCLUDED.car_loans_balance,
                    credit_cards_balance = EXCLUDED.credit_cards_balance,
                    personal_loans_balance = EXCLUDED.personal_loans_balance,
                    other_debts_balance = EXCLUDED.other_debts_balance,
                    total_assets = EXCLUDED.total_assets,
                    total_liabilities = EXCLUDED.total_liabilities,
                    net_worth = EXCLUDED.net_worth,
                    updated_at = CURRENT_TIMESTAMP
                RETURNING id;";

            return await connection.ExecuteScalarAsync<int>(sql, new
            {
                UserId = userId,
                LiquidCash = Math.Max(0, req.LiquidCash),
                LiquidSavings = Math.Max(0, req.LiquidSavings),
                LiquidOther = Math.Max(0, req.LiquidOther),
                RealEstatePrimaryHome = Math.Max(0, req.RealEstatePrimaryHome),
                RealEstateOther = Math.Max(0, req.RealEstateOther),
                InvestmentsRetirement = Math.Max(0, req.InvestmentsRetirement),
                InvestmentsStocks = Math.Max(0, req.InvestmentsStocks),
                VehiclesValue = Math.Max(0, req.VehiclesValue),
                PersonalPropertyValue = Math.Max(0, req.PersonalPropertyValue),
                MortgageBalance = Math.Max(0, req.MortgageBalance),
                CarLoansBalance = Math.Max(0, req.CarLoansBalance),
                CreditCardsBalance = Math.Max(0, req.CreditCardsBalance),
                PersonalLoansBalance = Math.Max(0, req.PersonalLoansBalance),
                OtherDebtsBalance = Math.Max(0, req.OtherDebtsBalance),
                TotalAssets = totalAssets,
                TotalLiabilities = totalLiabilities,
                NetWorth = netWorth
            });
        }
    }
}