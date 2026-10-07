using Microsoft.Extensions.Options;
using McpJiraConflu.Atlassian;
using McpJiraConflu.Auth;
using McpJiraConflu.Options;
using McpJiraConflu.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<McpAuthOptions>()
    .BindConfiguration(McpAuthOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.ApiToken), "McpAuth:ApiToken обязателен.")
    .ValidateOnStart();

builder.Services
    .AddOptions<JiraOptions>()
    .BindConfiguration(JiraOptions.SectionName)
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Jira:BaseUrl должен быть абсолютным URI.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Pat), "Jira:Pat обязателен (PAT для REST API).")
    .ValidateOnStart();

builder.Services
    .AddOptions<ConfluenceOptions>()
    .BindConfiguration(ConfluenceOptions.SectionName)
    .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Confluence:BaseUrl должен быть абсолютным URI.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.Pat), "Confluence:Pat обязателен (PAT для REST API).")
    .ValidateOnStart();

builder.Services
    .AddAuthentication(ApiTokenAuthHandler.SchemeName)
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthHandler>(
        ApiTokenAuthHandler.SchemeName, _ => { });

builder.Services.AddAuthorization();

builder.Services.AddHttpClient<JiraClient>((sp, client) =>
    AtlassianHttp.Configure(client, sp.GetRequiredService<IOptions<JiraOptions>>().Value));

builder.Services.AddHttpClient<ConfluenceClient>((sp, client) =>
    AtlassianHttp.Configure(client, sp.GetRequiredService<IOptions<ConfluenceOptions>>().Value));

builder.Services.AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "Jira Confluence MCP",
            Version = "1.0.0"
        };
    })
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithTools<JiraTools>()
    .WithTools<ConfluenceTools>();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapMcp("/mcp").RequireAuthorization();

app.Run();
