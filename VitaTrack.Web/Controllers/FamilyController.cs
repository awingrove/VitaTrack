using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.Family;

namespace VitaTrack.Web.Controllers;

public class FamilyController(IFamilyRepository familyRepo) : Controller
{
    private readonly IFamilyRepository _familyRepo = familyRepo;

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
    public async Task<IActionResult> Create(CreateFamilyMemberRequest request)
    {
        if (ModelState.IsValid)
        {
            await _familyRepo.AddAsync(new FamilyMember
            {
                Name = request.Name,
                DisplayName = request.DisplayName,
                AvatarUrl = request.AvatarUrl,
            });
            return RedirectToAction(nameof(Index));
        }
        return View(request);
    }

    // GET: /Family/Edit/5
    public async Task<IActionResult> Edit(int id)
    {
        var member = await _familyRepo.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound();
        }
        return View(new EditFamilyMemberRequest
        {
            Id = member.Id,
            Name = member.Name,
            DisplayName = member.DisplayName,
            AvatarUrl = member.AvatarUrl,
        });
    }

    // POST: /Family/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditFamilyMemberRequest request)
    {
        if (id != request.Id)
        {
            return NotFound();
        }
        if (ModelState.IsValid)
        {
            await _familyRepo.UpdateAsync(new FamilyMember
            {
                Id = request.Id,
                Name = request.Name,
                DisplayName = request.DisplayName,
                AvatarUrl = request.AvatarUrl,
            });
            return RedirectToAction(nameof(Index));
        }
        return View(request);
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