using Microsoft.AspNetCore.Mvc;
using VitaTrack.Core.Features.Reporting;

namespace VitaTrack.Web.Controllers;

public class ReportingController(IReportingService reportingService) : Controller
{
    private readonly IReportingService _reportingService = reportingService;

    public async Task<IActionResult> NutrientReport()
    {
        var data = await _reportingService.GetNutrientReportDataAsync();
        return View(data);
    }

    public async Task<IActionResult> CostReport()
    {
        var data = await _reportingService.GetCostReportDataAsync();
        return View(data);
    }
}
