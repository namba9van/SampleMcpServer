# Security model

## Основной принцип

Память и автономность разделены. Тот факт, что модель может вспомнить данные, не означает, что ей автоматически разрешено выполнять внешние действия.

## Capability gate

Каждая потенциально опасная capability имеет policy. Действие может быть разрешено, запрещено или отправлено на approval. Approval привязан к нормализованным аргументам и является одноразовым.

## HTTP и webhooks

Allowlist проверяется до запроса. Автоматические HTTP redirects для governed outbound requests отключены, чтобы разрешённый host не мог перенаправить запрос на запрещённый адрес.

Не добавляйте широкие wildcard-like host rules. Разрешайте только реально необходимые endpoints.

## Filesystem

Agent runtime ограничивает пути `AGENT_FILESYSTEM_ROOT`. Проверяется canonical path и блокируется traversal через symlink/reparse point за пределы root.

Общие пользовательские `file_*` tools оставлены ради совместимости с исходным SampleMcpServer и принимают явный путь. Не давайте их автономному Agent Host как unrestricted capability.

## Shell

Shell capability выключена policy по умолчанию. Команда запускается с timeout и рабочей директорией внутри agent workspace. Это не заменяет OS/container sandbox. Для недоверенных задач запускайте процесс в контейнере или отдельной учётной записи.

## Memory prompt injection

`memory_context` маркирует retrieved memory как данные, а не инструкции. Тем не менее содержимое памяти может быть недоверенным. System/developer policy host'а должна иметь более высокий приоритет и не позволять retrieved text расширять права агента.

## Secrets

Не храните API keys в репозитории. Передавайте их через environment variables/secret store. `.gitignore` исключает локальные `.env` и runtime databases.
