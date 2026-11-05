using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Npgsql;
using System;
using System.Threading.Tasks;
using MayordomiaFinanciera.Infrastructure.Repositories;

namespace MayordomiaFinanciera.Web.Filters
{
    public class CheckChurchSubscriptionAttribute : TypeFilterAttribute
    {
        public CheckChurchSubscriptionAttribute() : base(typeof(CheckChurchSubscriptionFilter))
        {
        }

        private class CheckChurchSubscriptionFilter : IAsyncActionFilter
        {
            private readonly PostgresRepository _postgresRepository;

            public CheckChurchSubscriptionFilter(PostgresRepository postgresRepository)
            {
                _postgresRepository = postgresRepository;
            }

            public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
            {
                var user = context.HttpContext.User;
                if (!user.Identity.IsAuthenticated)
                {
                    await next();
                    return;
                }

                var controllerName = context.RouteData.Values["controller"]?.ToString() ?? "";
                var actionName = context.RouteData.Values["action"]?.ToString() ?? "";

                // Excepciones permitidas
                if (controllerName.Equals("Account", StringComparison.OrdinalIgnoreCase) ||
                    controllerName.Equals("Home", StringComparison.OrdinalIgnoreCase) ||
                    (controllerName.Equals("Subscription", StringComparison.OrdinalIgnoreCase) &&
                     actionName.Equals("PendingPayment", StringComparison.OrdinalIgnoreCase)))
                {
                    await next();
                    return;
                }

                string? churchIdClaim = user.FindFirst("ChurchId")?.Value ?? user.FindFirst("church_id")?.Value;
                Guid churchId = Guid.Empty;

                if (!string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid parsedId))
                {
                    churchId = parsedId;
                }
                else
                {
                    string userEmail = user.Identity.Name ?? "";
                    if (!string.IsNullOrEmpty(userEmail))
                    {
                        string queryUser = "SELECT church_id FROM users WHERE email = @Email OR name = @Email LIMIT 1;";
                        var dtUser = await _postgresRepository.ExecuteQueryAsync(queryUser, new NpgsqlParameter[] { new NpgsqlParameter("@Email", userEmail) });
                        if (dtUser.Rows.Count > 0 && dtUser.Rows[0]["church_id"] != DBNull.Value)
                        {
                            Guid.TryParse(dtUser.Rows[0]["church_id"].ToString(), out churchId);
                        }
                    }
                }

                if (churchId != Guid.Empty)
                {
                    string queryChurch = "SELECT subscription_status, next_billing_date FROM churches WHERE id = @ChurchId LIMIT 1;";
                    var dtChurch = await _postgresRepository.ExecuteQueryAsync(queryChurch, new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) });

                    if (dtChurch.Rows.Count > 0)
                    {
                        string status = dtChurch.Rows[0]["subscription_status"]?.ToString()?.ToUpper() ?? "ACTIVE";
                        DateTime? nextBilling = dtChurch.Rows[0]["next_billing_date"] != DBNull.Value
                            ? Convert.ToDateTime(dtChurch.Rows[0]["next_billing_date"])
                            : (DateTime?)null;

                        // Período de gracia de 7 días a partir del próximo corte
                        DateTime gracePeriodEnd = nextBilling.HasValue ? nextBilling.Value.AddDays(7) : DateTime.MinValue;
                        bool isGracePeriodExpired = nextBilling.HasValue && DateTime.Now > gracePeriodEnd && status != "ACTIVE";

                        if (status == "SUSPENDED" || isGracePeriodExpired)
                        {
                            if (!(controllerName.Equals("Subscription", StringComparison.OrdinalIgnoreCase) && actionName.Equals("PendingPayment", StringComparison.OrdinalIgnoreCase)))
                            {
                                context.Result = new RedirectToActionResult("PendingPayment", "Subscription", null);
                                return;
                            }
                        }
                    }
                }

                await next();
            }
        }
    }
}