using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public interface IOcrService
{
    bool IsAvailable { get; }

    Task<OcrResult> RecognizeAsync(byte[] imageBytes, string? languages, CancellationToken cancellationToken = default);
}
