using System.Net.Http.Json;
using System.Text.Json;
using VsAuto.Core.Abstractions;

namespace VsAuto.Ai;

/// <summary>
/// Default provider when no LLM is configured. Returns an inconclusive verdict so the
/// deterministic checks remain authoritative and nothing fails on AI absence.
/// </summary>
public sealed class NullLlmProvider : ILlmProvider
{
    public string Name => "null-llm";

    public Task<LlmVerdict> AnalyzeAsync(LlmRequest request, CancellationToken ct)
        => Task.FromResult(new LlmVerdict(
            Passed: null,
            Summary: "AI analysis not configured (advisory, inconclusive).",
            Detail: null));
}

/// <summary>
/// Claude (Anthropic Messages API) provider. Activated when ANTHROPIC_API_KEY is present.
/// Sends the prompt plus any screenshot(s) for multimodal analysis. Used only for advisory
/// validation, failure RCA, and bug-summary drafting — never to drive the UI.
/// </summary>
public sealed class ClaudeLlmProvider : ILlmProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public ClaudeLlmProvider(string apiKey, string model = "claude-opus-4-8", HttpClient? http = null)
    {
        _apiKey = apiKey;
        _model = model;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string Name => $"claude:{_model}";

    public static ClaudeLlmProvider? FromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
            return null;
        var model = Environment.GetEnvironmentVariable("VSAUTO_LLM_MODEL") ?? "claude-opus-4-8";
        return new ClaudeLlmProvider(key, model);
    }

    public async Task<LlmVerdict> AnalyzeAsync(LlmRequest request, CancellationToken ct)
    {
        var content = new List<object> { new { type = "text", text = request.Prompt } };
        foreach (var image in request.ImagePaths.Where(File.Exists))
        {
            var bytes = await File.ReadAllBytesAsync(image, ct);
            content.Add(new
            {
                type = "image",
                source = new
                {
                    type = "base64",
                    media_type = "image/png",
                    data = Convert.ToBase64String(bytes)
                }
            });
        }

        var payload = new
        {
            model = _model,
            max_tokens = 1024,
            messages = new[] { new { role = "user", content } }
        };

        using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = JsonContent.Create(payload)
        };
        msg.Headers.Add("x-api-key", _apiKey);
        msg.Headers.Add("anthropic-version", ApiVersion);

        using var resp = await _http.SendAsync(msg, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";

        // The summary is the model's text; a stricter impl would parse a JSON verdict.
        return new LlmVerdict(Passed: null, Summary: Truncate(text, 400), Detail: text);
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
