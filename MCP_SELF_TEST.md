# Встроенная проверка MCP

## Автоматическая проверка

После сборки исполняемого файла:

```powershell
..\bin\Debug\net8.0\win-x64\SampleMcpServer.exe --self-test
```

Для опубликованной версии:

```powershell
.\SampleMcpServer.exe --self-test
```

Команда запускает дочерний экземпляр того же EXE, обменивается с ним JSON-RPC сообщениями через `stdin/stdout` и проверяет базовый жизненный цикл MCP.

## Интерактивный режим

```powershell
.\SampleMcpServer.exe --inspect
```

Доступны:

1. Полная проверка.
2. Список MCP-инструментов.
3. Ответ на `initialize`.

## Важное правило stdio

Обычный MCP-сервер должен писать протокольные сообщения только в `stdout`. Логи диагностики направляются в `stderr`. Встроенный инспектор соблюдает это разделение.

## Проверка потокового диалога агентов

Инструмент `agent_dialog` использует стандартные MCP `notifications/progress`.
Для проверки реального потока при настроенных моделях:

```powershell
$env:MCP_AGENT_SELF_TEST="true"
$env:AGENT_A_MODEL="model-a"
$env:AGENT_B_MODEL="model-b"
.\SampleMcpServer.exe --self-test
```

В выводе должны появиться строки вида:

```text
[progress] [A] ...
[progress] [B] ...
```

Без `MCP_AGENT_SELF_TEST=true` самопроверка не обращается к моделям и только
проверяет наличие инструмента `agent_dialog`.
