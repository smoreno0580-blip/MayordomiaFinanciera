using System;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MayordomiaFinanciera.Web.Services
{
    public interface ISubscriptionPolicyService
    {
        Task<bool> CanAccessAdvancedFeaturesAsync(Guid userId);
    }

    public class SubscriptionPolicyService : ISubscriptionPolicyService
    {
        private readonly string _connectionString;

        public SubscriptionPolicyService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<bool> CanAccessAdvancedFeaturesAsync(Guid userId)
        {
            using var connection = new NpgsqlConnection(_connectionString);

            // Un usuario tiene acceso avanzado si está vinculado a una iglesia con suscripción activa
            string sql = @"
                SELECT c.subscription_status 
                FROM users u
                INNER JOIN churches c ON u.church_id = c.id
                WHERE u.id = @UserId AND c.subscription_status = 'ACTIVE';";

            var status = await connection.QueryFirstOrDefaultAsync<string>(sql, new { UserId = userId });

            // Si status es 'ACTIVE', tiene acceso total por pertenecer a la iglesia. 
            // Si es null (Plan Gratuito B2C), limitamos funciones avanzadas.
            return !string.IsNullOrEmpty(status);
        }
    }
}