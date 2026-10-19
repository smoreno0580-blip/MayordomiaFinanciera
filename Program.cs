using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Forzar a Npgsql a mapear el tipo 'date' de Postgres a DateTime en lugar de DateOnly
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// 1. Agregar servicios MVC
builder.Services.AddControllersWithViews();

// 2. Configurar Autenticación de Cookies Seguras (Paso 5)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.HttpOnly = true; // Previene robo por XSS
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Fuerza uso de HTTPS
        options.Cookie.SameSite = SameSiteMode.Strict; // Protege contra CSRF
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
    });

// 3. Configurar Limitador de Peticiones para evitar Ataques de Fuerza Bruta (Paso 6)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter(policyName: "LoginPolicy", options =>
    {
        options.PermitLimit = 5; // Máximo 5 intentos
        options.Window = TimeSpan.FromMinutes(1); // Cada 1 minuto
        options.QueueLimit = 0;
    });
});

builder.Services.AddScoped<SqlRepository>(provider =>
    new SqlRepository(builder.Configuration.GetConnectionString("DefaultConnection") ?? ""));

builder.Services.AddScoped<PostgresRepository>(provider =>
    new PostgresRepository(builder.Configuration.GetConnectionString("DefaultConnection") ?? ""));
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// 4. Inyectar Cabeceras de Seguridad HTTP (Paso 4)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

app.UseRouting();

// Activar Limitador de Peticiones y Autenticación
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();