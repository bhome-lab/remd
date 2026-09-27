# RemoteControl

MCP Windows Service на **.NET 10**: мышь, клавиатура, JPEG/PNG, PowerShell, Node.js, Python и Lua. Все действия выполняет worker активного пользователя.

## Установка

Скачайте `RemoteControl-Setup.exe` из [GitHub Releases](https://github.com/bhome-lab/remd/releases) и запустите от администратора. Установщик NSIS включает приложение с .NET 10 LTS, подписанный [FakerInput 0.1.1](https://github.com/Ryochan7/FakerInput/releases/tag/v0.1.1) и его DLL. Сервис запускается автоматически на `127.0.0.1:8080`.

Node.js и Python установщик не меняет. Они нужны только для соответствующих `computer.run` и `computer.environment`. Если runtime отсутствует, вызов возвращает `environment_unavailable`; после установки следующий вызов проверяет наличие заново.

После установки выполните от администратора:

```powershell
& "$env:ProgramFiles\RemoteControl\Test-Installation.ps1"
```

Диагностика проверяет службу, подписи DLL, устройство FakerInput, подключение worker, MCP, захват экрана и выполнение доступных Node.js/Python. Отсутствующие Node.js/Python показываются отдельно как необязательные компоненты.

Токен создаётся при первом запуске в `%ProgramData%\RemoteControl\control.token` с доступом SYSTEM/Administrators. MCP: `http://127.0.0.1:8080/mcp`, header `X-Admin-Token` со значением этого файла. Для ручной установки и иного адреса прослушивания остаётся `install-service.ps1`.

## Сборка и проверка

```powershell
dotnet build RemoteControl.slnx -c Release
dotnet test RemoteControl.slnx -c Release --no-build
./installer/Build-Installer.ps1 -Version 1.1.0
.\scripts\Verify-Mcp.ps1 -Uri 'http://127.0.0.1:18080/mcp' -Token 'HOME_TOKEN'
.\scripts\Verify-Worker.ps1 -Uri 'http://127.0.0.1:18080/mcp' -Token 'HOME_TOKEN'
```

Для живой проверки нужен запущенный Path of Exile. Скрипт сохраняет MCP-ответы и JPEG; игровые действия проверены отдельно на POE1VM.

Локальные тесты запускаются командой выше; исходная VM-регрессия включала **46 живых проверок MCP**. Установщик и его VM-проверка описаны в [RELEASE.md](RELEASE.md).

`computer.key_down(key, holdTimeoutMs)` удерживает клавишу максимум 1–60000 мс; `computer.key_up(key)` отпускает её раньше. В `computer.sequence` доступны действия `key_down`/`key_up` и необязательные `resetBefore`/`resetAfter` (оба `false`). Сброс отпускает все виртуальные клавиши и кнопки мыши. Подробный пример есть в [DESIGN.md](DESIGN.md).

- Скриншоты: JPEG quality 75 по умолчанию; все четыре метода принимают `format` (`jpeg`/`png`), `quality` для JPEG, `maxWidth`, `maxHeight` и `maxBytes`.
- Shell и pip/npm: обычное пользовательское окружение, без sandbox.
- PowerShell, Node.js, Python: именованные persistent sessions.
- JSON worker protocol: DTO и source generation.
- Ввод: существующий подписанный FakerInput; собственного драйвера нет.

Код разделён на `Core` (контракты и алгоритмы ввода), `Infrastructure` (Windows, runtime, pipe) и `Service` (MCP и запуск процессов).

[Полный API и дизайн](DESIGN.md) · [Проверка на POE1VM](E2E-RESULTS.md)
