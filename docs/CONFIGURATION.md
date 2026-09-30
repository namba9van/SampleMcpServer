# Configuration

Все настройки передаются через environment variables. Полный шаблон — `.env.example`.

## Embeddings

- `EMBEDD_ENDPOINT` — base URL OpenAI-compatible API, например `http://127.0.0.1:1234/v1`.
- `EMBEDD_MODEL` — embedding model.
- `EMBEDD_KEY` — optional API key.

Если endpoint/model не заданы, используется локальный hashing fallback.

## Memory

- `MEMORY_DB_PATH` — путь к SQLite. Если пусто, используется каталог LocalApplicationData.
- `MEMORY_SEARCH_*_WEIGHT` — веса ranked recall.
- `MEMORY_SEARCH_FRESHNESS_HALF_LIFE_DAYS` — half-life freshness signal.
- `MEMORY_MAINTENANCE_*` — фоновая очистка TTL и почти точных дублей.
- `MEMORY_AUTO_CONSOLIDATE` — автоматическая semantic consolidation; по умолчанию выключена.

## Agent Host

- `AGENT_HOST_ENABLED=true|false`.
- `AGENT_HOST_MODE=embedded|external`.
- `AGENT_HOST_MODEL_ENDPOINT` — полный chat completions endpoint.
- `AGENT_HOST_MODEL` — chat model.
- `AGENT_HOST_MODEL_API_KEY` — optional key.
- `AGENT_HOST_MAX_CONCURRENCY` — число worker'ов.
- `AGENT_HOST_LEASE_SECONDS` — lease job.
- `AGENT_HOST_MAX_TOOL_TURNS` — bounded tool loop.

`external` означает, что сервер только хранит queue jobs; внешний orchestrator забирает их через `trigger_job_claim`.

## Trigger actions

- `TRIGGER_RUNNER_INTERVAL_MS` — polling/dispatch interval.
- `TRIGGER_WEBHOOK_ALLOWLIST` — comma-separated hosts для trigger webhook.
- `TRIGGER_MODEL_*` — direct-model mode; queue + Agent Host предпочтительнее, если модели нужны tools.

## External capabilities

- `AGENT_HTTP_ALLOWLIST` — hosts, которые разрешено читать Agent Host.
- `AGENT_FILESYSTEM_ROOT` — sandbox root для файловых действий runtime.
- `AGENT_GITHUB_TOKEN` — GitHub token для background GitHub read.

Наличие env-переменной не включает capability автоматически: policy хранится отдельно и управляется `agent_capability_set`.

## Notifications

- `AGENT_NOTIFICATION_WEBHOOK_ALLOWLIST` — разрешённые webhook hosts.
- `AGENT_NOTIFICATION_WEBHOOK_BEARER` — optional bearer token.
- `AGENT_NOTIFICATION_MAX_ATTEMPTS` — retry limit.

Канал `telegram` использует настройки Telegram bridge ниже и доставляет только в allow-listed chat ID.

## Telegram bridge

- `TELEGRAM_BOT_ENABLED=true|false` — включает polling bridge.
- `TELEGRAM_BOT_TOKEN` — токен BotFather. Не коммитьте реальное значение.
- `TELEGRAM_ALLOWED_CHAT_IDS` — comma-separated allowlist chat/channel IDs. Пустой allowlist блокирует все входящие и исходящие Telegram-действия.
- `TELEGRAM_DEFAULT_CHAT_ID` — optional destination для `agent_notify` с channel `telegram`, если destination не передан.
- `TELEGRAM_DROP_PENDING_UPDATES` — при старте пропускает старые updates, чтобы бот не переиграл историю.
- `TELEGRAM_ACK_QUEUED` — отправляет короткое подтверждение после постановки Telegram-сообщения в agent queue.
- `TELEGRAM_POLL_TIMEOUT_SECONDS` — Telegram long-poll timeout.
- `TELEGRAM_POLL_INTERVAL_MS` — backoff после ошибки polling.
- `TELEGRAM_CONTEXT_TOKEN_BUDGET` — budget для memory_context, который прикладывается к Telegram-задаче.
- `TELEGRAM_DELIVERY=queue|inbox|both` — куда попадает сообщение из разрешённого чата. `queue` (по умолчанию) — durable job для Agent Host; `inbox` — таблица `telegram_inbox` в `MEMORY_DB_PATH`, которую подключённый MCP-клиент читает инструментами `telegram_messages_wait` / `telegram_messages_poll` и подтверждает `telegram_messages_ack`, ack «Queued as agent job» при этом не отправляется; `both` — и то и другое.
- Несколько процессов сервера с одним токеном (Claude Desktop запускает по экземпляру на окно/сессию): polling ведёт только процесс, удерживающий файл `<MEMORY_DB_PATH>.telegram-poll.lock`; остальные ждут и подхватывают, когда он завершится. Inbox и очередь общие через SQLite, инструменты работают из любого экземпляра. Ответ 409 от Telegram означает второго получателя вне этой блокировки (другая машина или другой `MEMORY_DB_PATH`).
- `TELEGRAM_INBOX_FILTER=all|addressed` — `all` (по умолчанию) принимает любые сообщения разрешённого чата; `addressed` — только адресованные боту: ответ (reply) на сообщение бота или упоминание `@бот`; остальной разговор группы игнорируется (пишется в audit как `ignored`). Признак адресации приходит в поле `Addressing` каждого сообщения inbox: `chat`, `reply_to_bot`, `mention`, `reply` (ответ другому участнику).

Сообщение из разрешённого Telegram-чата превращается в durable queue job для Agent Host. Завершённый job автоматически отправляет результат обратно в тот же chat ID через notification channel `telegram`.

## Shell tools (operator)

Операторские инструменты `shell_run`, `shell_run_to_file`, `shell_info`. Это не capability `shell` Agent Host: инструменты вызывает подключённая модель/оператор напрямую, governance Agent Host на них не распространяется.

- `SHELL_ENABLED=true|false` — включает инструменты. По умолчанию `false`: `shell_run` и `shell_run_to_file` возвращают ошибку, `shell_info` сообщает `enabled=false`.
- `SHELL_ALLOWED_ROOTS` — список абсолютных каталогов через `;`. Рабочая директория команды и файл вывода `shell_run_to_file` должны лежать внутри одного из них. Пусто — текущий каталог процесса.
- `SHELL_DEFAULT_CWD` — рабочая директория по умолчанию (должна быть внутри allowed roots). Пусто — первый allowed root.
- `SHELL_PROGRAM` — исполняемый файл оболочки: `powershell.exe`, `pwsh`, `cmd.exe`, `/bin/sh`, `/bin/bash`. По умолчанию `powershell.exe` на Windows и `/bin/sh` в остальных ОС.
- `SHELL_TIMEOUT_SECONDS` — таймаут по умолчанию и верхняя граница `timeoutSeconds` (по умолчанию 120, жёсткий предел 3600). По истечении убивается всё дерево процессов.
- `SHELL_MAX_OUTPUT_CHARS` — сколько символов stdout/stderr возвращается inline (по умолчанию 60000); остальное обрезается с пометкой.

## Legacy/general tools

- `WEB_SEARCH_ENGINES=DuckDuckGo,Firecrawl`.
- `WEB_SEARCH_DUCKDUCKGO_REGION=wt-wt`.
- `WEB_SEARCH_FIRECRAWL_API_KEY` — optional.
- `GITHUB_TOKEN` — GitHub search token.

Для совместимости принимаются старые опечатанные имена `GUTHUB_TOKEN`, `WEB_SEARCH_FirecrawApiKey` и `WEB_SEARCH_duckduckgoRegion`, но новые конфигурации должны использовать имена выше.
