using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McpJiraConflu.Options;

namespace McpJiraConflu.Atlassian;

internal static class AtlassianHttp
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Configure(HttpClient client, AtlassianOptions options)
    {
        client.BaseAddress = ToBaseUri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 5, 120));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Pat);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McpJiraConflu/1.0");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Atlassian-Token", "no-check");
    }

    public static Uri ToBaseUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidOperationException("BaseUrl не задан.");
        }

        if (!url.EndsWith('/'))
        {
            url += "/";
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"BaseUrl '{url}' не является абсолютным URI.");
        }

        return uri;
    }

    public static async Task<JsonNode?> GetJsonAsync(HttpClient http, string relativeUrl, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(relativeUrl, cancellationToken);
        return await ReadJsonAsync(response, cancellationToken);
    }

    public static async Task<HttpResponseMessage> GetAsync(HttpClient http, string relativeUrl, CancellationToken cancellationToken)
    {
        return await http.GetAsync(relativeUrl, cancellationToken);
    }

    public static async Task<(HttpStatusCode Status, JsonNode? Body, string Raw)> SendJsonAsync(
        HttpClient http,
        HttpMethod method,
        string relativeUrl,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, relativeUrl);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode? node = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                node = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                // Atlassian sometimes returns HTML or empty bodies.
            }
        }

        return (response.StatusCode, node, raw);
    }

    public static async Task<JsonNode?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(FormatError(response.StatusCode, raw));
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(raw);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Не удалось разобрать JSON: {ex.Message}. Тело: {Truncate(raw)}");
        }
    }

    public static string FormatError(HttpStatusCode statusCode, string raw)
    {
        var message = ExtractErrorMessage(raw);
        return $"{(int)statusCode} {statusCode}: {message}";
    }

    public static string Truncate(string value, int max = 800)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return value;
        }

        return value[..max] + "…";
    }

    private static string ExtractErrorMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "пустой ответ";
        }

        try
        {
            var node = JsonNode.Parse(raw);
            var errorMessages = node?["errorMessages"] as JsonArray;
            if (errorMessages is { Count: > 0 })
            {
                return string.Join("; ", errorMessages.Select(x => x?.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
            }

            if (node?["message"] is { } message)
            {
                return message.ToString();
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return Truncate(raw);
    }
}
