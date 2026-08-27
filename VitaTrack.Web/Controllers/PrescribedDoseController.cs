using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Controllers;

public class PrescribedDoseController(
    IPrescribedDoseRepository prescribedDoseRepo,
    IFamilyRepository familyRepo,
    ISupplementRepository supplementRepo) : Controller
{
    private readonly IPrescribedDoseRepository _prescribedDoseRepo = prescribedDoseRepo;
    private readonly IFamilyRepository _familyRepo = familyRepo;
    private readonly ISupplementRepository _supplementRepo = supplementRepo;

    // GET: /PrescribedDose
    public async Task<IActionResult> Index()
    {
        var prescribedDoses = await _prescribedDoseRepo.GetAllAsync();
        return View(prescribedDoses);
    }

    // GET: /PrescribedDose/Create
    public async Task<IActionResult> Create()
    {
        await PopulateDropdowns();
        return View();
    }

    // POST: /PrescribedDose/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PrescribedDose prescribedDose)
    {
        if (ModelState.IsValid)
        {
            await _prescribedDoseRepo.AddAsync(prescribedDose);
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropdowns(prescribedDose.FamilyMemberId, prescribedDose.SupplementId);
        return View(prescribedDose);
    }

    // GET: /PrescribedDose/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var prescribedDose = await _prescribedDoseRepo.GetByIdAsync(id);
        if (prescribedDose == null) return NotFound();

        await PopulateDropdowns(prescribedDose.FamilyMemberId, prescribedDose.SupplementId);
        return View(prescribedDose);
    }

    // POST: /PrescribedDose/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PrescribedDose prescribedDose)
    {
        if (id != prescribedDose.Id) return NotFound();

        if (ModelState.IsValid)
        {
            await _prescribedDoseRepo.UpdateAsync(prescribedDose);
            return RedirectToAction(nameof(Index));
        }

        await PopulateDropdowns(prescribedDose.FamilyMemberId, prescribedDose.SupplementId);
        return View(prescribedDose);
    }

    // POST: /PrescribedDose/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        await _prescribedDoseRepo.DeleteAsync(id);
        return RedirectToAction(nameof(Index));
    }

    private async Task PopulateDropdowns(int? selectedFamilyMemberId = null, int? selectedSupplementId = null)
    {
        var familyMembers = await _familyRepo.GetAllAsync();
        var supplements = await _supplementRepo.GetAllAsync();

        ViewData["FamilyMemberId"] = new SelectList(familyMembers, "Id", "DisplayName", selectedFamilyMemberId);
        ViewData["SupplementId"] = new SelectList(supplements, "Id", "Name", selectedSupplementId);
    }
}
