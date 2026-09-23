using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace OhControl.Audio
{
    public sealed class JoystickDeviceInfo
    {
        public int DeviceId { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name + " (#" + DeviceId + ")";
        }
    }

    public sealed class JoystickPttMonitor : IDisposable
    {
        private const uint JoyReturnButtons = 0x00000080;

        private readonly Timer _timer;
        private int _deviceId = -1;
        private int _buttonIndex = -1;
        private bool _wasDown;

        public event Action Pressed;
        public event Action Released;

        public JoystickPttMonitor()
        {
            _timer = new Timer(Poll, null, 25, 25);
        }

        public void Rebind(int deviceId, int buttonIndex)
        {
            _deviceId = deviceId;
            _buttonIndex = buttonIndex;
            _wasDown = false;
        }

        public void Dispose()
        {
            _timer.Dispose();
        }

        public static IReadOnlyList<JoystickDeviceInfo> GetDevices()
        {
            var result = new List<JoystickDeviceInfo>();
            uint count = joyGetNumDevs();

            for (uint id = 0; id < count; id++)
            {
                var caps = new JoyCaps();
                int code = joyGetDevCaps(
                    new IntPtr(id),
                    ref caps,
                    (uint)Marshal.SizeOf(typeof(JoyCaps)));

                if (code == 0)
                {
                    result.Add(new JoystickDeviceInfo
                    {
                        DeviceId = (int)id,
                        Name = string.IsNullOrWhiteSpace(caps.szPname)
                            ? "Joystick"
                            : caps.szPname
                    });
                }
            }

            return result;
        }

        public static async Task<Tuple<int, int>> CaptureNextButtonAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            uint count = joyGetNumDevs();

            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (uint id = 0; id < count; id++)
                {
                    var info = CreateInfo();

                    if (joyGetPosEx(id, ref info) != 0 || info.dwButtons == 0)
                    {
                        continue;
                    }

                    for (int button = 0; button < 32; button++)
                    {
                        if ((info.dwButtons & (1u << button)) != 0)
                        {
                            return Tuple.Create((int)id, button);
                        }
                    }
                }

                await Task.Delay(35, cancellationToken).ConfigureAwait(false);
            }

            return null;
        }

        private void Poll(object state)
        {
            int deviceId = _deviceId;
            int buttonIndex = _buttonIndex;

            if (deviceId < 0 || buttonIndex < 0 || buttonIndex > 31)
            {
                return;
            }

            var info = CreateInfo();

            if (joyGetPosEx((uint)deviceId, ref info) != 0)
            {
                return;
            }

            bool isDown = (info.dwButtons & (1u << buttonIndex)) != 0;

            if (isDown && !_wasDown)
            {
                _wasDown = true;
                Pressed?.Invoke();
            }
            else if (!isDown && _wasDown)
            {
                _wasDown = false;
                Released?.Invoke();
            }
        }

        private static JoyInfoEx CreateInfo()
        {
            return new JoyInfoEx
            {
                dwSize = (uint)Marshal.SizeOf(typeof(JoyInfoEx)),
                dwFlags = JoyReturnButtons
            };
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct JoyCaps
        {
            public ushort wMid;
            public ushort wPid;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;

            public uint wXmin;
            public uint wXmax;
            public uint wYmin;
            public uint wYmax;
            public uint wZmin;
            public uint wZmax;
            public uint wNumButtons;
            public uint wPeriodMin;
            public uint wPeriodMax;
            public uint wRmin;
            public uint wRmax;
            public uint wUmin;
            public uint wUmax;
            public uint wVmin;
            public uint wVmax;
            public uint wCaps;
            public uint wMaxAxes;
            public uint wNumAxes;
            public uint wMaxButtons;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szRegKey;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szOEMVxD;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JoyInfoEx
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int joyGetDevCaps(
            IntPtr uJoyID,
            ref JoyCaps pjc,
            uint cbjc);

        [DllImport("winmm.dll")]
        private static extern int joyGetPosEx(
            uint uJoyID,
            ref JoyInfoEx pji);
    }
}
