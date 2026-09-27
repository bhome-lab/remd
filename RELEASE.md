# Установка и релиз

`RemoteControl-Setup.exe` собирается NSIS 3.12. Внутри: self-contained .NET 10 LTS service, FakerInput 0.1.1 MSI, `FakerInputWrapper.dll` и `FakerInputDll.dll`. Node.js и Python только проверяются. Драйвер FakerInput остаётся после удаления приложения, потому что он может использоваться другими программами.

## Проверенные версии

- .NET 10 LTS: `net10.0`, self-contained `win-x64`; общий .NET Runtime на машине не нужен.
- `ModelContextProtocol.AspNetCore` 2.2.0, `Microsoft.Extensions.Hosting.WindowsServices` 10.0.12, `Microsoft.PowerShell.SDK` 7.6.6, `MoonSharp` 2.0.0: прямые NuGet-зависимости актуальны на 2026-09-27.
- `System.Drawing.Common` 10.0.12; `coverlet.collector` 10.0.1; `Microsoft.NET.Test.Sdk` 18.10.1; `xunit.runner.visualstudio` 4.0.0.
- FakerInput 0.1.1: последняя версия [автора](https://github.com/Ryochan7/FakerInput/releases). MSI подписан Ryodigi Solutions LLC.
- Wrapper DLL взяты из MIT-репозитория [CircumSpector/DS4Windows](https://github.com/CircumSpector/DS4Windows) на коммите `62e4e59097156d75dda7a0def9120454809021b9`; обе DLL подписаны Travis Nickles.
- NSIS 3.12: установленный компилятор проверяется по SHA-256. Все внешние бинарники проверяются по SHA-256 и Authenticode в `installer/Prepare-Payload.ps1`.

## Сборка

```powershell
./installer/Build-Installer.ps1 -Version 1.0.0
```

Скрипт запускает тесты, публикует self-contained `win-x64`, проверяет внешние бинарники, собирает `installer/out/RemoteControl-Setup.exe` и `.sha256`.

## Проверка после установки

```powershell
& "$env:ProgramFiles\RemoteControl\Test-Installation.ps1" -Json
```

`ok=true` означает, что обязательные компоненты работают: служба, токен, FakerInput, worker, MCP и снимок экрана. Node.js/Python выводятся как необязательные. При их отсутствии `computer.run` и `computer.environment` возвращают `environment_unavailable`; поиск runtime выполняется заново при каждом вызове.

Если драйвер требует перезагрузки, диагностика явно покажет отсутствие устройства/подключения. Релиз считается проверенным только после успешной установки и диагностики на отдельной Windows VM.
