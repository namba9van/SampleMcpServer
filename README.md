# SampleMcpServer — LM Studio / MCP / RAG

## Текущее состояние

Рабочая версия проекта находится на ветке:

```text
fix/some-change
```

Последний зафиксированный рабочий коммит:

```text
b275889 Implement working LM Studio RAG index
```

Рабочее дерево после этого коммита должно оставаться чистым.

## Что реализовано

### MCP + LM Studio

SampleMcpServer запускается из LM Studio как MCP server через stdio.

LM Studio endpoint не задаётся жёстко в `mcp.json`.

Сервер автоматически:

1. получает порт LM Studio через:
   `lms server status --json --quiet`;
2. получает локальные IPv4-адреса Windows;
3. проверяет OpenAI-compatible endpoint `/v1/models`;
4. учитывает включённую в LM Studio authentication;
5. выбирает рабочий endpoint.

Ручной override `EMBEDD_ENDPOINT` поддерживается, но в обычной конфигурации не требуется.

### Автоматический выбор embedding-модели

Embedding-модель не должна быть жёстко задана.

Через native LM Studio API `/api/v1/models` сервер получает список моделей и выбирает модель с `type = embedding`.

Если задан `EMBEDD_MODEL`, он используется как ручной override.

Текущая проверенная модель LM Studio:

```text
text-embedding-nomic-embed-text-v1.5
```

Фактическая размерность embedding:

```text
768
```

### Authentication

При включённом в LM Studio `Require Authentication` используется:

```text
EMBEDD_KEY
```

Он передаётся как:

```http
Authorization: Bearer <token>
```

## Persistent RAG

RAG больше не пересчитывает embeddings неизменившихся файлов при каждом запросе.

Для файлов используется fingerprint:

```text
полный путь + размер + LastWriteTimeUtc
```

На основании fingerprint определяется, нужно ли переиндексировать файл.

Если файл не изменился:

```text
RAG changed files: 0
```

и embedding документа повторно не отправляется в LM Studio.

Если файл изменился, переиндексируется только этот файл.

### Хранение индекса

В каталоге `%LOCALAPPDATA%` создаются persistent данные RAG:

```text
%LOCALAPPDATA%\SampleMcpServer\RagIndex\
    metadata.json
    documents.json
```

`documents.json` содержит текст документов и уже рассчитанные embeddings.

FAISS остаётся in-memory и при запуске восстанавливается из сохранённых embeddings.

### Metadata

В metadata сохраняется как минимум:

```text
EmbeddingModel
EmbeddingDimension
Files
    FilePath
    Fingerprint
    Sections
        Id
        Content
        Embedding
```

Это позволяет обнаруживать:

- смену embedding-модели;
- смену размерности embedding;
- изменение файлов;
- удаление файлов.

При несовместимости embedding-модели или размерности индекс перестраивается.

## Поддерживаемые файлы

RAG loader использует:

- PDF
- DOCX
- XLSX
- PPTX
- TXT
- MD
- CSV
- LOG
- JSON
- XML
- HTML
- YAML
- исходный код и другие текстовые файлы из настроенного набора расширений.

Для известных текстовых расширений файл читается напрямую; `FileUtils.IsPlainText()` используется как fallback для неизвестных расширений.

## Конфигурация MCP

Обычная конфигурация `my-mcp-example` не должна содержать `EMBEDD_ENDPOINT` или обязательный `EMBEDD_MODEL`.

Минимально для RAG требуется:

```json
{
  "mcpServers": {
    "my-mcp-example": {
      "command": "C:\\Users\\user\\.lmstudio\\SampleMcpServer\\SampleMcpServer.exe",
      "env": {
        "EMBEDD_KEY": "<LM-Studio-API-token>"
      }
    }
  }
}
```

`EMBEDD_MODEL` можно добавить как ручной override.

## Сборка

Обычная проверка:

```powershell
dotnet build
```

Публикация Windows x64:

```powershell
dotnet publish `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o "$env:USERPROFILE\\.lmstudio\\SampleMcpServer"
```

Для FAISS native runtime в проекте используется:

```xml
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

После публикации в каталоге runtime должна присутствовать:

```text
FaissNetNative.dll
```

## Проверенный RAG-сценарий

Файл:

```text
C:\Users\user\SampleMcpServer\test.txt
```

Запрос:

```text
SampleMcpServer
```

Проверенный результат:

```text
Search results count: 1
Score ≈ 0.6932352781
```

При повторном запросе без изменения файла:

```text
RAG changed files: 0
```

и document embedding повторно не создаётся.

## Важное замечание о cold start

В текущей рабочей версии остаётся отдельная проблема: при новом MCP-сеансе LM Studio иногда наблюдается примерно 60-секундный timeout до начала фактической обработки RAG. При этом после начала обработки сам RAG завершается успешно и возвращает результат.

Эта проблема **не считается решённой** в текущем коммите `b275889` и должна исследоваться отдельно, не смешивая изменения с уже работающим persistent RAG.

## Безопасная точка возврата

Если дальнейший эксперимент с cold start окажется неудачным:

```powershell
git reset --hard b275889
```

После этого проект возвращается к последней подтверждённо рабочей версии persistent RAG.
