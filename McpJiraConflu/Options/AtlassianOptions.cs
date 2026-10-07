namespace McpJiraConflu.Options;

public abstract class AtlassianOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Pat { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
}

public sealed class JiraOptions : AtlassianOptions
{
    public const string SectionName = "Jira";
}

public sealed class ConfluenceOptions : AtlassianOptions
{
    public const string SectionName = "Confluence";
}
