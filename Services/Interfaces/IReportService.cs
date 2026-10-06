using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public interface IReportService
{
    Task<string> GenerateReportZipAsync();
}
