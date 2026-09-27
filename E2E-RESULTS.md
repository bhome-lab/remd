# Проверка RemoteControl на POE1VM

**2026-09-27: рефакторинг на .NET 10 проверен с запущенным Path of Exile.**

Windows Service работает как `LocalSystem`, DesktopWorker и все shell — как `POE1VM\Admin`. Игра: `C:\POE1\PathOfExile.exe`, PID 1720. Endpoint: `http://127.0.0.1:18080/mcp`.

## Подтверждено

- **78/78 regression tests**: input, runtime, Lua, pipe, worker lifecycle и capture. Release build и publish без ошибок и предупреждений.
- **46/46 живых проверок MCP**: 21 tool, четыре JPEG, пользовательский контекст PowerShell/Node/Python, persistent state, 128 KiB вывода, timeout/recovery, pip/npm, Lua и ошибки аргументов.
- **Все 21 имени и input schema совпали** с предыдущей версией.
- Разрыв клиентского pipe сохранил worker **PID 7832** и PowerShell-сессию: значение изменилось `42 → 43`, следующий запрос его прочитал.
- Lua `while true do end` завершился с `execution_timeout`; следующий вызов той же сессии вернул `42`.
- Python сохраняет stdout перед исключением; произвольный stdout не принимается за ответ внутреннего протокола.
- Неверный статический токен даёт HTTP 401.

Смена Windows session, readiness и гонки stop/status проверены через fake launcher; реальное переключение пользователей на VM не выполнялось.

## Ввод в игре

Через MCP нового сервиса проверены click, double_click, type, drag, hotkey/sequence, scroll, Lua input и raw keyboard/mouse.

- Click переместил персонажа; `press("I")` открыл инвентарь, raw keyboard закрыл.
- `type` набрал `remd refactor 42!`; double_click выделил слово, drag — строку.
- Sequence очистил набранный текст и закрыл чат; сообщения не отправлялись.
- Scroll изменил масштаб камеры, обратный scroll восстановил его.
- Lua выполнил click/sleep/press и вернул `42`.
- Raw mouse изменил координаты курсора `610,330 → 643,330`. После `release_all` старшие биты состояния left mouse, Ctrl и I равны нулю.

Игра и служба оставлены запущенными.

![Выделение drag](S:/remd/artifacts/refactor/04-drag-selection.jpg)

## Доказательства

- [78 локальных тестов](artifacts/test-results/refactor.trx).
- [46 живых проверок](artifacts/refactor/regression/summary.json) и [MCP-ответы](artifacts/refactor/regression/calls.jsonl).
- [Сравнение API](artifacts/refactor/api-compatibility.json), [актуальный schema](artifacts/refactor/regression/tools.json).
- [Игровые действия](artifacts/refactor/game-calls.jsonl), [raw input](artifacts/refactor/raw-input.json), [pipe disconnect](artifacts/refactor/pipe-disconnect.json).
- [Процессы, владельцы, токен и хеши установленной сборки](artifacts/refactor/final-build.json).
- [JPEG после scroll](artifacts/refactor/09-scroll-confirmed.jpg), [восстановленный масштаб](artifacts/refactor/10-scroll-restored.jpg).
- [Архив доказательств](artifacts/e2e-refactor-poe1vm.zip).
- [Проверенная сборка](artifacts/RemoteControl-refactor.zip).

Используется тот же установленный FakerInput 0.1.1. Подписи каталога и DLL ранее непосредственно проверены через Authenticode: [Valid, Ryodigi Solutions LLC](artifacts/e2e-poe1vm/driver-signatures.json). Драйвер в этом рефакторинге не переустанавливался.

SHA-256 установленной `RemoteControl.Service.dll` совпал с локальным publish:

```text
49F61949D6B9EAA0496631BFF5CA226C31896949866D2C33D432CF076B099A3A
```

Полные результаты запуска:

```powershell
dotnet test RemoteControl.slnx -c Release
.\scripts\Verify-Mcp.ps1 -Token 'HOME_TOKEN'
.\scripts\Verify-Worker.ps1 -Token 'HOME_TOKEN'
```

