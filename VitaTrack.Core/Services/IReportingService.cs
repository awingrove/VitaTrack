using VitaTrack.Core.Models;

namespace VitaTrack.Core.Services;

public interface IReportingService
{
    Task<NutrientReportData> GetNutrientReportDataAsync();
    Task<CostReportData> GetCostReportDataAsync();
}