using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SnapDescribe.App.Models;
using SnapDescribe.App.Services;
using Xunit;

namespace SnapDescribe.Tests;

public class GlmClientTests
{
    [Fact]
    public async Task DescribeAsync_BuildsExpectedRequest()
    {
        var handler = new RecordingHandler();
        handler.ResponseStatus = HttpStatusCode.OK;
        handler.ResponseBody = "{\"choices\":[{\"message\":{\"content\":\"   success  \"}}]}";
        var httpClient = new HttpClient(handler);
        var client = new GlmClient(httpClient);

        var settings = new AppSettings
        {
            BaseUrl = "https://example.com/api",
            ApiKey = "test-key",
            Model = "glm-test",
            DefaultPrompt = "Default prompt"
        };

        var pngBytes = new byte[] { 1, 2, 3, 4 };

        var response = await client.DescribeAsync(settings, pngBytes, string.Empty);

        Assert.Equal("success", response);
        Assert.Equal(HttpMethod.Post, handler.CapturedMethod);
        Assert.Equal(new Uri("https://example.com/api/chat/completions"), handler.CapturedUri);
        Assert.Equal("Bearer", handler.CapturedAuthScheme);
        Assert.Equal("test-key", handler.CapturedAuthParameter);

        using var document = JsonDocument.Parse(handler.CapturedBody ?? throw new InvalidOperationException("Missing request body"));
        var root = document.RootElement;
        Assert.Equal("glm-test", root.GetProperty("model").GetString());

        var messages = root.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());
        var firstMessage = messages[0];
        Assert.Equal("user", firstMessage.GetProperty("role").GetString());
        var content = firstMessage.GetProperty("content");
        Assert.Equal(2, content.GetArrayLength());

        var textPart = content[0];
        Assert.Equal("text", textPart.GetProperty("type").GetString());
        Assert.Equal("Default prompt", textPart.GetProperty("text").GetString());

        var imagePart = content[1];
        Assert.Equal("image_url", imagePart.GetProperty("type").GetString());
        var base64 = Convert.ToBase64String(pngBytes);
        Assert.Equal($"data:image/png;base64,{base64}", imagePart.GetProperty("image_url").GetProperty("url").GetString());
    }

    [Fact]
    public async Task DescribeAsync_ThrowsWhenImageMissing()
    {
        var client = new GlmClient(new HttpClient(new RecordingHandler()));
        var settings = new AppSettings { BaseUrl = "https://example.com", ApiKey = "key", Model = "m" };

        await Assert.ThrowsAsync<ArgumentException>(() => client.DescribeAsync(settings, Array.Empty<byte>(), "prompt"));
    }

    [Fact]
    public async Task DescribeAsync_ThrowsWhenApiKeyMissing()
    {
        var client = new GlmClient(new HttpClient(new RecordingHandler()));
        var settings = new AppSettings { BaseUrl = "https://example.com", ApiKey = string.Empty, Model = "m" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.DescribeAsync(settings, new byte[] { 1 }, "prompt"));
    }

    [Fact]
    public async Task DescribeAsync_ThrowsWhenStatusNotSuccessful()
    {
        var handler = new RecordingHandler
        {
            ResponseStatus = HttpStatusCode.BadRequest,
            ResponseBody = "{\"error\":\"bad request\"}"
        };
        var client = new GlmClient(new HttpClient(handler));
        var settings = new AppSettings { BaseUrl = "https://example.com", ApiKey = "key", Model = "m" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.DescribeAsync(settings, new byte[] { 1 }, "prompt"));
        Assert.Contains("BadRequest", ex.Message);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseBody { get; set; } = "{}";
        public Uri? CapturedUri { get; private set; }
        public HttpMethod? CapturedMethod { get; private set; }
        public string? CapturedBody { get; private set; }
        public string? CapturedAuthScheme { get; private set; }
        public string? CapturedAuthParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedUri = request.RequestUri;
            CapturedMethod = request.Method;
            CapturedAuthScheme = request.Headers.Authorization?.Scheme;
            CapturedAuthParameter = request.Headers.Authorization?.Parameter;
            CapturedBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            var response = new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
            return response;
        }
    }
}
