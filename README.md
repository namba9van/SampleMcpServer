# SampleMcpServer

SampleMcpServer — консольный MCP-сервер на .NET 8, использующий stdio-транспорт MCP, инструменты поиска и файловых операций, интеграцию с LM Studio и локальный persistent RAG с FAISS.

## Текущая структура

В проекте оставлена одна рабочая реализация:
Структура проекта:

```text
SampleMcpServer/
├── Program.cs
├── SampleMcpServer.csproj
├── SampleMcpServer.sln
├── Tools/
│   ├── CalcTools.cs
│   ├── FileOperationsTools.cs
│   ├── GitHubSearchTool.cs
│   ├── InternetSearchTools.cs
│   ├── RAGTool.cs
│   └── RandomNumberTools.cs
├── Services/
│   ├── EmbeddingService.cs
│   ├── LmStudioEndpoint.cs
│   ├── LmStudioModelDiscovery.cs
│   ├── RagDocument.cs
│   ├── RagDocumentLoader.cs
│   ├── RagFileFingerprint.cs
│   ├── RagIndexMetadata.cs
│   └── RagIndexService.cs
└── Utils/
    ├── BaiduSearch.cs
    ├── DuckDuckGoSearch.cs
    ├── FaissVectorStore.cs
    ├── FileUtils.cs
    ├── FirecrawlSearch.cs
    ├── MyWordExtractor.cs
    └── WebPageLoader.cs
```


## Запуск как MCP-сервера

Сервер запускается как обычное консольное приложение и использует stdio:

```text
MCP client <-> stdin/stdout <-> SampleMcpServer
```

Логи направляются в stderr, чтобы stdout оставался выделенным под сообщения MCP-протокола.

Для регистрации MCP-инструментов сервер использует:

- `random_number` — генерация случайного целого числа;
- `time` — получение текущего локального времени, UTC, смещения часового пояса, даты и ISO 8601 timestamp;
- `calc` — арифметические операции;
- `file_operations` — создание, чтение и перечисление файлов;
- `internet_search` — агрегированный поиск через настроенные поисковые движки;
- `github_search` — поиск репозиториев и исходного кода через GitHub API;
- `rag` — поиск по локальным документам через persistent RAG.

## Инструменты времени

`TimeTools` три MCP-инструмента для получения актуального времени и даты:

- `GetCurrentTime` — возвращает локальное время, UTC, смещение локального часового пояса и ISO 8601 timestamp;
- `GetCurrentDate` — возвращает текущую локальную дату в форматах `yyyy-MM-dd` и `dd.MM.yyyy`, а также день недели в текущей локали системы;
- `GetCurrentTimestamp` — возвращает текущий локальный timestamp в ISO 8601 с локальным смещением часового пояса и микросекундной точностью, как в исходной Python-реализации.

В реализации используется `DateTimeOffset`, чтобы сохранять локальное смещение непосредственно вместе со значением времени. Инструменты обращаются к системным часам и не вычисляют текущее время эвристически.

## Интеграция с LM Studio

`LmStudioEndpoint` автоматически определяет OpenAI-compatible endpoint LM Studio.

При отсутствии ручного endpoint сервер:

1. получает локальные IPv4-адреса активных сетевых интерфейсов;
2. сначала проверяет порт `1234`;
3. затем проверяет порты `1235..1300`;
4. проверяет endpoint через `/v1/models`;
5. считает HTTP `401 Unauthorized` признаком найденного LM Studio endpoint, если включена authentication.

Можно задать endpoint вручную:

```text
EMBEDD_ENDPOINT=http://127.0.0.1:1234/v1
```

Для запросов к LM Studio используется bearer-токен:

```text
EMBEDD_KEY=<LM-Studio-token>
```

Если `EMBEDD_KEY` не задан, текущая реализация `EmbeddingService` завершает инициализацию с ошибкой.

## Выбор embedding-модели

`LmStudioModelDiscovery` сначала проверяет переменную:

```text
EMBEDD_MODEL=<model-id>
```

Если override не задан:

1. вызывается native LM Studio API `/api/v1/models`;
2. выбираются модели с `type = embedding`;
3. если embedding-модель только одна, она выбирается автоматически;
4. если моделей несколько, сервер пытается определить загруженную модель через OpenAI-compatible `/v1/models`;
5. если однозначный выбор невозможен, сервер сообщает список моделей и предлагает задать `EMBEDD_MODEL`.

`EmbeddingService` кэширует созданный `IEmbeddingGenerator` на время работы процесса и защищает инициализацию через `SemaphoreSlim`.

## Persistent RAG

RAG построен вокруг `RagIndexService`, `RagDocumentLoader`, `EmbeddingService`, `LmStudioModelDiscovery` и `FaissVectorStore`.

Постоянные данные индекса хранятся в:

```text
%LOCALAPPDATA%\SampleMcpServer\RagIndex    metadata.json
    documents.json
```

В `metadata.json` хранится информация о версии формата, embedding-модели, размерности вектора и индексированных файлах.

В `documents.json` сохраняются текст документов и рассчитанные embeddings. При запуске FAISS индекс восстанавливается в памяти из сохранённых векторов.

### Обновление индекса

Для каждого файла создаётся fingerprint из:

```text
полный путь + размер файла + LastWriteTimeUtc
```

Если fingerprint не изменился, embedding уже сохранённого документа повторно не генерируется.

Для изменившегося файла:

1. старые chunks удаляются;
2. файл заново декодируется;
3. для новых chunks рассчитываются embeddings;
4. новые данные сохраняются в persistent storage;
5. in-memory FAISS индекс пересоздаётся из актуального набора embeddings.

Также обрабатывается удаление файлов из исходного набора.

Если изменилась embedding-модель или размерность embeddings, текущая реализация считает сохранённый индекс несовместимым и начинает его перестроение.

## Поддерживаемые документы

`RagDocumentLoader` поддерживает:

- PDF;
- DOCX;
- XLSX;
- PPTX;
- TXT;
- Markdown;
- CSV;
- LOG;
- JSON;
- XML;
- HTML;
- YAML;
- исходный код и другие текстовые форматы из настроенного списка расширений.

Для неизвестных расширений используется дополнительная проверка `FileUtils.IsPlainText()` по сигнатуре файла.

DOCX обрабатывается отдельным `MyWordExtractor`, который группирует содержимое по заголовкам и приблизительно определяет номер страницы.

## Поиск в FAISS

`FaissVectorStore` реализует `VectorStore` поверх FAISS.

Используемый индекс:

```text
IDMap,HNSW32
METRIC_INNER_PRODUCT
```

Векторное измерение фиксируется по фактическому embedding первого добавленного документа. При попытке добавить вектор другой размерности генерируется ошибка.

## MCP-инструменты и описание для модели

Для MCP-инструментов унифицированы XML-документация C# и атрибуты `Description`.

Особое внимание уделено описаниям:

- назначения инструмента;
- параметров;
- допустимых значений и ограничений;
- результата;
- исключений и порогов поиска.

Эти описания являются частью метаданных MCP-инструментов и используются MCP-клиентом/моделью при выборе подходящего инструмента.

## Конфигурация поиска в интернете

### Internet Search

Набор поисковых движков задаётся WEB_SEARCH_ENGINES= через запятую:

```text
WEB_SEARCH_ENGINES=DuckDuckGo,Firecrawl,Baidu
```

Дополнительные параметры:

```text
WEB_SEARCH_FirecrawApiKey=<Firecrawl API key>
WEB_SEARCH_duckduckgoRegion=<DuckDuckGo region>
```

Поддерживаются:

- DuckDuckGo HTML search;
- Firecrawl search-and-scrape;
- Baidu search.

### GitHub Search

GitHub-токен читается из:

```text
GUTHUB_TOKEN=<GitHub token>
```

Инструмент предоставляет поиск:

- репозиториев;
- исходного кода с последующей загрузкой найденных файлов.

## Сборка

Проект рассчитан на .NET 8:

```bash
dotnet restore
dotnet build
```

Для публикации self-contained single-file приложения настройки заданы в `SampleMcpServer.csproj`.

## Ограничения текущей реализации

`MyWordExtractor.DecodeAsync` имеет суффикс `Async`, но фактически выполняет синхронное чтение DOCX и возвращает готовый список.

`DuckDuckGoSearch.LoadAsync2` сохраняет параметры `region` и `time` для совместимости сигнатуры, хотя текущий instant-answer запрос их непосредственно не использует.

`FaissVectorStore` хранит FAISS индекс в памяти. Persistent состояние RAG сохраняется отдельно в JSON и используется для восстановления индекса при новом запуске.

## Лицензия

Проект распространяется по лицензии **MIT**.

Полный текст лицензии находится в файле [`LICENSE`](LICENSE).

Лицензия MIT разрешает использовать, копировать, изменять, объединять,
публиковать, распространять, сублицензировать и продавать программное
обеспечение при сохранении уведомления об авторских правах и текста лицензии.

