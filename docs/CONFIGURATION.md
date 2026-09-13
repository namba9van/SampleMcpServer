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

Сообщение из разрешённого Telegram-чата превращается в durable queue job для Agent Host. Завершённый job автоматически отправляет результат обратно в тот же chat ID через notification channel `telegram`.

## Legacy/general tools

- `WEB_SEARCH_ENGINES=DuckDuckGo,Firecrawl`.
- `WEB_SEARCH_DUCKDUCKGO_REGION=wt-wt`.
- `WEB_SEARCH_FIRECRAWL_API_KEY` — optional.
- `GITHUB_TOKEN` — GitHub search token.

Для совместимости принимаются старые опечатанные имена `GUTHUB_TOKEN`, `WEB_SEARCH_FirecrawApiKey` и `WEB_SEARCH_duckduckgoRegion`, но новые конфигурации должны использовать имена выше.
