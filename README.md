# McpJiraConfluence

MCP для Jira и Confluence. Транспорт HTTP, эндпоинт `POST /mcp`.

Клиент: `Authorization: Bearer <api-token>`.
Jira и Confluence: PAT на стороне сервера.

## Инструменты

Jira: поиск задач, чтение задачи (родитель, связи, подзадачи, вложения), файл вложения, список переходов, смена статуса.

Confluence: поиск, страница по id или по названию. В тексте страницы остаются ссылки и таблицы.

## Конфиг

Адреса и секреты в репозитории не хранятся. Переменные окружения или user secrets:

```
McpAuth__ApiToken
Jira__BaseUrl            https://jira.example.com/rest/api/2
Jira__Pat
Confluence__BaseUrl      https://confluence.example.com/rest/api
Confluence__Pat
```

Локально:

```
dotnet user-secrets set "McpAuth:ApiToken" "<token>" --project McpJiraConflu
dotnet user-secrets set "Jira:BaseUrl" "https://jira.example.com/rest/api/2" --project McpJiraConflu
dotnet user-secrets set "Jira:Pat" "<pat>" --project McpJiraConflu
dotnet user-secrets set "Confluence:BaseUrl" "https://confluence.example.com/rest/api" --project McpJiraConflu
dotnet user-secrets set "Confluence:Pat" "<pat>" --project McpJiraConflu
dotnet run --project McpJiraConflu --launch-profile https
```

Профиль `https`: `https://localhost:7089/mcp`. `GET /health` без токена.

Docker: те же переменные в `docker-compose.yml`. Файл в git не входит. Снаружи порт `5074`, в контейнере `8080`.

## Лицензия

Apache License 2.0, файл [LICENSE](LICENSE).
