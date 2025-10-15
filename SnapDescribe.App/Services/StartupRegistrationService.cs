using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace SnapDescribe.App.Services;

public class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string EntryName = "SnapDescribe";

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(EntryName) as string;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var command = BuildCommand();
        return string.Equals(Normalize(value), Normalize(command), StringComparison.OrdinalIgnoreCase);
    }

    public void Apply(bool enable)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var command = BuildCommand();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                              ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                throw new InvalidOperationException("Unable to access the startup registry key.");
            }

            if (enable)
            {
                key.SetValue(EntryName, command, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(EntryName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to update launch-on-startup setting.", ex);
            throw;
        }
    }

    private static string BuildCommand()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("Unable to resolve current process path.");
        }

        var fileName = Path.GetFileName(processPath);
        if (string.Equals(fileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            var assemblyLocation = ResolveAssemblyLocation();
            return $"\"{processPath}\" \"{assemblyLocation}\"";
        }

        return $"\"{processPath}\"";
    }

    private static string ResolveAssemblyLocation()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var entryAssembly = Assembly.GetEntryAssembly();
        var assemblyName = entryAssembly?.GetName().Name;

        if (!string.IsNullOrWhiteSpace(baseDirectory) && !string.IsNullOrWhiteSpace(assemblyName))
        {
            var dllCandidate = Path.Combine(baseDirectory, assemblyName + ".dll");
            if (File.Exists(dllCandidate))
            {
                return dllCandidate;
            }

            var exeCandidate = Path.Combine(baseDirectory, assemblyName + ".exe");
            if (File.Exists(exeCandidate))
            {
                return exeCandidate;
            }
        }

        // fallback to process main module when running as single-file
        using var process = Process.GetCurrentProcess();
        var modulePath = process.MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(modulePath) && File.Exists(modulePath))
        {
            return modulePath;
        }

        throw new InvalidOperationException("Unable to resolve executable location for startup registration.");
    }

    private static string Normalize(string value) => value.Trim().Trim('"').Replace("\"", string.Empty);
}
