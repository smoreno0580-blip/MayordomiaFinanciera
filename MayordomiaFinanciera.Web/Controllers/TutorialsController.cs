using MayordomiaFinanciera.Web.Models.Tutorials;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [AllowAnonymous] // Permite el acceso sin iniciar sesión (para la Landing Page)
    public class TutorialsController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public TutorialsController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tutorials = await _churchRepository.GetAllPublishedTutorialsAsync();
            return View(tutorials);
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            var tutorial = await _churchRepository.GetTutorialByIdAsync(id);
            if (tutorial == null) return NotFound();

            return View(tutorial);
        }

        // Panel de Administración de Tutoriales
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpGet]
        public async Task<IActionResult> AdminIndex()
        {
            var tutorials = await _churchRepository.GetAllTutorialsAdminAsync();
            return View(tutorials);
        }

        // Formulario de Creación / Edición
        [Authorize(Roles = "Admin,ADMIN")]
        [HttpGet]
        public async Task<IActionResult> Edit(Guid? id)
        {
            if (id == null) return View(new PlatformTutorialDto());

            var tutorial = await _churchRepository.GetTutorialByIdAsync(id.Value);
            if (tutorial == null) return NotFound();

            return View(tutorial);
        }

        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(PlatformTutorialDto model)
        {
            if (!ModelState.IsValid) return View(model);

            if (model.Id == Guid.Empty)
            {
                await _churchRepository.CreateTutorialAsync(model);
                TempData["SuccessMessage"] = "¡Tutorial creado exitosamente!";
            }
            else
            {
                await _churchRepository.UpdateTutorialAsync(model);
                TempData["SuccessMessage"] = "¡Tutorial actualizado exitosamente!";
            }

            return RedirectToAction("AdminIndex");
        }

        [Authorize(Roles = "Admin,ADMIN")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            await _churchRepository.DeleteTutorialAsync(id);
            TempData["SuccessMessage"] = "Tutorial eliminado correctamente.";
            return RedirectToAction("AdminIndex");
        }
    }
}