using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using MayordomiaFinanciera.Web.Models.DebtSnowball;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MayordomiaFinanciera.Web.Services
{
    public interface IDebtSnowballRepository
    {
        Task<DebtSnowballViewModel> GetUserDebtsAndConfigAsync(Guid userId);
        Task<int> SaveOrUpdateDebtAsync(Guid userId, SaveDebtRequest request);
        Task<bool> DeleteDebtAsync(Guid userId, int debtId);
        Task<bool> SaveConfigAsync(Guid userId, SaveSnowballConfigRequest request);
    }

    public class DebtSnowballRepository : IDebtSnowballRepository
    {
        private readonly string _connectionString;

        public DebtSnowballRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        public async Task<DebtSnowballViewModel> GetUserDebtsAndConfigAsync(Guid userId)
        {
            using var connection = CreateConnection();

            string configSql = "SELECT extra_monthly_payment AS ExtraMonthlyPayment, strategy_type AS StrategyType FROM user_snowball_configs WHERE user_id = @UserId;";
            var config = await connection.QueryFirstOrDefaultAsync<SaveSnowballConfigRequest>(configSql, new { UserId = userId });

            string debtsSql = @"
                SELECT id, creditor_name AS CreditorName, current_balance AS CurrentBalance,
                       minimum_payment AS MinimumPayment, interest_rate AS InterestRate, sort_order AS SortOrder
                FROM user_debts
                WHERE user_id = @UserId
                ORDER BY current_balance ASC;";

            var debts = (await connection.QueryAsync<UserDebtDto>(debtsSql, new { UserId = userId })).ToList();

            return new DebtSnowballViewModel
            {
                ExtraMonthlyPayment = config?.ExtraMonthlyPayment ?? 0,
                StrategyType = config?.StrategyType ?? "SNOWBALL",
                Debts = debts
            };
        }

        public async Task<int> SaveOrUpdateDebtAsync(Guid userId, SaveDebtRequest request)
        {
            using var connection = CreateConnection();

            if (request.Id.HasValue && request.Id.Value > 0)
            {
                string updateSql = @"
                    UPDATE user_debts
                    SET creditor_name = @CreditorName,
                        current_balance = @CurrentBalance,
                        minimum_payment = @MinimumPayment,
                        interest_rate = @InterestRate,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE id = @Id AND user_id = @UserId;";

                await connection.ExecuteAsync(updateSql, new
                {
                    Id = request.Id.Value,
                    UserId = userId,
                    CreditorName = request.CreditorName.Trim(),
                    CurrentBalance = Math.Max(0, request.CurrentBalance),
                    MinimumPayment = Math.Max(0, request.MinimumPayment),
                    InterestRate = Math.Max(0, request.InterestRate)
                });

                return request.Id.Value;
            }

            string insertSql = @"
                INSERT INTO user_debts (user_id, creditor_name, current_balance, minimum_payment, interest_rate)
                VALUES (@UserId, @CreditorName, @CurrentBalance, @MinimumPayment, @InterestRate)
                RETURNING id;";

            return await connection.ExecuteScalarAsync<int>(insertSql, new
            {
                UserId = userId,
                CreditorName = request.CreditorName.Trim(),
                CurrentBalance = Math.Max(0, request.CurrentBalance),
                MinimumPayment = Math.Max(0, request.MinimumPayment),
                InterestRate = Math.Max(0, request.InterestRate)
            });
        }

        public async Task<bool> DeleteDebtAsync(Guid userId, int debtId)
        {
            using var connection = CreateConnection();
            string sql = "DELETE FROM user_debts WHERE id = @DebtId AND user_id = @UserId;";
            int affected = await connection.ExecuteAsync(sql, new { DebtId = debtId, UserId = userId });
            return affected > 0;
        }

        public async Task<bool> SaveConfigAsync(Guid userId, SaveSnowballConfigRequest request)
        {
            using var connection = CreateConnection();
            string sql = @"
                INSERT INTO user_snowball_configs (user_id, extra_monthly_payment, strategy_type, updated_at)
                VALUES (@UserId, @ExtraMonthlyPayment, @StrategyType, CURRENT_TIMESTAMP)
                ON CONFLICT (user_id)
                DO UPDATE SET
                    extra_monthly_payment = EXCLUDED.extra_monthly_payment,
                    strategy_type = EXCLUDED.strategy_type,
                    updated_at = CURRENT_TIMESTAMP;";

            int affected = await connection.ExecuteAsync(sql, new
            {
                UserId = userId,
                ExtraMonthlyPayment = Math.Max(0, request.ExtraMonthlyPayment),
                StrategyType = request.StrategyType ?? "SNOWBALL"
            });

            return affected > 0;
        }
    }
}