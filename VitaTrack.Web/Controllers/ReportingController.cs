using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.Reporting;

namespace VitaTrack.Web.Controllers;

public class ReportingController(IReportingService reportingService) : Controller
{
    private readonly IReportingService _reportingService = reportingService;

    public async Task<IActionResult> NutrientReport()
    {
        var data = await _reportingService.GetNutrientReportDataAsync();

        ViewData["Units"] = JsonSerializer.Serialize(data.Units);
        ViewData["MemberContributions"] = JsonSerializer.Serialize(data.MemberContributions);
        ViewData["TotalCost"] = data.TotalCost.ToString();
        ViewData["ReportDate"] = data.ReportDate.ToString("yyyy-MM-dd");
        ViewData["MemberNames"] = JsonSerializer.Serialize(data.MemberNames);
        ViewData["MemberData"] = JsonSerializer.Serialize(data.MemberData);
        ViewData["SupplementRows"] = data.Supplements
            .Select(s => new
            {
                s.Name,
                s.Brand,
                s.DailyDose,
                MonthlyCost = data.SupplementMonthlyCosts.TryGetValue(s.Id, out var cost)
                    ? cost.ToString()
                    : (string?)"N/A"
            }).ToList();

        return View(data.Supplements);
    }

    public async Task<IActionResult> CostReport()
    {
        var data = await _reportingService.GetCostReportDataAsync();

        ViewData["SupplementCosts"] = data.SupplementCosts
            .Select(s => new { s.Name, s.Brand, s.UnitCost, s.MonthlyCost }).ToList();
        ViewData["MemberCosts"] = data.MemberCosts
            .Select(m => new { m.Name, m.MonthlyCost }).ToList();
        ViewData["GrandTotal"] = data.GrandTotal.ToString();
        ViewData["ReportDate"] = data.ReportDate.ToString("yyyy-MM-dd");

        return View();
    }
}