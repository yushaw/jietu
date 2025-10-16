using System;
using System.Collections.Generic;
using Microsoft.AppCenter;
using Microsoft.AppCenter.Analytics;

namespace SnapDescribe.App.Services;

public class AppCenterTelemetryService : ITelemetryService
{
    private const string AppSecret = "5379ed97-c10f-4f3e-bfec-0dba54dcc6de";

    private bool _initialized;

    public void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            AppCenter.LogLevel = LogLevel.Warn;
            AppCenter.Start(AppSecret, typeof(Analytics));
            _initialized = true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to initialize App Center telemetry.", ex);
        }
    }

    public void TrackAppLaunch()
        => TrackEvent("app_launch");

    public void TrackScreenshotCaptured(string capabilityId)
        => TrackEvent("capture_screenshot", new Dictionary<string, string>
        {
            ["capabilityId"] = capabilityId
        });

    private void TrackEvent(string name, IDictionary<string, string>? properties = null)
    {
        if (!_initialized)
        {
            return;
        }

        try
        {
            Analytics.TrackEvent(name, properties);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log($"Failed to track telemetry event '{name}'.", ex);
        }
    }
}
