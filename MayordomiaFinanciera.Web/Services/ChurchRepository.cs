using Dapper;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Models.Tutorials;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

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
        Task DeactivateChurchAsync(Guid churchId);
        Task ConfirmPaymentAndActivateAsync(Guid churchId, string newPlan);
        Task<IEnumerable<ChurchDto>> GetAllChurchesAsync();

        // Métodos para configuración de pagos por iglesia:
        Task<ChurchPaymentSettingsDto?> GetPaymentSettingsByChurchIdAsync(Guid churchId);
        Task SavePaymentSettingsAsync(ChurchPaymentSettingsDto settings);
        Task<GlobalPaymentSettingsDto> GetGlobalPaymentSettingsAsync();

        // Nuevos métodos para Perfil Ministerial Ampliado:
        Task<ChurchProfileDto?> GetChurchProfileAsync(Guid churchId);
        Task SaveChurchProfileAsync(ChurchProfileDto profile);
        Task<IEnumerable<VolunteerTeamDto>> GetVolunteerTeamsAsync(Guid churchId);
        Task<VolunteerTeamDto?> GetVolunteerTeamByIdAsync(Guid teamId);
        Task CreateVolunteerTeamAsync(VolunteerTeamDto team);
        Task AddVolunteerMemberAsync(VolunteerMemberDto member);

        Task UpdateVolunteerTeamAsync(VolunteerTeamDto model);
        Task DeleteVolunteerTeamAsync(Guid teamId, Guid churchId);
        Task RemoveVolunteerMemberAsync(Guid memberId);
                Task<IEnumerable<PlatformTutorialDto>> GetAllPublishedTutorialsAsync();
        Task<PlatformTutorialDto?> GetTutorialByIdAsync(Guid id);
                Task<IEnumerable<PlatformTutorialDto>> GetAllTutorialsAdminAsync();
        Task CreateTutorialAsync(PlatformTutorialDto tutorial);
        Task UpdateTutorialAsync(PlatformTutorialDto tutorial);
        Task DeleteTutorialAsync(Guid id);
    }

    public class ChurchRepository : IChurchRepository
    {
        private readonly string _connectionString;

        public async Task<IEnumerable<PlatformTutorialDto>> GetAllTutorialsAdminAsync()
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, title, category, description, youtube_url AS YoutubeUrl,
               step_by_step_content AS StepByStepContent, icon_class AS IconClass, 
               display_order AS DisplayOrder, is_published AS IsPublished
        FROM platform_tutorials
        ORDER BY display_order ASC, title ASC;";

            return await connection.QueryAsync<PlatformTutorialDto>(sql);
        }

        public async Task UpdateVolunteerTeamAsync(VolunteerTeamDto model)
        {
            string sql = @"
        UPDATE church_volunteer_teams 
        SET name = @Name, description = @Description, leader_name = @LeaderName
        WHERE id = @Id AND church_id = @ChurchId;";

            using var connection = CreateConnection();
            await connection.ExecuteAsync(sql, model);
        }

        public async Task DeleteVolunteerTeamAsync(Guid teamId, Guid churchId)
        {
            string sql = "DELETE FROM church_volunteer_teams WHERE id = @Id AND church_id = @ChurchId;";

            using var connection = CreateConnection();
            await connection.ExecuteAsync(sql, new { Id = teamId, ChurchId = churchId });
        }

        public async Task CreateTutorialAsync(PlatformTutorialDto tutorial)
        {
            using var connection = CreateConnection();
            string sql = @"
        INSERT INTO platform_tutorials (title, category, description, youtube_url, step_by_step_content, icon_class, display_order, is_published)
        VALUES (@Title, @Category, @Description, @YoutubeUrl, @StepByStepContent, @IconClass, @DisplayOrder, @IsPublished);";

            await connection.ExecuteAsync(sql, tutorial);
        }

        public async Task UpdateTutorialAsync(PlatformTutorialDto tutorial)
        {
            using var connection = CreateConnection();
            string sql = @"
        UPDATE platform_tutorials
        SET title = @Title, category = @Category, description = @Description,
            youtube_url = @YoutubeUrl, step_by_step_content = @StepByStepContent,
            icon_class = @IconClass, display_order = @DisplayOrder, is_published = @IsPublished
        WHERE id = @Id;";

            await connection.ExecuteAsync(sql, tutorial);
        }

        public async Task DeleteTutorialAsync(Guid id)
        {
            using var connection = CreateConnection();
            string sql = "DELETE FROM platform_tutorials WHERE id = @Id;";
            await connection.ExecuteAsync(sql, new { Id = id });
        }
        public async Task<IEnumerable<PlatformTutorialDto>> GetAllPublishedTutorialsAsync()
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, title, category, description, youtube_url AS YoutubeUrl,
               step_by_step_content AS StepByStepContent, icon_class AS IconClass, display_order AS DisplayOrder
        FROM platform_tutorials
        WHERE is_published = TRUE
        ORDER BY display_order ASC, title ASC;";

            return await connection.QueryAsync<PlatformTutorialDto>(sql);
        }

        public async Task<PlatformTutorialDto?> GetTutorialByIdAsync(Guid id)
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, title, category, description, youtube_url AS YoutubeUrl,
               step_by_step_content AS StepByStepContent, icon_class AS IconClass, display_order AS DisplayOrder
        FROM platform_tutorials
        WHERE id = @Id AND is_published = TRUE;";

            return await connection.QueryFirstOrDefaultAsync<PlatformTutorialDto>(sql, new { Id = id });
        }

        public async Task<IEnumerable<VolunteerTeamDto>> GetVolunteerTeamsAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT t.id, t.church_id AS ChurchId, t.name, t.description, t.leader_name AS LeaderName,
               (SELECT COUNT(*) FROM church_volunteer_members m WHERE m.team_id = t.id) AS MemberCount
        FROM church_volunteer_teams t
        WHERE t.church_id = @ChurchId
        ORDER BY t.name ASC;";
            return await connection.QueryAsync<VolunteerTeamDto>(sql, new { ChurchId = churchId });
        }

        public async Task<VolunteerTeamDto?> GetVolunteerTeamByIdAsync(Guid teamId)
        {
            using var connection = CreateConnection();
            string teamSql = "SELECT id, church_id AS ChurchId, name, description, leader_name AS LeaderName FROM church_volunteer_teams WHERE id = @TeamId;";
            var team = await connection.QueryFirstOrDefaultAsync<VolunteerTeamDto>(teamSql, new { TeamId = teamId });

            if (team != null)
            {
                string membersSql = "SELECT id, team_id AS TeamId, user_id AS UserId, member_name AS MemberName, member_email AS MemberEmail, role_in_team AS RoleInTeam FROM church_volunteer_members WHERE team_id = @TeamId;";
                var members = await connection.QueryAsync<VolunteerMemberDto>(membersSql, new { TeamId = teamId });
                team.Members = members.AsList();
            }
            return team;
        }

        public async Task CreateVolunteerTeamAsync(VolunteerTeamDto team)
        {
            using var connection = CreateConnection();
            string sql = @"
        INSERT INTO church_volunteer_teams (church_id, name, description, leader_name)
        VALUES (@ChurchId, @Name, @Description, @LeaderName);";
            await connection.ExecuteAsync(sql, team);
        }

        public async Task AddVolunteerMemberAsync(VolunteerMemberDto member)
        {
            using var connection = CreateConnection();
            string sql = @"
        INSERT INTO church_volunteer_members (team_id, user_id, member_name, member_email, role_in_team)
        VALUES (@TeamId, @UserId, @MemberName, @MemberEmail, @RoleInTeam);";
            await connection.ExecuteAsync(sql, member);
        }

        public async Task RemoveVolunteerMemberAsync(Guid memberId)
        {
            using var connection = CreateConnection();
            string sql = "DELETE FROM church_volunteer_members WHERE id = @MemberId;";
            await connection.ExecuteAsync(sql, new { MemberId = memberId });
        }
        public ChurchRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new ArgumentNullException(nameof(configuration));
        }

        private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

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

        public async Task DeactivateChurchAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        UPDATE churches 
        SET subscription_status = 'EXPIRED', 
            updated_at = CURRENT_TIMESTAMP 
        WHERE id = @ChurchId;";

            await connection.ExecuteAsync(sql, new { ChurchId = churchId });
        }

        public async Task ConfirmPaymentAndActivateAsync(Guid churchId, string newPlan)
        {
            using var connection = CreateConnection();
            string sql = @"
        UPDATE churches 
        SET subscription_plan = @Plan, 
            subscription_status = 'ACTIVE', 
            updated_at = CURRENT_TIMESTAMP 
        WHERE id = @ChurchId;";

            await connection.ExecuteAsync(sql, new { Plan = newPlan, ChurchId = churchId });
        }

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
        LEFT JOIN churches c ON u.church_id = c.id
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
                VALUES (@Name, @Email, @PasswordHash, 'Pastor',@ChurchId, 'PASTOR');";

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

        // Implementación de métodos para el Perfil Ministerial
        public async Task<ChurchProfileDto?> GetChurchProfileAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT id, church_id AS ChurchId, denomination, vision, mission,
               senior_pastor_name AS SeniorPastorName, pastor_spouse_name AS PastorSpouseName,
               co_pastors AS CoPastors, leadership_team AS LeadershipTeam,
               phone, public_email AS PublicEmail, address, service_times AS ServiceTimes,
               social_facebook AS SocialFacebook, social_instagram AS SocialInstagram, social_youtube AS SocialYoutube
        FROM church_profiles
        WHERE church_id = @ChurchId;";

            return await connection.QueryFirstOrDefaultAsync<ChurchProfileDto>(sql, new { ChurchId = churchId });
        }

        public async Task SaveChurchProfileAsync(ChurchProfileDto profile)
        {
            using var connection = CreateConnection();
            string sql = @"
        INSERT INTO church_profiles 
            (church_id, denomination, vision, mission, senior_pastor_name, pastor_spouse_name, co_pastors, leadership_team, phone, public_email, address, service_times, social_facebook, social_instagram, social_youtube, updated_at)
        VALUES 
            (@ChurchId, @Denomination, @Vision, @Mission, @SeniorPastorName, @PastorSpouseName, @CoPastors, @LeadershipTeam, @Phone, @PublicEmail, @Address, @ServiceTimes, @SocialFacebook, @SocialInstagram, @SocialYoutube, CURRENT_TIMESTAMP)
        ON CONFLICT (church_id) DO UPDATE SET
            denomination = EXCLUDED.denomination,
            vision = EXCLUDED.vision,
            mission = EXCLUDED.mission,
            senior_pastor_name = EXCLUDED.senior_pastor_name,
            pastor_spouse_name = EXCLUDED.pastor_spouse_name,
            co_pastors = EXCLUDED.co_pastors,
            leadership_team = EXCLUDED.leadership_team,
            phone = EXCLUDED.phone,
            public_email = EXCLUDED.public_email,
            address = EXCLUDED.address,
            service_times = EXCLUDED.service_times,
            social_facebook = EXCLUDED.social_facebook,
            social_instagram = EXCLUDED.social_instagram,
            social_youtube = EXCLUDED.social_youtube,
            updated_at = CURRENT_TIMESTAMP;";

            await connection.ExecuteAsync(sql, profile);
        }
        public async Task<IEnumerable<(string Email, string FullName)>> GetChurchMembersContactsAsync(Guid churchId)
        {
            using var connection = CreateConnection();
            string sql = @"
        SELECT u.email AS Email, u.full_name AS FullName
        FROM users u
        WHERE u.church_id = @ChurchId AND u.is_active = TRUE AND u.email IS NOT NULL;";

            return await connection.QueryAsync<(string Email, string FullName)>(sql, new { ChurchId = churchId });
        }
    }
}