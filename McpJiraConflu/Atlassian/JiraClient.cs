using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpJiraConflu.Atlassian;

public sealed class JiraClient
{
    private static readonly string[] DefaultFields =
    [
        "summary", "status", "assignee", "issuetype", "priority", "updated", "created", "reporter", "description"
    ];

    private readonly HttpClient _http;

    public JiraClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> SearchIssuesAsync(string jql, int maxResults, CancellationToken cancellationToken)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);
        var fields = string.Join(',', DefaultFields);
        var query = $"search?jql={Uri.EscapeDataString(jql)}&maxResults={maxResults}&fields={fields}";

        using var get = await AtlassianHttp.GetAsync(_http, query, cancellationToken);
        JsonNode? payload;
        if (get.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            var (status, body, raw) = await AtlassianHttp.SendJsonAsync(
                _http,
                HttpMethod.Post,
                "search/jql",
                new { jql, maxResults, fields = DefaultFields },
                cancellationToken);

            if (!IsSuccess(status))
            {
                throw new InvalidOperationException(AtlassianHttp.FormatError(status, raw));
            }

            payload = body;
        }
        else
        {
            payload = await AtlassianHttp.ReadJsonAsync(get, cancellationToken);
        }

        var issues = payload?["issues"] as JsonArray ?? [];
        var result = issues.Select(MapIssueSummary).ToList();
        return JsonSerializer.Serialize(new { total = payload?["total"]?.GetValue<int?>() ?? result.Count, issues = result }, AtlassianHttp.JsonOptions);
    }

    public async Task<string> GetIssueAsync(string issueKey, CancellationToken cancellationToken)
    {
        var key = Uri.EscapeDataString(issueKey.Trim());
        var query = $"issue/{key}?fields=summary,status,assignee,issuetype,priority,updated,created,reporter,description,comment,labels,parent,issuelinks,subtasks,attachment";
        var payload = await AtlassianHttp.GetJsonAsync(_http, query, cancellationToken)
                      ?? throw new InvalidOperationException("Jira вернула пустой ответ.");
        return JsonSerializer.Serialize(MapIssueDetails(payload), AtlassianHttp.JsonOptions);
    }

    public async Task<string> GetTransitionsAsync(string issueKey, CancellationToken cancellationToken)
    {
        var transitions = await LoadTransitionsAsync(issueKey, cancellationToken);
        return JsonSerializer.Serialize(new { issueKey, transitions }, AtlassianHttp.JsonOptions);
    }

    public async Task<string> TransitionIssueAsync(
        string issueKey,
        string transition,
        string? comment,
        CancellationToken cancellationToken)
    {
        var available = await LoadTransitionsAsync(issueKey, cancellationToken);
        var match = FindTransition(available, transition)
                    ?? throw new InvalidOperationException(
                        $"Переход '{transition}' не найден. Доступные: {string.Join(", ", available.Select(t => $"{t.Name} [{t.Id}] → {t.ToStatus}"))}");

        object body = string.IsNullOrWhiteSpace(comment)
            ? new { transition = new { id = match.Id } }
            : new
            {
                transition = new { id = match.Id },
                update = new
                {
                    comment = new[]
                    {
                        new { add = new { body = comment } }
                    }
                }
            };

        var key = Uri.EscapeDataString(issueKey.Trim());
        var (status, _, raw) = await AtlassianHttp.SendJsonAsync(
            _http,
            HttpMethod.Post,
            $"issue/{key}/transitions",
            body,
            cancellationToken);

        if (status != HttpStatusCode.NoContent && !IsSuccess(status))
        {
            throw new InvalidOperationException(AtlassianHttp.FormatError(status, raw));
        }

        return JsonSerializer.Serialize(new
        {
            issueKey,
            transitionId = match.Id,
            transitionName = match.Name,
            toStatus = match.ToStatus,
            status = "ok"
        }, AtlassianHttp.JsonOptions);
    }

    public async Task<string> GetAttachmentAsync(string attachmentId, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(attachmentId.Trim());
        var meta = await AtlassianHttp.GetJsonAsync(_http, $"attachment/{id}", cancellationToken)
                   ?? throw new InvalidOperationException("Jira вернула пустой ответ по вложению.");

        var filename = meta["filename"]?.ToString() ?? id;
        var mimeType = meta["mimeType"]?.ToString() ?? "application/octet-stream";
        var size = meta["size"]?.GetValue<long?>() ?? 0;
        var contentUrl = meta["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(contentUrl))
        {
            throw new InvalidOperationException($"У вложения {filename} нет ссылки на содержимое.");
        }

        using var response = await _http.GetAsync(contentUrl, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(AtlassianHttp.FormatError(response.StatusCode, Encoding.UTF8.GetString(bytes)));
        }

        if (IsText(mimeType, filename))
        {
            var text = Encoding.UTF8.GetString(bytes);
            return JsonSerializer.Serialize(new
            {
                id = attachmentId,
                filename,
                mimeType,
                size,
                text = AtlassianHttp.Truncate(text, 100_000)
            }, AtlassianHttp.JsonOptions);
        }

        const int maxInlineBytes = 750_000;
        if (bytes.Length <= maxInlineBytes)
        {
            return JsonSerializer.Serialize(new
            {
                id = attachmentId,
                filename,
                mimeType,
                size,
                base64 = Convert.ToBase64String(bytes)
            }, AtlassianHttp.JsonOptions);
        }

        return JsonSerializer.Serialize(new
        {
            id = attachmentId,
            filename,
            mimeType,
            size,
            note = "Файл больше 750 КБ, содержимое не вложено."
        }, AtlassianHttp.JsonOptions);
    }

    private async Task<List<JiraTransition>> LoadTransitionsAsync(string issueKey, CancellationToken cancellationToken)
    {
        var key = Uri.EscapeDataString(issueKey.Trim());
        var payload = await AtlassianHttp.GetJsonAsync(_http, $"issue/{key}/transitions", cancellationToken);
        var items = payload?["transitions"] as JsonArray ?? [];
        return items.Select(node => new JiraTransition(
            node?["id"]?.ToString() ?? string.Empty,
            node?["name"]?.ToString() ?? string.Empty,
            node?["to"]?["name"]?.ToString() ?? node?["to"]?["status"]?.ToString()
        )).Where(t => !string.IsNullOrWhiteSpace(t.Id)).ToList();
    }

    private static JiraTransition? FindTransition(IReadOnlyList<JiraTransition> transitions, string requested)
    {
        var value = requested.Trim();
        return transitions.FirstOrDefault(t => t.Id.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? transitions.FirstOrDefault(t => t.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
               ?? transitions.FirstOrDefault(t => string.Equals(t.ToStatus, value, StringComparison.OrdinalIgnoreCase));
    }

    private static object MapIssueSummary(JsonNode? issue)
    {
        var fields = issue?["fields"];
        return new
        {
            key = issue?["key"]?.ToString(),
            summary = fields?["summary"]?.ToString(),
            status = Named(fields?["status"]),
            assignee = Named(fields?["assignee"]),
            issueType = Named(fields?["issuetype"]),
            priority = Named(fields?["priority"]),
            updated = fields?["updated"]?.ToString()
        };
    }

    private static object MapIssueDetails(JsonNode issue)
    {
        var fields = issue["fields"];
        var descriptionNode = fields?["description"];
        var description = descriptionNode is null
            ? string.Empty
            : TextExtract.FromAdfOrString(descriptionNode.Deserialize<JsonElement>());

        var comments = (fields?["comment"]?["comments"] as JsonArray ?? [])
            .Select(comment => new
            {
                author = Named(comment?["author"]),
                created = comment?["created"]?.ToString(),
                body = comment?["body"] is null
                    ? string.Empty
                    : TextExtract.FromAdfOrString(comment["body"]!.Deserialize<JsonElement>())
            });

        return new
        {
            key = issue["key"]?.ToString(),
            summary = fields?["summary"]?.ToString(),
            status = Named(fields?["status"]),
            assignee = Named(fields?["assignee"]),
            reporter = Named(fields?["reporter"]),
            issueType = Named(fields?["issuetype"]),
            priority = Named(fields?["priority"]),
            labels = (fields?["labels"] as JsonArray)?.Select(x => x?.ToString()).ToArray() ?? [],
            created = fields?["created"]?.ToString(),
            updated = fields?["updated"]?.ToString(),
            parent = MapRelatedIssue(fields?["parent"]),
            links = (fields?["issuelinks"] as JsonArray ?? []).Select(MapIssueLink).ToArray(),
            subtasks = (fields?["subtasks"] as JsonArray ?? []).Select(MapRelatedIssue).ToArray(),
            attachments = (fields?["attachment"] as JsonArray ?? []).Select(MapAttachment).ToArray(),
            description,
            comments
        };
    }

    private static object? MapRelatedIssue(JsonNode? issue)
    {
        if (issue is null)
        {
            return null;
        }

        var fields = issue["fields"];
        return new
        {
            key = issue["key"]?.ToString(),
            summary = fields?["summary"]?.ToString(),
            status = Named(fields?["status"]),
            issueType = Named(fields?["issuetype"])
        };
    }

    private static object MapIssueLink(JsonNode? link)
    {
        var outward = link?["outwardIssue"];
        var inward = link?["inwardIssue"];
        var related = MapRelatedIssue(outward ?? inward);
        var direction = outward is not null
            ? link?["type"]?["outward"]?.ToString()
            : link?["type"]?["inward"]?.ToString();

        return new
        {
            type = link?["type"]?["name"]?.ToString(),
            direction,
            issue = related
        };
    }

    private static object MapAttachment(JsonNode? node)
    {
        return new
        {
            id = node?["id"]?.ToString(),
            filename = node?["filename"]?.ToString(),
            mimeType = node?["mimeType"]?.ToString(),
            size = node?["size"]?.GetValue<long?>(),
            created = node?["created"]?.ToString()
        };
    }

    private static bool IsText(string mimeType, string filename)
    {
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (mimeType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || mimeType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || mimeType.Contains("yaml", StringComparison.OrdinalIgnoreCase)
            || mimeType.Contains("javascript", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(filename);
        return extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".sql", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Named(JsonNode? node)
    {
        return node?["displayName"]?.ToString()
               ?? node?["name"]?.ToString()
               ?? node?.ToString();
    }

    private static bool IsSuccess(HttpStatusCode statusCode) => (int)statusCode is >= 200 and <= 299;

    private sealed record JiraTransition(string Id, string Name, string? ToStatus);
}
