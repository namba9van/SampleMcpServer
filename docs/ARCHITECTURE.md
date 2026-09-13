# Architecture

## Главная идея

Сервер разделён на четыре уровня:

1. **MCP tools** — интерфейс, который видит модель.
2. **Memory + Event Bus** — долговременное состояние и события.
3. **Agent Runtime** — очередь, scheduler, triggers и Agent Host.
4. **Governance** — capabilities, approvals, notifications и audit.

## Memory

`ModelMemoryService` хранит записи в SQLite. Каждая запись имеет namespace/key, content, metadata, embedding, importance, TTL, версию и статистику обращений.

Semantic recall сначала отбирает записи по embedding similarity, затем ранжирует их с учётом importance, freshness и access signal. `memory_context` ограничивает выдачу приблизительным token budget.

Relations хранят графовые связи. Конфликтные факты не обязаны перезаписывать друг друга: можно сохранить обе версии и позже явно разрешить конфликт или отметить supersession.

## Event Bus и triggers

Memory watches порождают durable events. `TriggerAutomationService` сопоставляет события с actions и создаёт durable runs/jobs.

Поддерживаются режимы:

- `queue` — рекомендуемый путь для полноценного agent host;
- `webhook` — доставка внешнему оркестратору;
- `model` — прямой вызов OpenAI-compatible chat endpoint без внешнего tool loop.

## Agent Host

`AgentHostService` — hosted service внутри процесса. Он:

1. atomically claim'ит queue job;
2. выдаёт ему lease;
3. получает `memory_context`;
4. вызывает настроенную chat model;
5. выполняет bounded tool loop;
6. проверяет capability/approval перед внешним действием;
7. завершает либо fail'ит job;
8. при необходимости создаёт уведомление.

Просроченный lease позволяет восстановить незавершённое задание после restart/crash.

## Governance

`RuntimeGovernanceService` хранит в той же SQLite базе:

- capability policy;
- one-shot approvals;
- evidence/provenance;
- schedules;
- notification outbox;
- audit records.

Scheduler создаёт обычные durable jobs, а не отдельный тип исполнения. Поэтому retry/recovery одинаковы для событий и задач по времени.

## RAG

RAG не связан с durable memory: это ad-hoc поиск по локальному файлу/каталогу. Документы извлекаются в chunks, embedding строится через `EmbeddingService`, затем выполняется cosine search.

Durable memory, наоборот, предназначена для cross-session состояния модели.
