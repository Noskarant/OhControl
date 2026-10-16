using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OhControl.Audio
{
    public sealed class PushToTalkHook : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;

        private readonly Keys _pushToTalkKey;
        private readonly LowLevelKeyboardProc _callback;
        private IntPtr _hookId = IntPtr.Zero;
        private bool _isDown;

        public event Action Pressed;
        public event Action Released;

        public PushToTalkHook(Keys pushToTalkKey = Keys.F12)
        {
            _pushToTalkKey = pushToTalkKey;
            _callback = HookCallback;
            _hookId = SetHook(_callback);
        }

        public void Dispose()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int virtualKey = Marshal.ReadInt32(lParam);

                if (virtualKey == (int)_pushToTalkKey)
                {
                    int message = wParam.ToInt32();

                    if ((message == WmKeyDown || message == WmSysKeyDown) && !_isDown)
                    {
                        _isDown = true;
                        Pressed?.Invoke();
                    }
                    else if ((message == WmKeyUp || message == WmSysKeyUp) && _isDown)
                    {
                        _isDown = false;
                        Released?.Invoke();
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process process = Process.GetCurrentProcess())
            using (ProcessModule module = process.MainModule)
            {
                return SetWindowsHookEx(
                    WhKeyboardLl,
                    proc,
                    GetModuleHandle(module?.ModuleName),
                    0);
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
