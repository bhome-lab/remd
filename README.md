# RemoteControl

MCP Windows Service на **.NET 10**: мышь, клавиатура, JPEG, PowerShell, Node.js, Python и Lua. Все действия выполняет worker активного пользователя.

## Установка

Нужны .NET 10 ASP.NET Core Runtime x64, установленный [FakerInput 0.1.1](https://github.com/Ryochan7/FakerInput/releases/tag/v0.1.1), Node.js и Python для соответствующих runtime. PowerShell включён в приложение через SDK.

Распакуйте release ZIP и выполните от администратора:

```powershell
.\install-service.ps1 -Token 'HOME_TOKEN' `
  -FakerInputDirectory 'C:\Tools\DS4Windows' `
  -Listen 'http://0.0.0.0:8080'
```

`FakerInputDirectory` должен содержать `FakerInputWrapper.dll` и `FakerInputDll.dll`. Установщик копирует их рядом с executable. DS4Windows как приложение для работы сервиса не требуется.

MCP: `http://<computer>:8080/mcp`, header `X-Admin-Token: HOME_TOKEN`. Каждый endpoint управляет только своим компьютером. Без `-Listen` служба слушает `127.0.0.1:8080`.

## Сборка и проверка

```powershell
dotnet build RemoteControl.slnx -c Release
dotnet test RemoteControl.slnx -c Release --no-build
dotnet publish RemoteControl.Service -c Release -r win-x64 --self-contained false
.\scripts\Verify-Mcp.ps1 -Uri 'http://127.0.0.1:18080/mcp' -Token 'HOME_TOKEN'
.\scripts\Verify-Worker.ps1 -Uri 'http://127.0.0.1:18080/mcp' -Token 'HOME_TOKEN'
```

Для живой проверки нужен запущенный Path of Exile. Скрипт сохраняет MCP-ответы и JPEG; игровые действия проверены отдельно на POE1VM.

Проверенная сборка: [RemoteControl-refactor.zip](artifacts/RemoteControl-refactor.zip). Результат: **78 локальных тестов, 46 живых проверок MCP**, input schema всех 21 tool сохранены.

- Скриншоты: JPEG quality 75 по умолчанию.
- Shell и pip/npm: обычное пользовательское окружение, без sandbox.
- PowerShell, Node.js, Python: именованные persistent sessions.
- JSON worker protocol: DTO и source generation.
- Ввод: существующий подписанный FakerInput; собственного драйвера нет.

Код разделён на `Core` (контракты и алгоритмы ввода), `Infrastructure` (Windows, runtime, pipe) и `Service` (MCP и запуск процессов).

[Полный API и дизайн](DESIGN.md) · [Проверка на POE1VM](E2E-RESULTS.md)
