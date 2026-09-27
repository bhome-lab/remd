namespace RemoteControl.Service.Mcp;

public static class ToolDocumentation
{
    public const string ServerInstructions = """
        Endpoint управляет только своим ПК. Desktop, ввод, shell, Lua и pip/npm работают от активного пользователя; shell использует обычное окружение без sandbox.
        Команды выполняются последовательно. Клавиатурный ввод получает текущее активное окно; выбор окна сам по себе не меняет фокус.
        High-level мышь: пиксели основного монитора, (0,0) слева сверху, координаты за границей прижимаются к краю. Raw-клавиши: USB HID usages, не ASCII-коды, Windows VK или scan codes.
        Проверяйте ok у input/run/environment/lua. Общие ошибки: interactive_session_unavailable, driver_unavailable, driver_write_failed. worker_response_lost означает потерю ответа: команда могла выполниться.
        Session хранится в памяти worker, отдельно для каждого runtime; перезапуск worker или смена пользователя её сбрасывает. Установленные environments сохраняются на диске.
        Примеры в описаниях — объект arguments для tools/call.
        """;

    private const string InputResult = "\nРезультат: {ok,backend,error?}.";
    private const string ImageResult = "\nРезультат: MCP image/jpeg, quality 75, исходный размер; при ошибке — текстовый код.";

    public const string Status = """
        Состояние службы, пользовательского desktop, FakerInput и shell.
        Результат: service, runtime, desktop, worker, input.driver, runtimes, environments. worker.state: starting (запускается), running (готов), unavailable (недоступен); runtimes — обнаруженные PowerShell/Node/Python.
        Пример: {}
        """;
    public const string Windows = """
        Видимые окна верхнего уровня с непустым заголовком.
        Результат: [{handle,title}]; handle используется в screenshot_window. Пустой список также возможен при недоступном desktop.
        Пример: {}
        """;
    public const string Screenshot = "Снимок всего основного монитора." + ImageResult + "\nПример: {}";
    public const string ScreenshotRegion = "Снимок прямоугольника desktop; x/y могут быть отрицательными для соседнего монитора." + ImageResult +
        "\nПример: {\"x\":100,\"y\":80,\"width\":640,\"height\":480}";
    public const string ScreenshotMonitor = "Снимок монитора по индексу Windows; неверный индекс — monitor_not_found." + ImageResult +
        "\nПример: {\"monitor\":0}";
    public const string ScreenshotWindow = "Снимок экранного прямоугольника окна, включая рамку и перекрывающие окна; не фокусирует окно. Устаревший handle — window_not_found." + ImageResult +
        "\nПример: {\"windowId\":123456}";

    public const string Click = "Плавно переместить курсор и нажать/отпустить левую кнопку мыши. Задержки фиксированы." + InputResult +
        "\nПример: {\"x\":800,\"y\":500}";
    public const string DoubleClick = "Плавно переместить курсор и выполнить двойной левый click." + InputResult +
        "\nПример: {\"x\":800,\"y\":500}";
    public const string Type = """
        Набрать текст HID-событиями по раскладке активного окна. CRLF превращается в один Enter; \r/\n — Enter, \t — Tab. Пустая строка ничего не вводит.
        unsupported_character возвращается до ввода, если хотя бы один символ нельзя сопоставить клавишам. Для физических клавиш используйте press.
        """ + InputResult + "\nПример: {\"text\":\"hello 42!\"}";
    public const string Press = """
        Нажать и отпустить одну клавишу: ASCII-символ, мнемонику или байт USB HID. Модификаторы тоже допустимы; для сочетания используйте hotkey.
        ASCII обозначает физические клавиши US: A/a — одна клавиша без Shift, ! — Shift+1. Это не ввод текста по текущей раскладке.
        """ + InputResult + """
        
        Пример: {"key":"a"}
        Пример: {"key":"F1"}
        Пример: {"key":4}
        """;
    public const string KeyDown = """
        Удержать физическую HID-клавишу или модификатор. key принимает ту же строку или HID-байт, что press.
        holdTimeoutMs обязателен: 1..60000 мс. Клавиша автоматически отпускается по истечении времени, если key_up или release_all не вызваны раньше.
        Повторный key_down той же клавиши обновляет срок удержания. Удержание сохраняется после завершения sequence.
        Ошибки: invalid_key, invalid_timeout, driver_unavailable, driver_write_failed.
        """ + InputResult + "\nПример: {\"key\":\"CTRL\",\"holdTimeoutMs\":2000}";
    public const string KeyUp = """
        Отпустить клавишу, удерживаемую key_down. key принимает ту же строку или HID-байт; после истечения таймаута повторное отпускание успешно и ничего не меняет.
        """ + InputResult + "\nПример: {\"key\":\"CTRL\"}";
    public const string Hotkey = """
        Одновременно нажать сочетание, затем отпустить клавиши и модификаторы. Массив смешивает ASCII, мнемоники и HID-байты; максимум 6 различных обычных клавиш плюс модификаторы. Повторы объединяются.
        Неверная клавиша/пустой массив/переполнение — invalid_key до отправки ввода. Для буквального + используйте массив.
        """ + InputResult + """
        
        Пример: {"keys":["CTRL",4]}
        Пример: {"keys":[224,4]}
        Пример: {"keys":"CTRL+A"}
        """;
    public const string Scroll = "Вертикальная прокрутка в шагах колеса, в текущей позиции курсора. Большие значения передаются полностью." + InputResult +
        "\nПример: {\"delta\":-3}";
    public const string Drag = "Переместить курсор к началу, удерживать левую кнопку до конца траектории и отпустить. При ошибке/отмене отпускание тоже выполняется." + InputResult +
        "\nПример: {\"x1\":100,\"y1\":200,\"x2\":500,\"y2\":200}";
    public const string Sequence = """
        Выполнить действия по порядку одним запросом; первая ошибка останавливает список без отката уже выполненных действий. [] — успешный пустой список.
        op и используемые поля: click/double_click(x,y), type(text), press(key), key_down(key,holdTimeoutMs), key_up(key), hotkey(keys), scroll(delta), drag(x,y,x2,y2).
        resetBefore и resetAfter — независимые bool, по умолчанию false. Первый отпускает все виртуальные клавиши/кнопки до действий; второй — после начала выполнения, в том числе при ошибке действия или отмене. Без этих флагов key_down остаётся активным после sequence до key_up, таймаута или release_all.
        Все key_down и их таймауты проверяются до начала действий и начального сброса. Диапазон holdTimeoutMs: 1..60000 мс.
        """ + InputResult + """
        
        Пример: {"resetBefore":true,"resetAfter":true,"actions":[{"op":"key_down","key":"CTRL","holdTimeoutMs":2000},{"op":"click","x":800,"y":500},{"op":"key_up","key":"CTRL"}]}
        """;
    public const string RawMouse = """
        Отправить одно состояние мыши FakerInput без траектории и задержек. Buttons заменяет набор удерживаемых кнопок; автоматического отпускания нет.
        Для отпускания отправьте buttons=0 или release_all. Неверные длина/id/buttons/absolute coordinates — invalid_report.
        """ + InputResult + "\nПример: {\"report\":\"AwAUAAAAAAA=\"}";
    public const string RawKeyboard = """
        Отправить одно состояние клавиатуры FakerInput без задержек. Новый report заменяет удерживаемые клавиши/модификаторы; автоматического отпускания нет.
        Пример удерживает I (HID 12); для отпускания отправьте AQAAAAAAAAAA или release_all. Неверные длина/id/reserved — invalid_report.
        """ + InputResult + "\nПример: {\"report\":\"AQAADAAAAAAA\"}";
    public const string ReleaseAll = "Отпустить все виртуальные клавиши, модификаторы и кнопки мыши FakerInput." + InputResult + "\nПример: {}";

    public const string Run = """
        Выполнить код в обычном пользовательском PowerShell, Node.js или Python. Без session — новый запуск; с session — сохранить переменные, функции и imports.
        Результат: {ok,runtime,session?,stdout,stderr,exitCode,durationMs,error?}; durationMs — миллисекунды, exitCode — код процесса либо -1 при ошибке session/timeout.
        Таймаут: execution_timeout; Node/Python session сбрасывается, PowerShell сохраняет session после остановки pipeline.
        Пример: {"runtime":"python","code":"print(2+2)","environment":"vision","session":"main","timeoutMs":30000}
        """;
    public const string CloseSession = """
        Закрыть именованную PowerShell/Node/Python session и освободить её ресурсы. Следующий run с этим именем создаст новую; environment на диске сохраняется. Lua-сессии этим методом не закрываются.
        Результат: true — закрыта; false — не найдена либо worker недоступен.
        Пример: {"runtime":"python","session":"main"}
        """;
    public const string Environment = """
        Создать/обновить Python venv через pip либо Node.js project через npm. Путь: %LOCALAPPDATA%\RemoteControl\envs\<name> активного пользователя.
        Повторный вызов использует существующий environment; packages передаётся установщику, [] ничего не устанавливает.
        Результат: {ok,runtime,name,path,packages,error?}; packages — переданные спецификации, не список всех установленных пакетов.
        Пример: {"runtime":"python","name":"vision","packages":["pillow"]}
        Пример: {"runtime":"nodejs","name":"web","packages":["is-number@7.0.0"]}
        """;
    public const string Lua = """
        Выполнить Lua с input/shell bindings; фиксированный общий timeout 30 секунд. Ошибка binding прерывает вызов; timeout сбрасывает Lua session.
        Результат: {ok,output,error?}; output — строковое представление return, не stdout/print.
        Bindings: click(x,y), double_click(x,y), type_text(text), press(key), hotkey(keys), drag(x1,y1,x2,y2), sleep(ms), run(runtime,code), environment(runtime,name,packages).
        press принимает строку/HID-байт; hotkey — строку CTRL+A или Lua-массив {"CTRL",4}. sleep: 0..60000 ms (значение прижимается к диапазону, действует общий timeout).
        run возвращает stdout, запускается одноразово без environment/session; runtime как в computer.run. environment возвращает path, packages — Lua-массив; варианты как в computer.environment.
        Scroll, raw reports, скриншоты и закрытие shell session доступны отдельными MCP tools.
        Пример: {"code":"hotkey({'CTRL',4}); sleep(100); press('F1'); return 42","session":"macro"}
        """;

    public const string X = "X в пикселях основного монитора, от левого края; за границей прижимается к краю.";
    public const string Y = "Y в пикселях основного монитора, от верхнего края; за границей прижимается к краю.";
    public const string RegionX = "Левая граница в координатах desktop; 0 — левый край основного монитора, отрицательные значения допустимы.";
    public const string RegionY = "Верхняя граница в координатах desktop; 0 — верх основного монитора, отрицательные значения допустимы.";
    public const string Width = "Ширина в пикселях, >0; иначе screen_unavailable.";
    public const string Height = "Высота в пикселях, >0; иначе screen_unavailable.";
    public const string Monitor = "Индекс монитора от 0 в порядке Windows EnumDisplayMonitors; это не windowId.";
    public const string WindowId = "Числовой handle из computer.windows; пример 123456 нужно заменить реальным handle.";
    public const string Text = "Текст для активного окна; используются текущая раскладка и реальные down/up. \\r/\\n — Enter, \\t — Tab.";
    public const string Delta = "Целое число шагов: >0 вверх, <0 вниз, 0 без движения. Это не пиксели и не единицы WHEEL_DELTA.";
    public const string Key = """
        Строка: один печатный ASCII-символ, \b/\t/\n/\r/\u001b/\u007f, мнемоника или 0xNN. Регистр букв не влияет на физическую клавишу; символы !@# и т.п. добавляют Shift по US.
        Число: целый USB HID usage 0..255; 4=A, 58=F1, 224..231=LCtrl/LShift/LAlt/LWin/RCtrl/RShift/RAlt/RWin, 0=пустое состояние. "4" — цифра 4, число 4/"0x04" — A. Не Windows VK/scan code. Неизвестная строка — invalid_key.
        """;
    public const string Keys = """
        Непустой массив обозначений из computer.press.key, например ["CTRL","a"], [224,4], ["CTRL",4] или ["CTRL","+"]. До 6 различных обычных HID-клавиш; модификаторы объединяются.
        Также принимается строка через +: "CTRL+A", "0xE0+0x04". Строковый + — разделитель; массив позволяет нажать сам символ +.
        """;
    public const string Actions = "Массив действий. op задаёт используемые поля; key/keys имеют тот же формат, что computer.press/hotkey. Числа по умолчанию 0, text/key/keys — null.";
    public const string RawMouseReport = """
        Base64 ровно 8 байт: [id,buttons,xLo,xHi,yLo,yHi,wheel,hWheel]. Little-endian.
        id=3: x/y signed int16 (-32768..32767), относительное перемещение. id=4: x/y 0..32767, абсолютные HID-координаты (не пиксели).
        buttons — OR битов left=1,right=2,middle=4,X1=8,X2=16; 0 отпускает все. wheel/hWheel — signed int8 (-128..127), >0 вверх/вправо. Пример AwAUAAAAAAA= перемещает на dx=20,dy=0.
        """;
    public const string RawKeyboardReport = """
        Base64 ровно 9 байт: [1,modifiers,0,key1,key2,key3,key4,key5,key6].
        key1..6 — USB HID usages 0..255; 0 — пустой слот. modifiers — OR битов LCtrl=1,LShift=2,LAlt=4,LWin=8,RCtrl=16,RShift=32,RAlt=64,RWin=128.
        AQAADAAAAAAA удерживает I; AQAAAAAAAAAA отпускает клавиатуру.
        """;
    public const string Runtime = "powershell|nodejs|python; алиасы pwsh|node|py. Регистр и пробелы вокруг имени игнорируются.";
    public const string EnvironmentRuntime = "python|nodejs; алиасы py|node. Регистр и пробелы вокруг имени игнорируются. PowerShell environment не поддерживается.";
    public const string Code = "Исходный код: PowerShell '$x=40;$x+2'; Node.js 'console.log(2+2)'; Python 'print(2+2)'. Выводите результат явно; именованная Node session также поддерживает top-level await.";
    public const string RunEnvironment = "Имя уже созданного computer.environment, только Node/Python. null/пропуск: обычное окружение или ранее привязанный env session. Другой существующий env для той же session — session_environment_mismatch; отсутствующий — environment_not_found.";
    public const string Session = "null/пропуск — одноразовый запуск; строка — создать/продолжить session. Имя без учёта регистра, отдельно для каждого runtime. Состояние живёт до close_session или завершения worker.";
    public const string CloseSessionName = "Имя session из computer.run; регистр не важен. Закрытие не удаляет environment.";
    public const string Timeout = "Общий timeout выполнения и чтения stdout/stderr в ms; по умолчанию 30000. Значения <=0 трактуются как 1 ms; 0 не означает бесконечность.";
    public const string EnvironmentName = "Имя каталога: непустые Unicode-буквы/цифры, '-' и '_'; пробелы вокруг удаляются. Общая область имён Python/Node; для разных runtime используйте разные имена.";
    public const string Packages = "Список спецификаций pip/npm: ['six==1.17.0'] или ['is-number@7.0.0']; один элемент — один аргумент установщика. null/пропуск/[] создаёт env без установки пакетов.";
    public const string LuaCode = "Lua-код с bindings из описания. Используйте return для output: 'return 2+2'. press(4), press('a'), press('F1'), hotkey({'CTRL',4}) используют общий формат клавиш.";
    public const string LuaSession = "null/пропуск — новый Lua state; строка — сохранить globals, отдельно от shell sessions, без учёта регистра. Timeout и завершение worker сбрасывают state; close_session к Lua не применяется.";
}
