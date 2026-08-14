using VitaTrack.Infrastructure.Models;

namespace VitaTrack.Infrastructure.Services;

public interface IReportingService
{
    Task<NutrientReportData> GetNutrientReportDataAsync();
    Task<CostReportData> GetCostReportDataAsync();
}