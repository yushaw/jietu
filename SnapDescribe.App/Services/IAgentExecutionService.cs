using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public interface IAgentExecutionService
{
    Task<AgentExecutionResult> ExecuteAsync(AppSettings settings, CaptureRecord record, string userMessage, bool includeImage, CancellationToken cancellationToken = default);

    Task<AgentExecutionResult> ContinueAsync(AppSettings settings, CaptureRecord record, string userMessage, CancellationToken cancellationToken = default);
}
