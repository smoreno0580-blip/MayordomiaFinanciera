using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using MayordomiaFinanciera.Web.Models.Church;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace MayordomiaFinanciera.Web.Services
{
    public interface IChurchRepository
    {
        Task<ChurchDto?> GetChurchByIdAsync(Guid churchId);
        Task<ChurchDto?> GetChurchByUserIdAsync(Guid userId);
        Task<bool> LinkUserToChurchByCodeAsync(Guid userId, string code);
        Task<bool> UnlinkUserFromChurchAsync(Guid userId);
        Task<Guid> RegisterNewChurchAndAdminAsync(RegisterChurchViewModel model, string initialStatus = "ACTIVE");
        Task<IEnumerable<dynamic>> GetMembersByChurchIdAsync(Guid churchId);
        Task<IEnumerable<dynamic>> GetInvitationCodesByChurchIdAsync(Guid churchId);
                Task ActivateChurchAsync(Guid churchId);
        Task<IEnumerable<ChurchDto>> GetAllChurchesAsync();
        // Nuevos métodos para configuración de pagos por iglesia:
        Task<ChurchPaymentSettingsDto?> GetPaymentSettingsByChurchIdAsync(Guid churchId);
        Task SavePaymentSettingsAsync(ChurchPaymentSettingsDto settings);
        Task<GlobalPaymentSettingsDto> GetGlobalPaymentSettingsAsync();
    }

    public class ChurchRepository : IChurchRepository
    {
        private readonly string _connectionString;

        public async Task<IEnumerable<ChurchDto>> GetAllChurchesAsync()
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, name, slug, logo_url AS LogoUrl, primary_color AS PrimaryColor,
               secondary_color AS SecondaryColor, subscription_plan AS SubscriptionPlan,
               subscription_status AS SubscriptionStatus, max_members AS MaxMembers
        FROM churches
        ORDER BY name ASC;";

            return await connection.QueryAsync<ChurchDto>(sql);
        }
                public async Task ActivateChurchAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        UPDATE churches 
        SET subscription_status = 'ACTIVE', 
            updated_at = CURRENT_TIMESTAMP 
        WHERE id = @ChurchId;";

            await connection.ExecuteAsync(sql, new { ChurchId = churchId });
        }

        public ChurchRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

        public async Task<ChurchDto?> GetChurchByIdAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
                SELECT id, name, slug, logo_url AS LogoUrl, primary_color AS PrimaryColor,
                       secondary_color AS SecondaryColor, subscription_plan AS SubscriptionPlan,
                       subscription_status AS SubscriptionStatus, max_members AS MaxMembers
                FROM churches
                WHERE id = @ChurchId;";

            return await connection.QueryFirstOrDefaultAsync<ChurchDto>(sql, new { ChurchId = churchId });
        }

        public async Task<ChurchDto?> GetChurchByUserIdAsync(Guid userId)
        {
            using var connection = CreateConnection();
            string sql = @"
                SELECT c.id, c.name, c.slug, c.logo_url AS LogoUrl, c.primary_color AS PrimaryColor,
                       c.secondary_color AS SecondaryColor, c.subscription_plan AS SubscriptionPlan,
                       c.subscription_status AS SubscriptionStatus, c.max_members AS MaxMembers
                FROM users u
                INNER JOIN churches c ON u.church_id = c.id
                WHERE u.id = @UserId;";

            return await connection.QueryFirstOrDefaultAsync<ChurchDto>(sql, new { UserId = userId });
        }

        public async Task<bool> LinkUserToChurchByCodeAsync(Guid userId, string code)
        {
            using var connection = CreateConnection();

            string findSql = @"
                SELECT church_id 
                FROM church_invitation_codes 
                WHERE UPPER(code) = UPPER(@Code) AND is_active = TRUE;";

            var churchId = await connection.QueryFirstOrDefaultAsync<Guid?>(findSql, new { Code = code.Trim() });

            if (!churchId.HasValue) return false;

            string updateSql = @"
                UPDATE users 
                SET church_id = @ChurchId, church_role = 'MEMBER'
                WHERE id = @UserId;";

            int affected = await connection.ExecuteAsync(updateSql, new { ChurchId = churchId.Value, UserId = userId });
            return affected > 0;
        }

        public async Task<bool> UnlinkUserFromChurchAsync(Guid userId)
        {
            using var connection = CreateConnection();
            string sql = @"
                UPDATE users 
                SET church_id = NULL, church_role = 'MEMBER'
                WHERE id = @UserId;";

            int affected = await connection.ExecuteAsync(sql, new { UserId = userId });
            return affected > 0;
        }

        public async Task<GlobalPaymentSettingsDto> GetGlobalPaymentSettingsAsync()
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, 
               saas_ach_bank_name AS SaasAchBankName, 
               saas_ach_account_type AS SaasAchAccountType, 
               saas_ach_account_number AS SaasAchAccountNumber, 
               saas_ach_account_holder AS SaasAchAccountHolder, 
               saas_yappy_alias AS SaasYappyAlias, 
               saas_paypal_email AS SaasPaypalEmail, 
               saas_paypal_client_id AS SaasPaypalClientId
        FROM global_payment_settings
        LIMIT 1;";

            var result = await connection.QueryFirstOrDefaultAsync<GlobalPaymentSettingsDto>(sql);
            return result ?? new GlobalPaymentSettingsDto();
        }

        public async Task<Guid> RegisterNewChurchAndAdminAsync(RegisterChurchViewModel model, string initialStatus = "ACTIVE")
        {
            using var connection = CreateConnection();
            if (connection.State != ConnectionState.Open) connection.Open();
            using var transaction = connection.BeginTransaction();

            try
            {
                int maxMembers = model.SubscriptionPlan == "CATHEDRAL" ? 1000 : (model.SubscriptionPlan == "MINISTERIAL" ? 200 : 50);

                // Se inserta respetando el initialStatus (PENDING_VERIFICATION o ACTIVE)
                string insertChurchSql = @"
            INSERT INTO churches (name, slug, subscription_plan, subscription_status, max_members, payment_reference, created_by_landing)
            VALUES (@Name, @Slug, @Plan, @Status, @MaxMembers, @PaymentReference, @CreatedByLanding)
            RETURNING id;";

                Guid churchId = await connection.ExecuteScalarAsync<Guid>(insertChurchSql, new
                {
                    Name = model.Name,
                    Slug = model.Slug.ToLower().Trim(),
                    Plan = model.SubscriptionPlan,
                    Status = initialStatus,
                    MaxMembers = maxMembers,
                    PaymentReference = model.PaymentReference ?? "MANUAL-ADMIN",
                    CreatedByLanding = model.CreatedByLanding
                }, transaction);

                string cleanSlug = System.Text.RegularExpressions.Regex.Replace(model.Slug.ToUpper().Trim(), @"[^A-Z0-9]", "");
                string inviteCode = $"{cleanSlug}-2026";

                string insertCodeSql = @"
            INSERT INTO church_invitation_codes (church_id, code, is_active)
            VALUES (@ChurchId, @Code, TRUE);";

                await connection.ExecuteAsync(insertCodeSql, new { ChurchId = churchId, Code = inviteCode }, transaction);

                string passwordPlain = model.AdminPassword;
                string adminName = $"Pastor {model.Name}";

                string checkUserSql = "SELECT id FROM users WHERE LOWER(email) = LOWER(@Email);";
                var existingUserId = await connection.QueryFirstOrDefaultAsync<Guid?>(checkUserSql, new { Email = model.AdminEmail.Trim() }, transaction);

                if (existingUserId.HasValue)
                {
                    string updateUserSql = @"
                UPDATE users 
                SET password_hash = @PasswordHash, church_id = @ChurchId, church_role = 'PASTOR'
                WHERE id = @UserId;";

                    await connection.ExecuteAsync(updateUserSql, new
                    {
                        PasswordHash = passwordPlain,
                        ChurchId = churchId,
                        UserId = existingUserId.Value
                    }, transaction);
                }
                else
                {
                    string insertUserSql = @"
                INSERT INTO users (name, email, password_hash, role, church_id, church_role)
                VALUES (@Name, @Email, @PasswordHash, 'Teacher',@ChurchId, 'PASTOR');";

                    await connection.ExecuteAsync(insertUserSql, new
                    {
                        Name = adminName,
                        Email = model.AdminEmail.Trim().ToLower(),
                        PasswordHash = passwordPlain,
                        ChurchId = churchId
                    }, transaction);
                }

                transaction.Commit();
                return churchId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public async Task<IEnumerable<dynamic>> GetMembersByChurchIdAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
                SELECT id, email, church_role AS Role, created_at AS JoinedAt
                FROM users
                WHERE church_id = @ChurchId
                ORDER BY created_at DESC;";

            return await connection.QueryAsync(sql, new { ChurchId = churchId });
        }

        public async Task<IEnumerable<dynamic>> GetInvitationCodesByChurchIdAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
                SELECT id, code, is_active AS IsActive, created_at AS CreatedAt
                FROM church_invitation_codes
                WHERE church_id = @ChurchId
                ORDER BY created_at DESC;";

            return await connection.QueryAsync(sql, new { ChurchId = churchId });
        }

        public async Task<ChurchPaymentSettingsDto?> GetPaymentSettingsByChurchIdAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, church_id AS ChurchId,
               ach_bank_name AS AchBankName, ach_account_type AS AchAccountType,
               ach_account_number AS AchAccountNumber, ach_account_holder AS AchAccountHolder,
               yappy_phone AS YappyPhone, yappy_alias AS YappyAlias,
               paypal_email AS PaypalEmail,
               is_ach_enabled AS IsAchEnabled, is_yappy_enabled AS IsYappyEnabled,
               is_paypal_enabled AS IsPaypalEnabled
        FROM church_payment_settings
        WHERE church_id = @ChurchId;";

            return await connection.QueryFirstOrDefaultAsync<ChurchPaymentSettingsDto>(sql, new { ChurchId = churchId });
        }

        public async Task SavePaymentSettingsAsync(ChurchPaymentSettingsDto settings)
        {
            using var connection = CreateConnection();
            string sql = @"
        INSERT INTO church_payment_settings 
            (church_id, ach_bank_name, ach_account_type, ach_account_number, ach_account_holder, yappy_phone, yappy_alias, paypal_email, is_ach_enabled, is_yappy_enabled, is_paypal_enabled, updated_at)
        VALUES 
            (@ChurchId, @AchBankName, @AchAccountType, @AchAccountNumber, @AchAccountHolder, @YappyPhone, @YappyAlias, @PaypalEmail, @IsAchEnabled, @IsYappyEnabled, @IsPaypalEnabled, CURRENT_TIMESTAMP)
        ON CONFLICT (church_id) DO UPDATE SET
            ach_bank_name = EXCLUDED.ach_bank_name,
            ach_account_type = EXCLUDED.ach_account_type,
            ach_account_number = EXCLUDED.ach_account_number,
            ach_account_holder = EXCLUDED.ach_account_holder,
            yappy_phone = EXCLUDED.yappy_phone,
            yappy_alias = EXCLUDED.yappy_alias,
            paypal_email = EXCLUDED.paypal_email,
            is_ach_enabled = EXCLUDED.is_ach_enabled,
            is_yappy_enabled = EXCLUDED.is_yappy_enabled,
            is_paypal_enabled = EXCLUDED.is_paypal_enabled,
            updated_at = CURRENT_TIMESTAMP;";

            await connection.ExecuteAsync(sql, settings);
        }
    }
}