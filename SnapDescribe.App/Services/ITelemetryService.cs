namespace SnapDescribe.App.Services;

public interface ITelemetryService
{
    void Initialize();

    void TrackAppLaunch();

    void TrackScreenshotCaptured(string capabilityId);
}
