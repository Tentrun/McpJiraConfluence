namespace McpJiraConflu.Options;

public sealed class McpAuthOptions
{
    public const string SectionName = "McpAuth";

    public string ApiToken { get; set; } = string.Empty;
}
