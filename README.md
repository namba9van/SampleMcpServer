# SampleMcpServer v1.3.0

**SampleMcpServer** — консольный MCP-сервер на .NET 8 с транспортом `stdio`.
Проект предоставляет инструменты для вычислений, работы с файлами, времени,
веб-поиска, GitHub и локального RAG.

## Возможности

Основные MCP-инструменты:

- `random_number` — генерация случайного целого числа;
- `time` — получение локального и UTC-времени, даты, смещения часового пояса и временной метки;
- `calc` — арифметические операции;
- `write_file` — создание нового текстового файла без возможности перезаписи существующего;
- `rewrite_file` — полная замена содержимого только уже существующего текстового файла;
- `read_file` — чтение текстового файла;
- `list_files` — список файлов и непосредственных подкаталогов;
- `internet_search` — параллельный поиск через несколько поисковых систем с фильтрацией, ранжированием и удалением дублей;
- `github_search` — поиск репозиториев и исходного кода через GitHub API;
- `rag` — поиск по локальным документам с использованием embedding-моделей и FAISS.

Все сообщения протокола MCP передаются через `stdin/stdout`.
Диагностические сообщения записываются в `stderr` и не смешиваются с MCP-протоколом.

## Структура проекта

```text
SampleMcpServer/
├── Program.cs
├── SampleMcpServer.csproj
├── SampleMcpServer.sln
├── README.md
├── MCP_SELF_TEST.md
├── mcp.json.example
├── LICENSE
├── build-release.ps1
├── build-win.ps1
├── build-mac.ps1
├── build-linux.ps1
├── Tools/
│   ├── CalcTools.cs
│   ├── FileOperationsTools.cs
│   ├── GitHubSearchTool.cs
│   ├── InternetSearchTools.cs
│   ├── RAGTool.cs
│   ├── RandomNumberTools.cs
│   └── TimeTools.cs
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
    └── поисковые, файловые и RAG-вспомогательные классы
```

## Файловые операции

Файловые инструменты специально разделены по назначению:

- `write_file(filename, content)` создаёт только новый файл. Если файл уже существует, инструмент возвращает отказ и не изменяет его. Параметра `overwrite` у `write_file` нет.
- `rewrite_file(filename, content)` полностью заменяет содержимое только существующего файла. Если файла нет, инструмент не создаёт его и рекомендует использовать `write_file`.
- `read_file(filename)` читает текстовый файл.
- `list_files(path)` перечисляет файлы и непосредственные подкаталоги каталога.

Такое разделение предотвращает случайную потерю данных: создание нового файла и намеренная перезапись существующего требуют разных MCP-вызовов.

## Требования

- .NET 8 SDK;
- для RAG — доступный OpenAI-совместимый embedding endpoint, например LM Studio;
- для `github_search` рекомендуется `GITHUB_TOKEN`;
- для SearXNG нужен URL доступного экземпляра;
- API-ключи Firecrawl и Marginalia могут использоваться для персональных лимитов.

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
`mcp.json` (созданного на основе `mcp.json.example`) её можно удалить:

```powershell
Remove-Item Env:WEB_SEARCH_ENGINES -ErrorAction SilentlyContinue
```

## Конфигурация

Готовый пример находится в `mcp.json.example`. Скопируйте его в `mcp.json`
(этот файл игнорируется Git, так как в нём обычно указывается локальный путь
к `.exe`) и заполните нужные значения. Пример не содержит реальных секретов.

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

Подробности находятся в `MCP_SELF_TEST.md`.

## Безопасность и публикация

Перед публикацией проверьте, что в Git нет секретов:

```powershell
git status
git diff --cached
```

Никогда не добавляйте в репозиторий:

- `GITHUB_TOKEN`;
- `EMBEDD_KEY`;
- `WEB_SEARCH_FIRECRAWL_API_KEY`;
- `WEB_SEARCH_MARGINALIA_API_KEY`;
- локальные индексы RAG;
- `.env` и локальные конфигурационные файлы.

Шаблон `mcp.json.example` содержит только пустые значения секретов.

## Release-сборки для Windows, macOS и Linux

Релизные сборки создаются через PowerShell-скрипты. Основной сценарий рассчитан на Windows-машину с установленным .NET 8 SDK и выполняет cross-publish сразу для трёх целевых платформ.

Собрать все три варианта одной командой:

```powershell
.\build-release.ps1
```

Результат:

```text
_Release/
├── win-x64/   # Windows x64, RID win-x64
├── mac/       # macOS Apple Silicon, RID osx-arm64
└── linux/     # Linux x64, RID linux-x64
```

Каждая папка содержит self-contained публикацию для своей платформы. Для Windows основной исполняемый файл называется `SampleMcpServer.exe`; для macOS и Linux — `SampleMcpServer`.

Можно собрать только одну платформу:

```powershell
.\build-win.ps1
.\build-mac.ps1
.\build-linux.ps1
```

Соответствие скриптов:

- `build-win.ps1` → `_Release\win-x64` → `win-x64`;
- `build-mac.ps1` → `_Release\mac` → `osx-arm64` (Apple Silicon);
- `build-linux.ps1` → `_Release\linux` → `linux-x64`.

Скрипты используют `dotnet publish -c Release`, self-contained публикацию и single-file режим. Жёсткого `RuntimeIdentifier` в Release-конфигурации проекта больше нет: нужный RID передаёт конкретный build-скрипт, поэтому Windows-машина может подготовить все три варианта.

Перед релизной сборкой рекомендуется проверить проект:

```powershell
dotnet restore
dotnet build
dotnet run --project .\SampleMcpServer.csproj -- --self-test
```

Cross-publish создаёт файлы для другой ОС, но не заменяет запуск на целевой системе. После сборки macOS/Linux вариант следует хотя бы один раз проверить на соответствующей ОС, особенно если используются NuGet-пакеты с нативными библиотеками. После переноса файлов с Windows на macOS/Linux при необходимости установите исполняемый бит:

```bash
chmod +x SampleMcpServer
```

## Лицензия

Проект распространяется по лицензии MIT. Полный текст находится в `LICENSE`.

## Полный `mcp.json.example`

В репозитории файл `mcp.json.example` является готовым шаблоном для LM Studio. Он использует опубликованный Windows x64-файл `SampleMcpServer.exe`, а секреты оставлены пустыми. Перед запуском скопируйте его в `mcp.json`, проверьте путь к `.exe` и заполните только необходимые ключи.

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

## Release bundle

`build-release.ps1` создаёт три независимых release-комплекта в `_Release\win-x64`, `_Release\mac` и `_Release\linux`. Для LM Studio переносите содержимое папки, соответствующей ОС, а не весь `_Release` как один общий runtime. На Windows используйте `_Release\win-x64`.
