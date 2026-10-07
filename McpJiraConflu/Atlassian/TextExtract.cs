using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McpJiraConflu.Atlassian;

internal static class TextExtract
{
    public static string FromHtml(string? html, string? siteOrigin = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = Regex.Replace(html, "<script[\\s\\S]*?</script>", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style[\\s\\S]*?</style>", string.Empty, RegexOptions.IgnoreCase);
        text = ReplaceAcLinks(text);
        text = ReplaceAnchors(text, siteOrigin);
        text = ReplaceTables(text);
        text = Regex.Replace(text, "</ac:parameter>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</p>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</h[1-6]>", "\n\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</tr>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"[ \t]+\r?\n", "\n");
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    public static string FromAdfOrString(JsonElement node)
    {
        return node.ValueKind switch
        {
            JsonValueKind.String => node.GetString() ?? string.Empty,
            JsonValueKind.Object => FromAdf(node).Trim(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => node.ToString()
        };
    }

    private static string ReplaceAcLinks(string html)
    {
        return Regex.Replace(html, @"<ac:link\b[^>]*>[\s\S]*?</ac:link>", match =>
        {
            var block = match.Value;
            var title = Attr(block, "ri:content-title");
            var space = Attr(block, "ri:space-key");
            var pageId = Attr(block, "ri:content-id");
            var filename = Attr(block, "ri:filename");
            var label = CData(block);
            if (string.IsNullOrWhiteSpace(label))
            {
                label = title ?? filename ?? pageId ?? "link";
            }

            var target = !string.IsNullOrWhiteSpace(pageId)
                ? $"pageId={pageId}"
                : !string.IsNullOrWhiteSpace(title)
                    ? (string.IsNullOrWhiteSpace(space) ? $"title={title}" : $"space={space}, title={title}")
                    : !string.IsNullOrWhiteSpace(filename)
                        ? $"attachment={filename}"
                        : "confluence";

            return $"[{Collapse(label)}]({target})";
        }, RegexOptions.IgnoreCase);
    }

    private static string ReplaceAnchors(string html, string? siteOrigin)
    {
        return Regex.Replace(html, @"<a\b[^>]*\bhref\s*=\s*[""']([^""']+)[""'][^>]*>([\s\S]*?)</a>", match =>
        {
            var href = WebUtility.HtmlDecode(match.Groups[1].Value.Trim());
            if (href.StartsWith('/') && !string.IsNullOrWhiteSpace(siteOrigin))
            {
                href = siteOrigin.TrimEnd('/') + href;
            }

            var label = Collapse(Regex.Replace(match.Groups[2].Value, "<[^>]+>", string.Empty));
            if (string.IsNullOrWhiteSpace(label))
            {
                label = href;
            }

            return $"[{label}]({href})";
        }, RegexOptions.IgnoreCase);
    }

    private static string ReplaceTables(string html)
    {
        var innerTable = new Regex(@"<table\b[^>]*>(?:(?!<table\b)[\s\S])*?</table>", RegexOptions.IgnoreCase);
        while (innerTable.IsMatch(html))
        {
            html = innerTable.Replace(html, match => ConvertTable(match.Value));
        }

        return html;
    }

    private static string ConvertTable(string tableHtml)
    {
        var rows = Regex.Matches(tableHtml, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
        if (rows.Count == 0)
        {
            return "\n";
        }

        var lines = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var cells = Regex.Matches(rows[i].Groups[1].Value, @"<t[hd]\b[^>]*>([\s\S]*?)</t[hd]>", RegexOptions.IgnoreCase);
            var values = cells.Select(cell => Collapse(Regex.Replace(cell.Groups[1].Value, "<[^>]+>", string.Empty))).ToArray();
            if (values.Length == 0)
            {
                continue;
            }

            lines.Add("| " + string.Join(" | ", values) + " |");
            if (i == 0)
            {
                lines.Add("| " + string.Join(" | ", values.Select(_ => "---")) + " |");
            }
        }

        return "\n" + string.Join("\n", lines) + "\n";
    }

    private static string? Attr(string fragment, string name)
    {
        var match = Regex.Match(fragment, Regex.Escape(name) + @"\s*=\s*""([^""]*)""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(fragment, Regex.Escape(name) + @"\s*=\s*'([^']*)'", RegexOptions.IgnoreCase);
        }

        return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static string? CData(string fragment)
    {
        var match = Regex.Match(fragment, @"<!\[CDATA\[([\s\S]*?)\]\]>", RegexOptions.IgnoreCase);
        return match.Success ? Collapse(match.Groups[1].Value) : null;
    }

    private static string Collapse(string value)
    {
        var text = WebUtility.HtmlDecode(value).Replace('\n', ' ').Replace('\r', ' ');
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    private static string FromAdf(JsonElement node)
    {
        var builder = new StringBuilder();
        WalkAdf(node, builder);
        return builder.ToString();
    }

    private static void WalkAdf(JsonElement node, StringBuilder builder)
    {
        if (node.ValueKind == JsonValueKind.String)
        {
            builder.Append(node.GetString());
            return;
        }

        if (node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = node.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
        if (type == "text" && node.TryGetProperty("text", out var textNode))
        {
            var text = textNode.GetString() ?? string.Empty;
            var href = LinkHref(node);
            builder.Append(href is null ? text : $"[{text}]({href})");
        }

        if (!node.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var child in content.EnumerateArray())
        {
            WalkAdf(child, builder);
            var childType = child.TryGetProperty("type", out var childTypeNode) ? childTypeNode.GetString() : null;
            if (childType is "paragraph" or "heading" or "listItem" or "blockquote" or "rule" or "tableRow")
            {
                builder.AppendLine();
            }
        }
    }

    private static string? LinkHref(JsonElement node)
    {
        if (!node.TryGetProperty("marks", out var marks) || marks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var mark in marks.EnumerateArray())
        {
            if (mark.TryGetProperty("type", out var type) && type.GetString() == "link"
                && mark.TryGetProperty("attrs", out var attrs)
                && attrs.TryGetProperty("href", out var href))
            {
                return href.GetString();
            }
        }

        return null;
    }
}
