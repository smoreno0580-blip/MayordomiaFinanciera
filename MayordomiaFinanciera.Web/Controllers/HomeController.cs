using MayordomiaFinanciera.Core.Interfaces;
using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Infrastructure.Services;
using MayordomiaFinanciera.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MayordomiaFinanciera.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;

        private readonly IEmailService _emailService; // Servicio de correo inyectado

        public HomeController(ILogger<HomeController> logger, IEmailService emailService)
        {
            _logger = logger;
            _emailService = emailService;
        }

        public IActionResult Index()
        {
            // Si el usuario ya inició sesión, redirigirlo automáticamente al Dashboard interno
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            // Si no está logueado, muestra la página web comercial / institucional
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendContactMessage(string name, string email, string subject, string message, string website_hp)
        {
            try
            {
                // 1. Validación Honeypot: Si el campo oculto contiene texto, es un robot de spam
                if (!string.IsNullOrEmpty(website_hp))
                {
                    // Simulamos éxito para no alertar al robot
                    TempData["Success"] = "¡Gracias por comunicarte con nosotros! Tu mensaje ha sido enviado exitosamente.";
                    return Redirect(Url.Action("Index", "Home") + "#contacto");
                }

                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(message))
                {
                    TempData["Error"] = "Por favor completa todos los campos obligatorios.";
                    return Redirect(Url.Action("Index", "Home") + "#contacto");
                }

                // Definir el correo receptor (puedes cambiarlo o leerlo de appsettings.json)
                string destinationEmail = "samuel.moreno@compassfinancialministry.org";

                string emailSubject = $"[Contacto Web] {subject} - De: {name}";
                string emailBody = $@"
            <h3>Nuevo mensaje desde el formulario de contacto de la Landing Page</h3>
            <p><strong>Nombre:</strong> {name}</p>
            <p><strong>Correo:</strong> {email}</p>
            <p><strong>Asunto:</strong> {subject}</p>
            <hr />
            <p><strong>Mensaje:</strong></p>
            <p>{message.Replace("\n", "<br/>")}</p>
        ";

                // Usar tu servicio de correo existente
                await _emailService.SendEmailAsync(destinationEmail, emailSubject, emailBody);

                TempData["Success"] = "¡Gracias por comunicarte con nosotros! Tu mensaje ha sido enviado exitosamente.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Ocurrió un error al enviar el mensaje. Inténtalo de nuevo más tarde.";
            }

            return Redirect(Url.Action("Index", "Home") + "#contacto");
        }
    }
}
