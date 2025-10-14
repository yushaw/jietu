using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public interface IScreenshotService
{
    Task<ScreenshotResult?> CaptureInteractiveAsync();
}
