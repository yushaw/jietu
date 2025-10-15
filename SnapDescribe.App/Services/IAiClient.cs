using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public interface IAiClient
{
    Task<string> DescribeAsync(AppSettings settings, byte[] pngBytes, string prompt, CancellationToken cancellationToken = default);

    Task<string> ChatAsync(AppSettings settings, byte[] pngBytes, IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken = default);
}
