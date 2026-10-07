using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpJiraConflu.Atlassian;

public sealed class ConfluenceClient
{
    private readonly HttpClient _http;

    public ConfluenceClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        limit = Math.Clamp(limit, 1, 50);
        var cql = LooksLikeCql(query) ? query : $"""type = page AND text ~ "{EscapeCql(query)}" """;
        var relative = $"content/search?cql={Uri.EscapeDataString(cql)}&limit={limit}&expand=space,history.lastUpdated,version";

        using var response = await AtlassianHttp.GetAsync(_http, relative, cancellationToken);
        JsonNode? payload;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            payload = await AtlassianHttp.GetJsonAsync(
                _http,
                $"search?cql={Uri.EscapeDataString(cql)}&limit={limit}",
                cancellationToken);
        }
        else
        {
            payload = await AtlassianHttp.ReadJsonAsync(response, cancellationToken);
        }

        var results = (payload?["results"] as JsonArray ?? []).Select(node => MapSearchHit(_http, node)).ToList();
        return JsonSerializer.Serialize(new { cql, size = payload?["size"]?.GetValue<int?>() ?? results.Count, results }, AtlassianHttp.JsonOptions);
    }

    public async Task<string> GetPageAsync(string pageId, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(pageId.Trim());
        var payload = await AtlassianHttp.GetJsonAsync(
                          _http,
                          $"content/{id}?expand=body.storage,version,space,ancestors,history.lastUpdated",
                          cancellationToken)
                      ?? throw new InvalidOperationException("Confluence вернула пустой ответ.");
        return JsonSerializer.Serialize(MapPage(_http, payload), AtlassianHttp.JsonOptions);
    }

    public async Task<string> GetPageByTitleAsync(string spaceKey, string title, CancellationToken cancellationToken)
    {
        var query =
            $"content?spaceKey={Uri.EscapeDataString(spaceKey.Trim())}&title={Uri.EscapeDataString(title.Trim())}&expand=body.storage,version,space,ancestors,history.lastUpdated";
        var payload = await AtlassianHttp.GetJsonAsync(_http, query, cancellationToken);
        var first = (payload?["results"] as JsonArray)?.FirstOrDefault();
        if (first is null)
        {
            throw new InvalidOperationException($"Страница '{title}' в пространстве '{spaceKey}' не найдена.");
        }

        return JsonSerializer.Serialize(MapPage(_http, first), AtlassianHttp.JsonOptions);
    }

    private static object MapSearchHit(HttpClient http, JsonNode? node)
    {
        var content = node?["content"] ?? node;
        return new
        {
            id = content?["id"]?.ToString(),
            title = content?["title"]?.ToString() ?? node?["title"]?.ToString(),
            type = content?["type"]?.ToString() ?? node?["type"]?.ToString(),
            spaceKey = content?["space"]?["key"]?.ToString() ?? node?["space"]?["key"]?.ToString(),
            spaceName = content?["space"]?["name"]?.ToString(),
            excerpt = TextExtract.FromHtml(node?["excerpt"]?.ToString(), SiteOrigin(http)),
            lastUpdated = content?["history"]?["lastUpdated"]?["when"]?.ToString()
                          ?? content?["version"]?["when"]?.ToString()
        };
    }

    private static object MapPage(HttpClient http, JsonNode page)
    {
        var storage = page["body"]?["storage"]?["value"]?.ToString();
        var ancestors = (page["ancestors"] as JsonArray ?? [])
            .Select(x => x?["title"]?.ToString())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return new
        {
            id = page["id"]?.ToString(),
            title = page["title"]?.ToString(),
            type = page["type"]?.ToString(),
            spaceKey = page["space"]?["key"]?.ToString(),
            spaceName = page["space"]?["name"]?.ToString(),
            version = page["version"]?["number"]?.GetValue<int?>(),
            lastUpdated = page["history"]?["lastUpdated"]?["when"]?.ToString()
                          ?? page["version"]?["when"]?.ToString(),
            ancestors,
            text = AtlassianHttp.Truncate(TextExtract.FromHtml(storage, SiteOrigin(http)), 100_000)
        };
    }

    private static string? SiteOrigin(HttpClient http)
    {
        var address = http.BaseAddress;
        return address is null ? null : $"{address.Scheme}://{address.Host}";
    }

    private static bool LooksLikeCql(string query)
    {
        return query.Contains('=') || query.Contains('~') || query.Contains(" AND ", StringComparison.OrdinalIgnoreCase)
               || query.Contains(" OR ", StringComparison.OrdinalIgnoreCase);
    }

    private static string EscapeCql(string query) => query.Replace("\"", "\\\"");
}
