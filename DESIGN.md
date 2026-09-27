# RemoteControl

Локальный MCP-сервер на **.NET 10**: Windows Service принимает запросы, а desktop, ввод и все shell выполняются **от активного пользователя**.

```text
Windows Service (LocalSystem, HTTP /mcp, статический токен)
  └─ DesktopWorker (тот же exe, активная пользовательская сессия)
       ├─ FakerInput: клавиатура и мышь
       ├─ desktop: окна и JPEG/PNG
       ├─ PowerShell / Node.js / Python: код, сессии, pip/npm
       └─ Lua: последовательности, циклы, функции
```

Один установленный экземпляр управляет своим компьютером. Endpoint определяет компьютер; `host_id`, координатора и регистрации хостов нет.

## Пример

```json
{
  "name": "computer.sequence",
  "arguments": {
    "actions": [
      {"op":"click","x":800,"y":500},
      {"op":"type","text":"hello"},
      {"op":"press","key":"ENTER"},
      {"op":"drag","x":800,"y":600,"x2":1100,"y2":600}
    ]
  }
}
```

Результат: `{"ok":true,"backend":"fakerinput"}`. При недоступном драйвере: `{"ok":false,"backend":"fakerinput","error":"driver_unavailable"}`.

## Процессы и JSON

- SCM запускает `RemoteControlSvc` автоматически как `LocalSystem`.
- Сервис запускает тот же executable с `--desktop-worker` через `WTSQueryUserToken`, `CreateEnvironmentBlock`, `CreateProcessAsUser`; desktop — `winsta0\default`.
- Worker получает токен и environment пользователя: `USERNAME`, `USERPROFILE`, `LOCALAPPDATA`, `PATH`. Shell, Lua, pip/npm и дочерние процессы работают с этим окружением.
- Worker не показывает консоль. При его завершении сервис запускает новый.
- Named pipe с префиксом `RemoteControl.DesktopWorker` и уникальным суффиксом службы принимает команды последовательно.
- Протокол: типизированные `WorkerRequest`, `WorkerArgs`, `WorkerResponse`; сериализация через source-generated `WorkerJsonContext`. Ответ содержит типизированный результат, без JSON-строки внутри JSON.
- Состояние worker: `starting` до pipe-handshake, затем `running`. При смене активной Windows session запускается worker нового пользователя; прежние shell-сессии завершаются.
- После потери ответа уже отправленная команда не повторяется: возвращается `worker_response_lost`.

HTTP MCP использует официальный C# SDK, Streamable HTTP и `/mcp`. Для каждого MCP-запроса нужен `X-Admin-Token`; сервис создаёт токен в `%ProgramData%\RemoteControl\control.token` при первом запуске, либо использует явно заданный `Control:Token`/`REMOTE_CONTROL_TOKEN`. `/healthz` возвращает состояние HTTP-сервера.

## Все MCP tools

```text
computer.status()
computer.windows()

computer.screenshot(format?, quality?, maxWidth?, maxHeight?, maxBytes?)
computer.screenshot_monitor(monitor, format?, quality?, maxWidth?, maxHeight?, maxBytes?)
computer.screenshot_window(windowId, format?, quality?, maxWidth?, maxHeight?, maxBytes?)
computer.screenshot_region(x, y, width, height, format?, quality?, maxWidth?, maxHeight?, maxBytes?)

computer.click(x, y)
computer.double_click(x, y)
computer.type(text)
computer.press(key)
computer.key_down(key, holdTimeoutMs)
computer.key_up(key)
computer.hotkey(keys)
computer.scroll(delta)
computer.drag(x1, y1, x2, y2)
computer.sequence(actions, resetBefore=false, resetAfter=false)

computer.raw_mouse(report)
computer.raw_keyboard(report)
computer.release_all()

computer.run(runtime, code, environment?, session?, timeoutMs=30000)
computer.close_session(runtime, session)
computer.environment(runtime, name, packages?)
computer.lua(code, session?)
```

`runtime`: `powershell | nodejs | python`. Синонимы: `pwsh | node | py`.

`sequence` поддерживает `click`, `double_click`, `type`, `press`, `key_down`, `key_up`, `hotkey`, `scroll`, `drag`. Действия выполняются одним запросом worker, последовательно; первая ошибка останавливает sequence.

```json
{
  "name": "computer.sequence",
  "arguments": {
    "resetBefore": true,
    "resetAfter": true,
    "actions": [
      {"op":"key_down","key":"CTRL","holdTimeoutMs":2000},
      {"op":"press","key":"A"},
      {"op":"key_up","key":"CTRL"}
    ]
  }
}
```

`holdTimeoutMs` обязателен для `key_down` и ограничен 1–60000 мс. Клавиша отпускается по `key_up`, таймауту или `release_all`; конец `sequence` сам её не отпускает. `resetBefore`/`resetAfter` по умолчанию `false` и отпускают все виртуальные клавиши и кнопки мыши. `resetAfter` выполняется и при ошибке действия или отмене. Неверный `key_down` проверяется до любых действий и сбросов.

## Высокоуровневый ввод

Параметры траектории и задержек фиксированы внутри сервиса:

```text
click:       плавное движение → пауза → down → hold → up
double_click: два click с интервалом
type:        down/up каждого символа, по текущей раскладке
drag:        движение к началу → down → плавное движение → up
```

Для `type` используются реальные HID-клавиши и модификаторы, без вставки через clipboard. Если текущая раскладка не позволяет набрать символ, весь вызов возвращает `unsupported_character` до ввода текста. `press` принимает клавишу, например `I`, `ENTER`, `ESCAPE`, `F1`; `hotkey` — `CTRL+A`. `scroll`: число шагов колеса со знаком.

При ошибке или отмене drag кнопка отпускается в `finally`. Для явного сброса есть `release_all`.

## Низкоуровневый ввод

`report` — массив байт, передаваемый в JSON как base64. Один вызов отправляет одно состояние без задержек.

```text
keyboard, 9 bytes:
  [1, modifiers, reserved=0, key1, key2, key3, key4, key5, key6]

relative mouse, 8 bytes:
  [3, buttons, dxLo, dxHi, dyLo, dyHi, wheel, horizontalWheel]

absolute mouse, 8 bytes:
  [4, buttons, xLo, xHi, yLo, yHi, wheel, horizontalWheel]
```

Клавиши — USB HID usages. Модификаторы: Ctrl=1, Shift=2, Alt=4, Win=8; правые модификаторы — старшие четыре бита. Кнопки мыши: left=1, right=2, middle=4, X1=8, X2=16. Relative координаты — signed int16 little-endian; absolute — uint16 в диапазоне 0…32767. Колесо — signed int8. Пустой keyboard report отпускает клавиши.

Например, raw `I down`: `[1,0,0,12,0,0,0,0,0]`; затем `[1,0,0,0,0,0,0,0,0]` для отпускания.

## FakerInput

Используется **существующий подписанный Windows UMDF-драйвер FakerInput 0.1.1**. Собственного драйвера и этапа его сборки/подписания нет.

User-mode библиотеки `FakerInputWrapper.dll` и `FakerInputDll.dll` располагаются рядом с executable. Можно указать wrapper через `FAKERINPUT_WRAPPER_PATH`. Драйвер устанавливается отдельно официальным MSI; DS4Windows не требуется запускать для работы сервиса.

Только DesktopWorker открывает FakerInput. Проверяется устройство по **hardware ID `ROOT\FakerInput`**, а не по instance ID; реальный экземпляр на POE1VM — `ROOT\SYSTEM\0001`. Затем проверяются установленный пакет и соединение wrapper. `computer.status` возвращает состояние именно worker.

Worker не переключает ввод на SendInput при ошибке драйвера.

## Скриншоты

Все четыре метода возвращают MCP `ImageContentBlock`: по умолчанию **JPEG, quality 75, исходный размер**. Необязательные параметры одинаковы: `format` (`jpeg`/`png`), `quality` (1–100 только для JPEG), `maxWidth`, `maxHeight` и `maxBytes`. Для PNG `quality` нужно опустить. Изображение приходит прямо в результате tool.

`maxWidth` и `maxHeight` пропорционально уменьшают кадр без увеличения. `maxBytes` задаёт строгий предел размера закодированного файла (1–16777216 байт); передача через MCP base64 больше. Worker захватывает кадр один раз и при необходимости уменьшает разрешение, сохраняя запрошенные формат и качество. Если кадр не помещается даже при минимальном размере, возвращается `image_budget_unreachable`; неверные параметры дают `invalid_capture_options` до захвата. Область более 64 млн пикселей или 32768 пикселей по стороне даёт `capture_too_large` до выделения bitmap.

```json
{"x":100,"y":80,"width":1200,"height":800,"format":"jpeg","quality":70,"maxWidth":960,"maxBytes":120000}
```

- `screenshot`: основной монитор; размеры определяет worker.
- `screenshot_monitor`: монитор по индексу с нуля.
- `screenshot_region`: прямоугольник в координатах desktop.
- `screenshot_window`: видимый прямоугольник окна из `computer.windows`; перекрывающие окна тоже попадут в кадр.

Захват выполняется в пользовательской сессии через GDI. При ошибке возвращается текстовый код.

## Shell и окружения

Все три shell работают в обычном пользовательском окружении, **без отдельного sandbox**.

- PowerShell: persistent Runspace в DesktopWorker.
- Node.js: обычный `node.exe`, persistent REPL с `useGlobal:true`; доступны `process`, `require`, `fs`, `child_process`.
- Python: обычный `python.exe`, persistent namespace; штатные builtins, imports и subprocess.

Без `session` код выполняется одноразово. С именем сессии сохраняются переменные, функции, импорты и состояние. `close_session` освобождает её; после закрытия тот же идентификатор создаёт новую сессию. Environment закрепляется при создании: последующие вызовы могут его опускать, а другой явно указанный environment возвращает `session_environment_mismatch`.

```text
computer.environment("python", "vision", ["pillow"])
computer.run("python", "from PIL import Image; x=40", "vision", "main")
computer.run("python", "print(x+2)", "vision", "main")
computer.close_session("python", "main")
```

Окружения хранятся в `%LOCALAPPDATA%\RemoteControl\envs\<name>` пользователя. Python использует обычный venv и pip; Node.js — project directory с package.json, package-lock.json и node_modules. Повторный вызов передаёт указанные пакеты pip/npm в существующем окружении.

Результат `run`: `ok, runtime, session, stdout, stderr, exitCode, durationMs, error`. stdout/stderr читаются одновременно с процессом; общий timeout охватывает и чтение потоков. Таймаут прерывает PowerShell pipeline или завершает Node/Python процесс. Node/Python session после таймаута создаётся заново. Ошибка Python сохраняет уже напечатанный stdout.

## Lua: что доступно внутри скрипта

MCP предоставляет один `computer.lua(code, session?)`. Внутри доступны язык Lua и функции:

```lua
click(800, 500)
double_click(800, 500)
type_text("hello")
press("ENTER")
hotkey("CTRL+A")
drag(800, 600, 1100, 600)
sleep(150)
run("python", "print(2+2)")                 -- stdout
environment("python", "vision", {"pillow"}) -- path
```

Lua использует те же high-level методы ввода. Циклы, условия, функции и переменные принадлежат Lua; named session сохраняет globals между вызовами. Один вызов ограничен 30 секундами; бесконечный цикл прерывается, worker продолжает принимать команды. Ошибка shell или установки пакета прерывает Lua и возвращает ошибку. Raw reports, screenshots и управление shell-сессиями доступны через отдельные MCP tools.

## Файлы проекта и проверка

```text
RemoteControl.Core/             DTO, интерфейсы, алгоритмы high-level ввода
RemoteControl.Infrastructure/
  Input/                        FakerInput, раскладка, курсор Win32
  Desktop/                      GDI JPEG и окна
  Execution/                    runtime, процессы, сессии, pip/npm
  Scripting/                    Lua и bindings
  Transport/                    typed pipe protocol, server, client, dispatch
RemoteControl.Service/
  Program.cs, WorkerApplication.cs  сборка компонентов для двух процессов
  Hosting/                      supervisor и запуск worker от пользователя
  Mcp/                          21 tool, токен и преобразование JPEG в MCP image
RemoteControl.Tests/            regression tests с fake-устройствами и реальными runtime
install-service.ps1   установка из исходников или publish-каталога
scripts/Verify-Mcp.ps1 автоматическая живая проверка MCP
E2E-RESULTS.md         результаты на POE1VM и ссылки на кадры
```

Зависимости: `Service → Infrastructure → Core`. Core не зависит от Windows, MCP, PowerShell или pipe. Локальные исполнители и pipe-прокси отдельно реализуют одни и те же интерфейсы; только worker создаёт драйвер и runtime-сессии.

Обязательная проверка: status → кадр PoE → click → type → drag → raw input → кадры результата → PowerShell/Node/Python sessions → Lua composite. Результаты — реальные MCP-ответы, подписи установленного драйвера и кадры до/после.
