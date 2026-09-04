SampleMcpServer (lite: без agent_chat/agent_dialog/ChatSupervizer)

ТРЕБОВАНИЯ ДЛЯ СБОРКИ:
  Windows + .NET 8 SDK

СБОРКА ВСЕХ ПЛАТФОРМ НА WINDOWS:
  cd C:\Users\user\SampleMcpServer
  .\build-release.ps1

СТРУКТУРА RELEASE:
  _Release\win-x64\SampleMcpServer.exe   Windows x64 (win-x64)
  _Release\mac\SampleMcpServer           macOS Apple Silicon (osx-arm64)
  _Release\linux\SampleMcpServer         Linux x64 (linux-x64)

СБОРКА ТОЛЬКО ОДНОЙ ПЛАТФОРМЫ:
  .\build-win.ps1
  .\build-mac.ps1
  .\build-linux.ps1

ПРОВЕРКА ПЕРЕД RELEASE:
  dotnet restore
  dotnet build
  dotnet run --project .\SampleMcpServer.csproj -- --self-test

ПЕРЕНОС В LM STUDIO НА WINDOWS:
  Используйте содержимое _Release\win-x64.
  Рабочая папка LM Studio может находиться здесь:
  C:\Users\user\.lmstudio\SampleMcpServer\

  После копирования основной файл:
  C:\Users\user\.lmstudio\SampleMcpServer\SampleMcpServer.exe

MACOS / LINUX:
  Переносите содержимое _Release\mac или _Release\linux соответственно.
  После копирования при необходимости выполните:
    chmod +x SampleMcpServer

ВАЖНО:
  Cross-publish на Windows создаёт сборку для другой ОС, но финальную проверку
  macOS/Linux варианта рекомендуется выполнить непосредственно на целевой ОС.
