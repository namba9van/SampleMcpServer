# Встроенная проверка MCP

## Автоматическая проверка

Из корня проекта удобнее запускать проверку через `dotnet run`:

```powershell
dotnet run --project .\SampleMcpServer.csproj -- --self-test
```

Для опубликованной Windows-сборки:

```powershell
.\_Release\win-x64\SampleMcpServer.exe --self-test
```

Для macOS/Linux после переноса соответствующей release-папки на целевую ОС:

```bash
./SampleMcpServer --self-test
```

Команда запускает дочерний экземпляр того же EXE, обменивается с ним JSON-RPC сообщениями через `stdin/stdout` и проверяет базовый жизненный цикл MCP. Дополнительно самопроверка проверяет файловые инструменты: наличие `write_file` и `rewrite_file`, отсутствие параметра `overwrite` у `write_file`, запрет перезаписи существующего файла через `write_file` и корректную перезапись через `rewrite_file`. Для проверки используются временные файлы, которые удаляются после теста.

## Интерактивный режим

Из исходников:

```powershell
dotnet run --project .\SampleMcpServer.csproj -- --inspect
```

Для Windows release-сборки:

```powershell
.\_Release\win-x64\SampleMcpServer.exe --inspect
```

Доступны:

1. Полная проверка.
2. Список MCP-инструментов.
3. Ответ на `initialize`.

## Важное правило stdio

Обычный MCP-сервер должен писать протокольные сообщения только в `stdout`. Логи диагностики направляются в `stderr`. Встроенный инспектор соблюдает это разделение.
