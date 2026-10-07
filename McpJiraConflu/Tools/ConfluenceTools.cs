using System.ComponentModel;
using McpJiraConflu.Atlassian;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpJiraConflu.Tools;

[McpServerToolType]
public sealed class ConfluenceTools
{
    private readonly ConfluenceClient _confluence;

    public ConfluenceTools(ConfluenceClient confluence)
    {
        _confluence = confluence;
    }

    [McpServerTool(Name = "confluence_search", Title = "Поиск в Confluence", ReadOnly = true, OpenWorld = true)]
    [Description("Ищет страницы Confluence. Можно передать обычный текст или CQL (например type=page AND space=DEV)")]
    public async Task<string> Search(
        [Description("Текст поиска или CQL-запрос")] string query,
        [Description("Максимум результатов (1-50, по умолчанию 20)")] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _confluence.SearchAsync(query, limit, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Confluence search: {ex.Message}");
        }
    }

    [McpServerTool(Name = "confluence_get_page", Title = "Чтение страницы Confluence", ReadOnly = true, OpenWorld = true)]
    [Description("Читает страницу Confluence по id. В тексте сохраняются ссылки и таблицы")]
    public async Task<string> GetPage(
        [Description("Id страницы Confluence")] string pageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _confluence.GetPageAsync(pageId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Confluence get page: {ex.Message}");
        }
    }

    [McpServerTool(Name = "confluence_get_page_by_title", Title = "Страница Confluence по названию", ReadOnly = true, OpenWorld = true)]
    [Description("Читает страницу Confluence по ключу пространства и точному названию")]
    public async Task<string> GetPageByTitle(
        [Description("Ключ пространства, например DEV")] string spaceKey,
        [Description("Точное название страницы")] string title,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _confluence.GetPageByTitleAsync(spaceKey, title, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Confluence get page by title: {ex.Message}");
        }
    }
}
