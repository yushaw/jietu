using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;

namespace SnapDescribe.App.Services;

public class GlmClient : IAiClient
{
    private readonly HttpClient _httpClient;

    public GlmClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<string> DescribeAsync(AppSettings settings, byte[] pngBytes, string prompt, CancellationToken cancellationToken = default)
    {
        var message = string.IsNullOrWhiteSpace(prompt) ? settings.DefaultPrompt : prompt;
        var conversation = new List<ChatMessage>
        {
            new("user", message, includeImage: true)
        };

        return SendAsync(settings, pngBytes, conversation, cancellationToken);
    }

    public Task<string> ChatAsync(AppSettings settings, byte[] pngBytes, IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken = default)
        => SendAsync(settings, pngBytes, conversation, cancellationToken);

    private async Task<string> SendAsync(AppSettings settings, byte[] pngBytes, IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken)
    {
        if (pngBytes is null || pngBytes.Length == 0)
        {
            throw new ArgumentException("Image data cannot be empty.", nameof(pngBytes));
        }

        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Configure the API Key in Settings first.");
        }

        if (conversation is null || conversation.Count == 0)
        {
            throw new ArgumentException("Conversation messages cannot be empty.", nameof(conversation));
        }

        var request = BuildRequest(settings, pngBytes, conversation);
        var json = JsonSerializer.Serialize(request, JsonOptions);

        var baseUrl = settings.BaseUrl?.TrimEnd('/') ?? throw new InvalidOperationException("BaseUrl is not configured.");
        var uri = new Uri($"{baseUrl}/chat/completions");

        using var message = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await _httpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            DiagnosticLogger.Log($"Model request returned status code {response.StatusCode}", new InvalidOperationException(responseContent));
            throw new InvalidOperationException($"Model request failed: {response.StatusCode} {responseContent}");
        }

        var payload = JsonSerializer.Deserialize<GlmResponse>(responseContent, JsonOptions);
        var content = payload?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            DiagnosticLogger.Log("Model response content is empty.", new InvalidOperationException(responseContent));
            throw new InvalidOperationException("Model response content is empty.");
        }

        return content.Trim();
    }

    private static object BuildRequest(AppSettings settings, byte[] pngBytes, IReadOnlyList<ChatMessage> conversation)
    {
        var base64 = Convert.ToBase64String(pngBytes);

        var messages = conversation.Select(message =>
        {
            var contentParts = new List<object>
            {
                new { type = "text", text = message.Content }
            };

            if (message.IncludeImage)
            {
                contentParts.Add(new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64}" } });
            }

            return new
            {
                role = message.Role,
                content = contentParts.ToArray()
            };
        }).ToArray();

        return new
        {
            model = settings.Model,
            messages
        };
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private sealed class GlmResponse
    {
        public GlmChoice[]? Choices { get; set; }
    }

    private sealed class GlmChoice
    {
        public GlmMessage? Message { get; set; }
    }

    private sealed class GlmMessage
    {
        public string? Content { get; set; }
    }
}
