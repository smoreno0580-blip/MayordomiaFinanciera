using System;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize(Roles = "admin")]
    public class SaaSChurchController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public SaaSChurchController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();
            return View(new RegisterChurchViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterChurchViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();
                return View(model);
            }

            try
            {
                await _churchRepository.RegisterNewChurchAndAdminAsync(model);
                TempData["SuccessMessage"] = "¡Iglesia registrada exitosamente!";
                return RedirectToAction("Register");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Error al guardar: {ex.Message}");
                ViewBag.Churches = await _churchRepository.GetAllChurchesAsync();
                return View(model);
            }
        }

        [Authorize(Roles = "admin,ADMIN")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ActivateChurch(Guid churchId)
        {
            try
            {
                // Ejecución limpia a través de la interfaz encapsulada del repositorio
                await _churchRepository.ActivateChurchAsync(churchId);

                TempData["SuccessMessage"] = "¡La iglesia ha sido activada exitosamente! El pastor ya puede iniciar sesión en la plataforma.";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Error al activar la iglesia: {ex.Message}";
            }

            return RedirectToAction("Register");
        }
    }
}