using System.Linq;
using Microsoft.AspNetCore.Mvc;
using VitaTrack.Infrastructure.Data;
using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Web.Controllers;

public class SupplementNutrientController(
    ISupplementNutrientRepository nutrientRepo,
    ISupplementRepository supplementRepo) : Controller
{
    private readonly ISupplementNutrientRepository _nutrientRepo = nutrientRepo;
    private readonly ISupplementRepository _supplementRepo = supplementRepo;

    // GET: /SupplementNutrient/Index/5
    public async Task<IActionResult> Index(int supplementId)
    {
        var supplement = await _supplementRepo.GetByIdAsync(supplementId);
        if (supplement == null) return NotFound();

        var nutrients = await _nutrientRepo.GetBySupplementIdAsync(supplementId);
        ViewData["Supplement"] = supplement;
        return View(nutrients);
    }

    // GET: /SupplementNutrient/Create/5
    public async Task<IActionResult> Create(int supplementId)
    {
        var supplement = await _supplementRepo.GetByIdAsync(supplementId);
        if (supplement == null) return NotFound();

        ViewData["Supplement"] = supplement;
        ViewData["ParentOptions"] = (await _nutrientRepo.GetBySupplementIdAsync(supplementId))
            .Where(n => n.ParentNutrientId == null)
            .Select(n => new { Id = n.Id, Name = $"{n.GenericName} – {n.SpecificForm}" })
            .ToList();
        return View(new SupplementNutrient { SupplementId = supplementId });
    }

    // POST: /SupplementNutrient/Create/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SupplementNutrient nutrient)
    {
        if (ModelState.IsValid)
        {
            await _nutrientRepo.AddAsync(nutrient);
            return RedirectToAction(nameof(Index), new { supplementId = nutrient.SupplementId });
        }

        var supplement = await _supplementRepo.GetByIdAsync(nutrient.SupplementId);
        ViewData["Supplement"] = supplement;
        ViewData["ParentOptions"] = (await _nutrientRepo.GetBySupplementIdAsync(nutrient.SupplementId))
            .Where(n => n.ParentNutrientId == null)
            .Select(n => new { Id = n.Id, Name = $"{n.GenericName} – {n.SpecificForm}" })
            .ToList();
        return View(nutrient);
    }

    // GET: /SupplementNutrient/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var nutrient = await _nutrientRepo.GetByIdAsync(id);
        if (nutrient == null) return NotFound();

        var supplement = await _supplementRepo.GetByIdAsync(nutrient.SupplementId);
        ViewData["Supplement"] = supplement;
        ViewData["ParentOptions"] = (await _nutrientRepo.GetBySupplementIdAsync(nutrient.SupplementId))
            .Where(n => n.ParentNutrientId == null && n.Id != nutrient.Id)
            .Select(n => new { Id = n.Id, Name = $"{n.GenericName} – {n.SpecificForm}" })
            .ToList();
        return View(nutrient);
    }

    // POST: /SupplementNutrient/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SupplementNutrient nutrient)
    {
        if (id != nutrient.Id) return NotFound();

        if (ModelState.IsValid)
        {
            await _nutrientRepo.UpdateAsync(nutrient);
            return RedirectToAction(nameof(Index), new { supplementId = nutrient.SupplementId });
        }

        var supplement = await _supplementRepo.GetByIdAsync(nutrient.SupplementId);
        ViewData["Supplement"] = supplement;
        ViewData["ParentOptions"] = (await _nutrientRepo.GetBySupplementIdAsync(nutrient.SupplementId))
            .Where(n => n.ParentNutrientId == null && n.Id != nutrient.Id)
            .Select(n => new { Id = n.Id, Name = $"{n.GenericName} – {n.SpecificForm}" })
            .ToList();
        return View(nutrient);
    }

    // GET: /SupplementNutrient/Delete/5
    public async Task<IActionResult> Delete(int id)
    {
        var nutrient = await _nutrientRepo.GetByIdAsync(id);
        if (nutrient == null) return NotFound();

        var supplement = await _supplementRepo.GetByIdAsync(nutrient.SupplementId);
        ViewData["Supplement"] = supplement;
        return View(nutrient);
    }

    // POST: /SupplementNutrient/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var nutrient = await _nutrientRepo.GetByIdAsync(id);
        if (nutrient == null) return NotFound();

        var supplementId = nutrient.SupplementId;
        await _nutrientRepo.DeleteAsync(id);
        return RedirectToAction(nameof(Index), new { supplementId });
    }

    // POST: /SupplementNutrient/DeleteSelected
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSelected(List<int> ids, int supplementId)
    {
        if (ids != null && ids.Count > 0)
        {
            await _nutrientRepo.DeleteAsync(ids);
        }
        return RedirectToAction(nameof(Index), new { supplementId });
    }
}