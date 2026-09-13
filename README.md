# SampleMcpServer Agent Runtime

Полноценный локальный MCP-сервер на .NET 8: обычные MCP-инструменты, локальный RAG, долговременная SQLite-память модели, события, триггеры, автономный Agent Host и Telegram bridge для удалённого общения с подключёнными моделями.

Проект сохраняет исходную идею SampleMcpServer - дать локальной модели полезные инструменты для файлов, интернета, GitHub и RAG - и расширяет её управляемой долговременной памятью, durable job queue, capability governance, audit trail и безопасной удалённой точкой входа через Telegram.

## Что умеет

### MCP tools

Сервер предоставляет 62 MCP-инструмента:

- калькулятор и текущее время;
- чтение, создание и перечисление локальных файлов;
- поиск в интернете через DuckDuckGo и опционально Firecrawl;
- поиск репозиториев и кода через GitHub API;
- локальный RAG по PDF, DOCX, XLSX, PPTX, тексту и исходному коду;
- долговременная память модели;
- события, watches, triggers и queue jobs;
- autonomous runtime capabilities, approvals, schedules, notifications и audit;
- Telegram status/send tools.

Полный каталог инструментов находится в [docs/TOOLS.md](docs/TOOLS.md).

### Долговременная память модели

Память хранится в SQLite и доступна модели через высокоуровневые MCP-инструменты. SQL модели не показывается.

- `memory_remember`, `memory_get`, `memory_update`, `memory_delete`;
- семантический поиск по embeddings;
- компактный `memory_recall` для экономии токенов;
- `memory_context` собирает готовый релевантный контекст под заданный token budget;
- importance, TTL, freshness и access ranking;
- консолидация повторяющихся эпизодов в summary-memory;
- provenance/evidence и confidence;
- связи между воспоминаниями (`depends_on`, `part_of`, `owned_by` и другие);
- версии, supersession и явное разрешение конфликтов.

Если embedding endpoint не настроен, сервер использует детерминированный локальный hashing-vector fallback. Для качественного semantic recall рекомендуется OpenAI-compatible embedding endpoint, например LM Studio.

### События и автономные действия

Изменения памяти могут порождать durable events. На события ставятся watches/triggers, после чего создаются задания агенту.

```text
Memory change
    -> Event Bus
    -> Trigger
    -> Durable job
    -> Agent Host
    -> memory_context
    -> LLM
    -> governed action
    -> memory / notification / audit
```

Agent Host запускается вместе с сервером как `BackgroundService`. Задания используют lease, поэтому после падения процесса просроченное незавершённое задание может быть безопасно захвачено снова.

### Telegram bridge

Опциональный Telegram bridge позволяет удалённо ставить задачи подключённым моделям из allow-listed Telegram-чата или канала. Бот читает сообщения через polling, создаёт durable queue job для Agent Host и отправляет результат обратно в тот же chat ID через notification channel `telegram`.

Для включения задайте `TELEGRAM_BOT_ENABLED=true`, `TELEGRAM_BOT_TOKEN` и `TELEGRAM_ALLOWED_CHAT_IDS`. Для каналов Telegram бот должен быть добавлен в канал с нужными правами, а ID канала должен быть в allowlist.

### Governance и безопасность

Автономный агент не получает все права автоматически. Для внешних действий используется capability policy:

- `memory`;
- `http`;
- `github`;
- `filesystem`;
- `shell`;
- `scheduler`;
- уведомления.

Capability может быть разрешён, запрещён или требовать approval. HTTP/webhook ограничиваются allowlist. Файловые действия ограничиваются отдельным workspace. Shell ограничен рабочей директорией и timeout. Все значимые действия пишутся в audit log.

## Быстрый запуск

Требуется .NET 8 SDK.

```bash
dotnet restore
dotnet build
dotnet run
```

Сервер использует stdio transport. Настройки обычно передаются через `mcp.json` в блоке `env`. Да, Telegram, модель, embeddings, память и allowlists можно хранить именно там, но реальные токены не стоит коммитить в GitHub.

Если автономный Agent Host пока не нужен:

```text
AGENT_HOST_ENABLED=false
```

Полный список настроек также находится в [`.env.example`](.env.example) и [docs/CONFIGURATION.md](docs/CONFIGURATION.md).

## Полный пример mcp.json

Ниже пример полного `mcp.json` для локального запуска. Замените пути, model names, токены и chat IDs на свои значения.

```json
{
  "mcpServers": {
    "sample-agent-runtime": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "C:\\Users\\Zver\\Documents\\Codex\\SampleMcpServer-complete-handoff\\SampleMcpServer.csproj"
      ],
      "env": {
        "WEB_SEARCH_ENGINES": "DuckDuckGo",
        "WEB_SEARCH_DUCKDUCKGO_REGION": "wt-wt",
        "WEB_SEARCH_FIRECRAWL_API_KEY": "",
        "GITHUB_TOKEN": "",

        "MEMORY_DB_PATH": "",
        "EMBEDD_ENDPOINT": "http://127.0.0.1:1234/v1",
        "EMBEDD_MODEL": "text-embedding-model",
        "EMBEDD_KEY": "",

        "MEMORY_SEARCH_SEMANTIC_WEIGHT": "0.65",
        "MEMORY_SEARCH_IMPORTANCE_WEIGHT": "0.15",
        "MEMORY_SEARCH_FRESHNESS_WEIGHT": "0.12",
        "MEMORY_SEARCH_ACCESS_WEIGHT": "0.08",
        "MEMORY_SEARCH_FRESHNESS_HALF_LIFE_DAYS": "30",

        "MEMORY_MAINTENANCE_INTERVAL_MINUTES": "60",
        "MEMORY_MAINTENANCE_INITIAL_DELAY_SECONDS": "30",
        "MEMORY_MAINTENANCE_DUPLICATE_THRESHOLD": "0.995",
        "MEMORY_MAINTENANCE_MAX_SCAN": "500",
        "MEMORY_AUTO_CONSOLIDATE": "false",
        "MEMORY_AUTO_CONSOLIDATE_NAMESPACES": "",
        "MEMORY_AUTO_CONSOLIDATE_MIN_CLUSTER": "4",
        "MEMORY_AUTO_CONSOLIDATE_MAX_SOURCES": "8",
        "MEMORY_AUTO_CONSOLIDATE_SIMILARITY": "0.82",
        "MEMORY_AUTO_CONSOLIDATE_SOURCE_IMPORTANCE_FACTOR": "0.65",

        "TRIGGER_RUNNER_INTERVAL_MS": "750",
        "TRIGGER_WEBHOOK_ALLOWLIST": "",

        "TRIGGER_MODEL_ENDPOINT": "http://127.0.0.1:1234/v1/chat/completions",
        "TRIGGER_MODEL_MODEL": "",
        "TRIGGER_MODEL_API_KEY": "",

        "AGENT_HOST_ENABLED": "true",
        "AGENT_HOST_MODE": "embedded",
        "AGENT_HOST_MAX_CONCURRENCY": "2",
        "AGENT_HOST_IDLE_MS": "500",
        "AGENT_HOST_LEASE_SECONDS": "180",
        "AGENT_HOST_MAX_TOOL_TURNS": "8",
        "AGENT_HOST_MAX_OUTPUT_TOKENS": "900",
        "AGENT_HOST_NOTIFY_ON_COMPLETION": "true",
        "AGENT_HOST_MODEL_ENDPOINT": "http://127.0.0.1:1234/v1/chat/completions",
        "AGENT_HOST_MODEL": "your-chat-model",
        "AGENT_HOST_MODEL_API_KEY": "",

        "AGENT_RUNTIME_TICK_MS": "1000",
        "AGENT_NOTIFICATION_MAX_ATTEMPTS": "5",

        "AGENT_FILESYSTEM_ROOT": "./agent-workspace",
        "AGENT_HTTP_ALLOWLIST": "",
        "AGENT_GITHUB_TOKEN": "",

        "AGENT_NOTIFICATION_WEBHOOK_ALLOWLIST": "",
        "AGENT_NOTIFICATION_WEBHOOK_BEARER": "",

        "TELEGRAM_BOT_ENABLED": "false",
        "TELEGRAM_BOT_TOKEN": "123456789:replace-with-your-bot-token",
        "TELEGRAM_ALLOWED_CHAT_IDS": "-1001234567890,123456789",
        "TELEGRAM_DEFAULT_CHAT_ID": "-1001234567890",
        "TELEGRAM_DROP_PENDING_UPDATES": "true",
        "TELEGRAM_ACK_QUEUED": "true",
        "TELEGRAM_POLL_TIMEOUT_SECONDS": "25",
        "TELEGRAM_POLL_INTERVAL_MS": "1000",
        "TELEGRAM_CONTEXT_TOKEN_BUDGET": "1800"
      }
    }
  }
}
```

Минимальный Telegram-набор:

```json
{
  "TELEGRAM_BOT_ENABLED": "true",
  "TELEGRAM_BOT_TOKEN": "token-from-botfather",
  "TELEGRAM_ALLOWED_CHAT_IDS": "-1001234567890",
  "AGENT_HOST_MODEL_ENDPOINT": "http://127.0.0.1:1234/v1/chat/completions",
  "AGENT_HOST_MODEL": "your-chat-model"
}
```

## Как модель использует память

Для долговременного контекста предпочтительный путь такой:

```text
current user request
    -> memory_context (например, 2400 токенов)
    -> релевантные устойчивые воспоминания
    -> модель выполняет задачу
    -> durable changes сохраняются через memory_remember/update
```

Это позволяет не тащить в каждый запрос огромную историю прошлых разговоров. Сам MCP-сервер не управляет conversation window клиента, поэтому максимальная экономия получается, когда host хранит короткую историю текущего диалога, а долговременный контекст берёт из памяти.

## RAG

`rag_search` индексирует выбранный файл или каталог на время запроса и ищет смыслово близкие фрагменты. Поддерживаются:

- PDF - через PdfPig;
- DOCX / XLSX / PPTX - извлечение текста из Open XML;
- txt/md/json/xml/csv/log и распространённые исходники.

RAG и Agent Memory используют OpenAI-compatible embeddings. При отсутствии endpoint работает локальный fallback, но качество поиска ниже.

## Структура

```text
Services/
  ModelMemoryService.cs             durable memory + event log
  TriggerAutomationService.cs       trigger -> job bridge
  AgentHostService.cs               embedded autonomous worker
  RuntimeGovernanceService.cs       capabilities, approvals, scheduler, notifications, audit
  TelegramBotService.cs             Telegram polling bridge + outbound messages
  MemoryBackgroundMaintenanceService.cs
  EmbeddingService.cs
  RagDocumentLoader.cs
  RagIndexService.cs
Tools/
  MemoryTools.cs
  TriggerActionTools.cs
  RuntimeTools.cs
  TelegramTools.cs
  RAGTool.cs
  ... legacy/general MCP tools
docs/
  ARCHITECTURE.md
  CONFIGURATION.md
  SECURITY.md
  MEMORY_POLICY.md
  TOOLS.md
```

## Проверка перед публикацией

```bash
python scripts/validate_repo.py
dotnet restore
dotnet build --configuration Release
```

GitHub Actions выполняет те же build/static-validation шаги.

## Важные ограничения

- Autonomous runtime - это инфраструктура для агента, а не security sandbox уровня ОС.
- Не включайте `shell`, filesystem write или произвольный HTTP без осознанной policy.
- Telegram bot token, GitHub token и model API keys не должны попадать в репозиторий.
- Telegram bridge принимает и отправляет сообщения только для chat IDs из `TELEGRAM_ALLOWED_CHAT_IDS`.
- Memory ranking оценивает полезность/релевантность, а не истинность факта.
- Confidence и evidence помогают модели работать с источниками, но не являются автоматической гарантией достоверности.

Подробнее: [архитектура](docs/ARCHITECTURE.md), [безопасность](docs/SECURITY.md), [конфигурация](docs/CONFIGURATION.md), [политика памяти](docs/MEMORY_POLICY.md).

## Upstream

Исходная публичная реализация, на которой основан набор базовых возможностей: `virex-84/SampleMcpServer` (локальный RAG, web search, GitHub tools). В этой версии RAG-слой упрощён по зависимостям и переписан на единый OpenAI-compatible embedding сервис, а agent runtime добавлен отдельным слоем.

## License

MIT. См. [LICENSE](LICENSE).
