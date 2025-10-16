using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class ShellAgentToolRunner : IAgentToolRunner
{
    public async Task<AgentToolRunResult> ExecuteAsync(AgentTool tool, string resolvedArguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tool.Command))
        {
            throw new InvalidOperationException("Tool command cannot be empty.");
        }

        var info = new ProcessStartInfo
        {
            FileName = tool.Command,
            Arguments = resolvedArguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process
        {
            StartInfo = info,
            EnableRaisingEvents = true
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        var outputTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                outputTcs.TrySetResult();
            }
            else
            {
                outputBuilder.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
            {
                errorTcs.TrySetResult();
            }
            else
            {
                errorBuilder.AppendLine(args.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start tool '{tool.Name}'.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timeoutMilliseconds = Math.Max(1, tool.TimeoutSeconds) * 1000;
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var delayTask = Task.Delay(timeoutMilliseconds, cancellationToken);
            var completedTask = await Task.WhenAny(waitTask, delayTask).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                TryKillProcess(process);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var timedOut = completedTask == delayTask && !delayTask.IsCanceled;

            if (timedOut)
            {
                TryKillProcess(process);
            }
            else
            {
                await waitTask.ConfigureAwait(false);
            }

            await Task.WhenAll(outputTcs.Task, errorTcs.Task).ConfigureAwait(false);

            return new AgentToolRunResult
            {
                ToolId = tool.Id,
                Name = tool.Name,
                Command = tool.Command,
                Arguments = resolvedArguments,
                ExitCode = timedOut ? -1 : process.ExitCode,
                StandardOutput = outputBuilder.ToString().Trim(),
                StandardError = errorBuilder.ToString().Trim(),
                TimedOut = timedOut
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DiagnosticLogger.Log("Tool execution failed", ex);
            return new AgentToolRunResult
            {
                ToolId = tool.Id,
                Name = tool.Name,
                Command = tool.Command,
                Arguments = resolvedArguments,
                ExitCode = -1,
                StandardOutput = outputBuilder.ToString().Trim(),
                StandardError = string.IsNullOrWhiteSpace(errorBuilder.ToString()) ? ex.Message : errorBuilder.ToString().Trim(),
                TimedOut = false
            };
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.Log("Failed to terminate timed-out tool process.", ex);
        }
    }
}
