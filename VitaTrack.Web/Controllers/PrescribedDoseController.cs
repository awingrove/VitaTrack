using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using VitaTrack.Core.Data;
using VitaTrack.Core.Features.Dosing;
using VitaTrack.Core.Features.Supplements;

namespace VitaTrack.Web.Controllers;

public class PrescribedDoseController(
    IPrescribedDoseRepository prescribedDoseRepo,
    IFamilyRepository familyRepo,
    ISupplementRepository supplementRepo,
    PrescribeDoseHandler prescribeHandler,
    AmendDoseHandler amendHandler) : Controller
{
    private readonly IPrescribedDoseRepository _prescribedDoseRepo = prescribedDoseRepo;
    private readonly IFamilyRepository _familyRepo = familyRepo;
    private readonly ISupplementRepository _supplementRepo = supplementRepo;
    private readonly PrescribeDoseHandler _prescribeHandler = prescribeHandler;
    private readonly AmendDoseHandler _amendHandler = amendHandler;

    // GET: /PrescribedDose?familyMemberId=5
    public async Task<IActionResult> Index(int? familyMemberId)
    {
        var prescribedDoses = familyMemberId.HasValue
            ? await _prescribedDoseRepo.GetByFamilyMemberIdAsync(familyMemberId.Value)
            : await _prescribedDoseRepo.GetAllAsync();

        ViewData["FamilyMembers"] = await _familyRepo.GetAllAsync();
        ViewData["SelectedFamilyMemberId"] = familyMemberId;
        return View(prescribedDoses);
    }

    // GET: /PrescribedDose/Create
    public async Task<IActionResult> Create(int? familyMemberId)
    {
        await PopulateDropdowns(familyMemberId);
        return View(new CreateDoseRequest { FamilyMemberId = familyMemberId ?? 0 });
    }

    // POST: /PrescribedDose/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateDoseRequest request)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdowns(request.FamilyMemberId, request.SupplementId);
            return View(request);
        }

        var result = await _prescribeHandler.HandleAsync(request);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(request.Multiplier), result.Error!);
            await PopulateDropdowns(request.FamilyMemberId, request.SupplementId);
            return View(request);
        }

        return RedirectToAction(nameof(Index));
    }

    // GET: /PrescribedDose/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var prescribedDose = await _prescribedDoseRepo.GetByIdAsync(id);
        if (prescribedDose is null) return NotFound();

        await PopulateDropdowns(prescribedDose.FamilyMemberId, prescribedDose.SupplementId);
        return View(new EditDoseRequest
        {
            Id = prescribedDose.Id,
            FamilyMemberId = prescribedDose.FamilyMemberId,
            SupplementId = prescribedDose.SupplementId,
            Multiplier = prescribedDose.Multiplier,
            Instructions = prescribedDose.Instructions,
            StartDate = prescribedDose.StartDate,
            EndDate = prescribedDose.EndDate,
        });
    }

    // POST: /PrescribedDose/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditDoseRequest request)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdowns(request.FamilyMemberId, request.SupplementId);
            return View(request);
        }

        var result = await _amendHandler.HandleAsync(request);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(nameof(request.Multiplier), result.Error!);
            await PopulateDropdowns(request.FamilyMemberId, request.SupplementId);
            return View(request);
        }

        return RedirectToAction(nameof(Index));
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
        ViewData["Supplements"] = supplements;
    }
}
