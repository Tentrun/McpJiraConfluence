using System.ComponentModel;
using McpJiraConflu.Atlassian;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace McpJiraConflu.Tools;

[McpServerToolType]
public sealed class JiraTools
{
    private readonly JiraClient _jira;

    public JiraTools(JiraClient jira)
    {
        _jira = jira;
    }

    [McpServerTool(Name = "jira_search_issues", Title = "Поиск задач Jira", ReadOnly = true, OpenWorld = true)]
    [Description("Ищет задачи Jira по JQL. Возвращает ключ, название, статус, исполнителя, тип и приоритет")]
    public async Task<string> SearchIssues(
        [Description("JQL-запрос, например: project = ABC AND status = \"In Progress\"")] string jql,
        [Description("Максимум задач (1-50, по умолчанию 20)")] int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jira.SearchIssuesAsync(jql, maxResults, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Jira search: {ex.Message}");
        }
    }

    [McpServerTool(Name = "jira_get_issue", Title = "Чтение задачи Jira", ReadOnly = true, OpenWorld = true)]
    [Description("Возвращает подробности задачи Jira по ключу: описание, статус, комментарии, исполнитель, родитель, связи, подзадачи и вложения")]
    public async Task<string> GetIssue(
        [Description("Ключ задачи, например PROJ-123")] string issueKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jira.GetIssueAsync(issueKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Jira get issue: {ex.Message}");
        }
    }

    [McpServerTool(Name = "jira_get_attachment", Title = "Вложение задачи Jira", ReadOnly = true, OpenWorld = true)]
    [Description("Скачивает вложение задачи Jira по id из списка attachments. Текст возвращает как text, небольшой файл как base64")]
    public async Task<string> GetAttachment(
        [Description("Id вложения из поля attachments задачи")] string attachmentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jira.GetAttachmentAsync(attachmentId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Jira attachment: {ex.Message}");
        }
    }

    [McpServerTool(Name = "jira_get_transitions", Title = "Доступные переходы Jira", ReadOnly = true, OpenWorld = true)]
    [Description("Список доступных переходов статуса для задачи Jira")]
    public async Task<string> GetTransitions(
        [Description("Ключ задачи, например PROJ-123")] string issueKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jira.GetTransitionsAsync(issueKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Jira transitions: {ex.Message}");
        }
    }

    [McpServerTool(Name = "jira_transition_issue", Title = "Смена статуса Jira", Destructive = true, OpenWorld = true)]
    [Description("Переводит задачу Jira в другой статус. Можно указать id перехода, имя перехода или целевой статус")]
    public async Task<string> TransitionIssue(
        [Description("Ключ задачи, например PROJ-123")] string issueKey,
        [Description("Id перехода, имя перехода или целевой статус")] string transition,
        [Description("Необязательный комментарий к переходу")] string? comment = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _jira.TransitionIssueAsync(issueKey, transition, comment, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new McpException($"Jira transition: {ex.Message}");
        }
    }
}
