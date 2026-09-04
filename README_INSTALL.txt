SampleMcpServer (lite: без agent_chat/agent_dialog/ChatSupervizer)

СТРУКТУРА RELEASE:
  _Release\SampleMcpServer.exe

СБОРКА:
  cd C:\Users\user\SampleMcpServer
  dotnet clean
  dotnet restore
  dotnet build -c Release

Или:
  .\build-release.ps1

ПЕРЕНОС В LM STUDIO:
  Переносите папку _Release ЦЕЛИКОМ.
  В рабочей папке LM Studio она должна находиться как:
  C:\Users\user\.lmstudio\SampleMcpServer\

После переноса:
  C:\Users\user\.lmstudio\SampleMcpServer\SampleMcpServer.exe

mcp.json уже указывает на:
  C:\Users\user\.lmstudio\SampleMcpServer\SampleMcpServer.exe
