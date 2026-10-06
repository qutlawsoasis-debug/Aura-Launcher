using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public record SendReportResult(bool Success, string? ReportId, string? ErrorMessage);

public interface IReportService
{
    Task<string> GenerateReportZipAsync(string? errorText = null, string? userComment = null);
    Task<byte[]> GenerateReportBytesAsync(string? errorText = null, string? userComment = null);
    Task<SendReportResult> SendReportAsync(string? errorText = null, string? userComment = null);
}
