# SampleMcpServer

**SampleMcpServer** — консольный MCP-сервер на .NET 8 с транспортом `stdio`.
Проект предоставляет инструменты для вычислений, работы с файлами, времени,
веб-поиска, GitHub, локального RAG и потокового диалога двух моделей.

## Возможности

Основные MCP-инструменты:

- `random_number` — генерация случайного целого числа;
- `time` — получение локального и UTC-времени, даты, смещения часового пояса и временной метки;
- `calc` — арифметические операции;
- `file_operations` — создание, чтение и перечисление файлов;
- `internet_search` — параллельный поиск через несколько поисковых систем с фильтрацией, ранжированием и удалением дублей;
- `github_search` — поиск репозиториев и исходного кода через GitHub API;
- `rag` — поиск по локальным документам с использованием embedding-моделей и FAISS;
- `agent_dialog` — последовательный диалог моделей A → B → A → B с потоковыми MCP `notifications/progress`.

Все сообщения протокола MCP передаются через `stdin/stdout`.
Диагностические сообщения записываются в `stderr` и не смешиваются с MCP-протоколом.

## Структура проекта

```text
SampleMcpServer/
├── Program.cs
├── SampleMcpServer.csproj
├── SampleMcpServer.sln
├── README.md
├── AGENT_DIALOG.md
├── MCP_SELF_TEST.md
├── mcp.json
├── LICENSE
├── Tools/
│   ├── AgentDialogTools.cs
│   ├── CalcTools.cs
│   ├── FileOperationsTools.cs
│   ├── GitHubSearchTool.cs
│   ├── InternetSearchTools.cs
│   ├── RAGTool.cs
│   ├── RandomNumberTools.cs
│   └── TimeTools.cs
├── Services/
│   ├── AgentDialogService.cs
│   ├── EmbeddingService.cs
│   ├── LmStudioEndpoint.cs
│   ├── LmStudioModelDiscovery.cs
│   ├── RagDocument.cs
│   ├── RagDocumentLoader.cs
│   ├── RagFileFingerprint.cs
│   ├── RagIndexMetadata.cs
│   └── RagIndexService.cs
└── Utils/
    └── поисковые, файловые и RAG-вспомогательные классы
```

## Требования

- .NET 8 SDK;
- для RAG — доступный OpenAI-совместимый embedding endpoint, например LM Studio;
- для `github_search` рекомендуется `GITHUB_TOKEN`;
- для SearXNG нужен URL доступного экземпляра;
- API-ключи Firecrawl и Marginalia могут использоваться для персональных лимитов;
- для `agent_dialog` нужны две доступные OpenAI-совместимые chat-модели с поддержкой потокового `/chat/completions`.

## Сборка и запуск

В корне репозитория:

```powershell
dotnet restore
dotnet build
dotnet run
```

Обычный запуск использует `stdio`.
Не выводите диагностические сообщения в `stdout`.

## MCP Inspector

Для проверки через MCP Inspector:

```powershell
npx @modelcontextprotocol/inspector dotnet run --project .\SampleMcpServer.csproj
```

Если в текущем PowerShell-сеансе задана переменная `WEB_SEARCH_ENGINES`, она имеет
приоритет над отдельными `WEB_SEARCH_ENABLE_*`. Для проверки конфигурации из
`mcp.json` её можно удалить:

```powershell
Remove-Item Env:WEB_SEARCH_ENGINES -ErrorAction SilentlyContinue
```

## Конфигурация

Готовый пример находится в `mcp.json`. Файл не содержит реальных секретов.

### Поиск

Можно использовать отдельные переключатели:

```text
WEB_SEARCH_ENABLE_DUCKDUCKGO=true
WEB_SEARCH_ENABLE_YANDEX=false
WEB_SEARCH_ENABLE_BAIDU=true
WEB_SEARCH_ENABLE_MOJEEK=false
```

Если задана `WEB_SEARCH_ENGINES`, например:

```powershell
$env:WEB_SEARCH_ENGINES="DuckDuckGo,Baidu"
```

этот список полностью имеет приоритет над `WEB_SEARCH_ENABLE_*`.

### GitHub

Для GitHub API:

```text
GITHUB_TOKEN=<ваш токен>
```

Не добавляйте токен в репозиторий.

### RAG и embedding

Основные параметры:

```text
EMBEDD_ENDPOINT=http://127.0.0.1:1234/v1
EMBEDD_KEY=<ключ, если требуется>
EMBEDD_MODEL=<идентификатор embedding-модели>
```

Индекс RAG хранится вне репозитория:

```text
%LOCALAPPDATA%\SampleMcpServer\RagIndex\
```

## Потоковый диалог агентов

`agent_dialog` использует стандартный механизм MCP progress notifications.
Сервер не вводит отдельный протокол поверх MCP.

Клиент вызывает инструмент с `progressToken`. Сервер передаёт промежуточные
фрагменты ответа модели через `notifications/progress`, а в конце возвращает
полный стенографический текст диалога.

Схема:

```text
клиент
  │
  ├── agent_dialog + progressToken
  │
  ├── Agent A ──► progress
  ├── Agent B ──► progress
  ├── Agent A ──► progress
  └── Agent B ──► progress
          │
          └── финальный результат tools/call
```

Настройки:

```text
AGENT_A_ENDPOINT
AGENT_A_MODEL
AGENT_A_API_KEY
AGENT_A_TEMPERATURE

AGENT_B_ENDPOINT
AGENT_B_MODEL
AGENT_B_API_KEY
AGENT_B_TEMPERATURE
```

Пример для двух моделей в LM Studio:

```powershell
$env:AGENT_A_ENDPOINT="http://127.0.0.1:1234/v1/chat/completions"
$env:AGENT_A_MODEL="model-a"

$env:AGENT_B_ENDPOINT="http://127.0.0.1:1234/v1/chat/completions"
$env:AGENT_B_MODEL="model-b"

dotnet run
```

Модели могут находиться на разных OpenAI-совместимых endpoints.

### Модель доверия

Ответ одного агента не является системной инструкцией для другого агента.
Он передаётся как обычный внешний текст и должен рассматриваться как недоверенные
данные — по той же модели доверия, что и результаты веб-поиска.

Сервер ограничивает начальный запрос 32 000 символами и количество ходов 1–20.
Отмена выполняется через стандартный `CancellationToken` MCP.

Подробности находятся в `AGENT_DIALOG.md`.

## Встроенная самопроверка

Проект содержит автономный инспектор, поэтому базовую проверку можно выполнять
без стороннего MCP Inspector:

```powershell
SampleMcpServer.exe --self-test
```

Интерактивный режим:

```powershell
SampleMcpServer.exe --inspect
```

Для проверки реального потока `agent_dialog` нужны две настроенные модели:

```powershell
$env:MCP_AGENT_SELF_TEST="true"
$env:AGENT_A_MODEL="model-a"
$env:AGENT_B_MODEL="model-b"

SampleMcpServer.exe --self-test
```

Подробности находятся в `MCP_SELF_TEST.md`.

## Безопасность и публикация

Перед публикацией проверьте, что в Git нет секретов:

```powershell
git status
git diff --cached
```

Никогда не добавляйте в репозиторий:

- `GITHUB_TOKEN`;
- `AGENT_A_API_KEY`;
- `AGENT_B_API_KEY`;
- `EMBEDD_KEY`;
- `WEB_SEARCH_FIRECRAWL_API_KEY`;
- `WEB_SEARCH_MARGINALIA_API_KEY`;
- локальные индексы RAG;
- `.env` и локальные конфигурационные файлы.

Шаблон `mcp.json` содержит только пустые значения секретов.

## Публикация

Self-contained single-file приложение можно опубликовать командой:

```powershell
dotnet publish -c Release
```

Перед публикацией рекомендуется выполнить:

```powershell
dotnet clean
dotnet build
```

## Лицензия

Проект распространяется по лицензии MIT. Полный текст находится в `LICENSE`.

## Полный `mcp.json`

В репозитории файл `mcp.json` является готовым шаблоном для LM Studio. Он использует опубликованный Windows x64-файл `SampleMcpServer.exe`, а секреты оставлены пустыми. Перед запуском проверьте путь к `.exe` и заполните только необходимые ключи.

Содержимое файла:

```json
{
  "mcpServers": {
    "my-mcp-example": {
      "command": "C:\\Users\\user\\.lmstudio\\SampleMcpServer\\SampleMcpServer.exe",
      "env": {
        "WEB_SEARCH_ENABLE_DUCKDUCKGO": "true",
        "WEB_SEARCH_ENABLE_YANDEX": "false",
        "WEB_SEARCH_ENABLE_BAIDU": "true",
        "WEB_SEARCH_ENABLE_MOJEEK": "false",
        "WEB_SEARCH_ENABLE_MARGINALIA": "true",
        "WEB_SEARCH_ENABLE_QWANT": "false",
        "WEB_SEARCH_ENABLE_ECOSIA": "false",
        "WEB_SEARCH_ENABLE_SOGOU": "false",
        "WEB_SEARCH_ENABLE_SEARXNG": "false",
        "WEB_SEARCH_ENABLE_FIRECRAWL": "true",
        "WEB_SEARCH_DUCKDUCKGO_REGION": "ru-ru",
        "WEB_SEARCH_MARGINALIA_API_KEY": "",
        "WEB_SEARCH_FIRECRAWL_API_KEY": "",
        "WEB_SEARCH_SEARXNG_URL": "",
        "GITHUB_TOKEN": "",
        "EMBEDD_ENDPOINT": "http://127.0.0.1:1234/v1",
        "EMBEDD_KEY": "",
        "EMBEDD_MODEL": ""
      }
    }
  }
}
```

### Публикация EXE

Для Windows x64:

```powershell
dotnet clean
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true
```

После публикации укажите фактический путь к `SampleMcpServer.exe` в `mcp.json`.

### Перед отправкой на GitHub

```powershell
dotnet clean
dotnet restore
dotnet build
git status
git diff --cached
```

Не публикуйте реальные значения `GITHUB_TOKEN`, `EMBEDD_KEY`, `WEB_SEARCH_FIRECRAWL_API_KEY`, `WEB_SEARCH_MARGINALIA_API_KEY` или ключи агентов.
