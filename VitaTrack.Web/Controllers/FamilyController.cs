using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Controllers
{
    public class FamilyController : Controller
    {
        private readonly IFamilyRepository _familyRepo;
        public FamilyController(IFamilyRepository familyRepo)
        {
            _familyRepo = familyRepo;
        }

        // GET: /Family
        public async Task<IActionResult> Index()
        {
            var members = await _familyRepo.GetAllAsync();
            return View(members);
        }

        // GET: /Family/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: /Family/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(FamilyMember member)
        {
            if (ModelState.IsValid)
            {
                await _familyRepo.AddAsync(member);
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        // GET: /Family/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var member = await _familyRepo.GetByIdAsync(id);
            if (member == null)
            {
                return NotFound();
            }
            return View(member);
        }

        // POST: /Family/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, FamilyMember member)
        {
            if (id != member.Id)
            {
                return NotFound();
            }
            if (ModelState.IsValid)
            {
                await _familyRepo.UpdateAsync(member);
                return RedirectToAction(nameof(Index));
            }
            return View(member);
        }

        // POST: /Family/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            await _familyRepo.DeleteAsync(id);
            return RedirectToAction(nameof(Index));
        }
    }
}