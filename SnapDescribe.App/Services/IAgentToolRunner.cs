using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public interface IAgentToolRunner
{
    Task<AgentToolRunResult> ExecuteAsync(AgentTool tool, string resolvedArguments, CancellationToken cancellationToken);
}
