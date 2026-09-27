using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using RemoteControl.Core.Input;

namespace RemoteControl.Infrastructure.Input;

/// <summary>Владелец соединения с существующим FakerInputWrapper и подтвержденного состояния кнопок.</summary>
public sealed class FakerInputDevice : IInputDevice, IDisposable
{
    private readonly object _gate = new();
    private readonly bool _devicePresent;
    private readonly bool _driverSigned;
    private NativeConnection? _connection;
    private string _wrapperPath = "";
    private string? _error;
    private byte _mouseButtons;
    private bool _disposed;

    public FakerInputDevice(string? wrapperPath = null)
    {
        _devicePresent = FakerInputDiscovery.DevicePresent();
        _driverSigned = _devicePresent && FakerInputDiscovery.DriverPackageSigned();
        if (!_devicePresent || !_driverSigned) { _error = "driver_unavailable"; return; }
        foreach (var candidate in FakerInputDiscovery.WrapperCandidates(wrapperPath))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                _connection = new NativeConnection(candidate);
                _wrapperPath = candidate;
                _error = null;
                return;
            }
            catch (Exception exception) { _error = exception.GetBaseException().Message; }
        }
        _error ??= "wrapper_not_found";
    }

    public DriverStatus Status { get { lock (_gate) return new(_connection is not null && !_disposed, _wrapperPath, _devicePresent, _driverSigned, _error); } }
    public byte MouseButtons { get { lock (_gate) return _mouseButtons; } }

    public bool WriteMouse(MouseReport report)
    {
        lock (_gate)
        {
            if (_connection is null || _disposed) return false;
            try
            {
                if (!_connection.WriteMouse(report)) { _error = "driver_write_failed"; return false; }
                _mouseButtons = report.Buttons;
                _error = null;
                return true;
            }
            catch (Exception exception) { _error = exception.GetBaseException().Message; return false; }
        }
    }

    public bool WriteKeyboard(byte modifiers, ReadOnlySpan<byte> keys)
    {
        lock (_gate)
        {
            if (_connection is null || _disposed) return false;
            try
            {
                var success = _connection.WriteKeyboard(modifiers, keys);
                _error = success ? null : "driver_write_failed";
                return success;
            }
            catch (Exception exception) { _error = exception.GetBaseException().Message; return false; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try
            {
                if (_connection is null) return;
                // Отпускаем оба устройства, даже если первая запись завершилась ошибкой.
                WriteKeyboard(0, []);
                WriteMouse(new(false, 0, 0, 0));
                try { _connection.Dispose(); }
                catch (Exception exception) { _error = exception.GetBaseException().Message; }
            }
            finally { _connection = null; _mouseButtons = 0; _disposed = true; }
        }
    }

    private sealed class NativeConnection : IDisposable
    {
        private readonly object _native;
        private readonly MethodInfo _disconnect;
        private readonly MethodInfo _updateKeyboard;
        private readonly Type _keyboardReportType;
        private readonly Type _keyType;
        private readonly Type _modifierType;
        private readonly MethodInfo _keyDown;
        private readonly MethodInfo _modifierDown;
        private readonly MouseBinding _relativeMouse;
        private readonly MouseBinding _absoluteMouse;
        private bool _disposed;

        public NativeConnection(string path)
        {
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            var nativePath = Path.Combine(Path.GetDirectoryName(path)!, "FakerInputDll.dll");
            try
            {
                NativeLibrary.SetDllImportResolver(assembly, (name, _, _) => name.Contains("FakerInputDll", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(nativePath) ? NativeLibrary.Load(nativePath) : nint.Zero);
            }
            catch (InvalidOperationException) { /* Resolver уже установлен предыдущим соединением. */ }

            var inputType = RequireType(assembly, "FakerInput");
            _disconnect = RequireMethod(inputType, "Disconnect");
            _updateKeyboard = RequireMethod(inputType, "UpdateKeyboard");
            _keyboardReportType = RequireType(assembly, "KeyboardReport");
            _keyType = RequireType(assembly, "KeyboardKey");
            _modifierType = RequireType(assembly, "KeyboardModifier");
            _keyDown = _keyboardReportType.GetMethod("KeyDown", [_keyType]) ?? throw new MissingMethodException("KeyboardReport.KeyDown(KeyboardKey)");
            _modifierDown = _keyboardReportType.GetMethod("KeyDown", [_modifierType]) ?? throw new MissingMethodException("KeyboardReport.KeyDown(KeyboardModifier)");
            _relativeMouse = new(RequireType(assembly, "RelativeMouseReport"), RequireMethod(inputType, "UpdateRelativeMouse"));
            _absoluteMouse = new(RequireType(assembly, "AbsoluteMouseReport"), RequireMethod(inputType, "UpdateAbsoluteMouse"));
            _native = Activator.CreateInstance(inputType) ?? throw new InvalidOperationException("wrapper_creation_failed");
            try
            {
                if (!(bool)(RequireMethod(inputType, "Connect").Invoke(_native, null) ?? false)) throw new InvalidOperationException("driver_connection_failed");
            }
            catch { Dispose(); throw; }
        }

        public bool WriteMouse(MouseReport report) => (report.Absolute ? _absoluteMouse : _relativeMouse).Write(_native, report);

        public bool WriteKeyboard(byte modifiers, ReadOnlySpan<byte> keys)
        {
            // У upstream IEnumerable-конструктора не инициализированы sets: используем default + KeyDown.
            var report = Activator.CreateInstance(_keyboardReportType)!;
            for (var bit = 0; bit < 8; bit++)
                if ((modifiers & (1 << bit)) != 0) _modifierDown.Invoke(report, [Enum.ToObject(_modifierType, 1 << bit)]);
            foreach (var key in keys)
                if (key != 0) _keyDown.Invoke(report, [Enum.ToObject(_keyType, key)]);
            return (bool)(_updateKeyboard.Invoke(_native, [report]) ?? false);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _disconnect.Invoke(_native, null); }
            catch { /* Ошибка Disconnect не должна помешать освобождению wrapper. */ }
            finally { (_native as IDisposable)?.Dispose(); }
        }

        private static Type RequireType(Assembly assembly, string name) => assembly.GetType("FakerInputWrapper." + name) ?? throw new TypeLoadException(name);
        private static MethodInfo RequireMethod(Type type, string name) => type.GetMethod(name) ?? throw new MissingMethodException(type.FullName, name);

        private sealed class MouseBinding
        {
            private readonly Type _reportType;
            private readonly MethodInfo _update;
            private readonly MethodInfo _buttonDown;
            private readonly Type _buttonType;
            private readonly PropertyInfo _x, _y, _wheel, _horizontalWheel;

            public MouseBinding(Type reportType, MethodInfo update)
            {
                _reportType = reportType;
                _update = update;
                _buttonDown = RequireMethod(reportType, "ButtonDown");
                _buttonType = _buttonDown.GetParameters()[0].ParameterType;
                _x = RequireProperty("MouseX"); _y = RequireProperty("MouseY");
                _wheel = RequireProperty("WheelPosition"); _horizontalWheel = RequireProperty("HWheelPosition");
            }

            public bool Write(object native, MouseReport value)
            {
                var report = Activator.CreateInstance(_reportType)!;
                _x.SetValue(report, Convert.ChangeType(value.X, _x.PropertyType));
                _y.SetValue(report, Convert.ChangeType(value.Y, _y.PropertyType));
                _wheel.SetValue(report, unchecked((byte)value.Wheel));
                _horizontalWheel.SetValue(report, unchecked((byte)value.HorizontalWheel));
                for (var bit = 0; bit < 5; bit++)
                    if ((value.Buttons & (1 << bit)) != 0) _buttonDown.Invoke(report, [Enum.ToObject(_buttonType, 1 << bit)]);
                return (bool)(_update.Invoke(native, [report]) ?? false);
            }

            private PropertyInfo RequireProperty(string name) => _reportType.GetProperty(name) ?? throw new MissingMemberException(_reportType.FullName, name);
        }
    }
}
