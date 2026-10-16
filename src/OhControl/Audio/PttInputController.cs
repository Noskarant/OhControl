using System;
using System.Windows.Forms;
using OhControl.Configuration;

namespace OhControl.Audio
{
    public sealed class PttInputController : IDisposable
    {
        private PushToTalkHook _keyboard;
        private readonly JoystickPttMonitor _joystick = new JoystickPttMonitor();
        private bool _isPressed;

        public event Action Pressed;
        public event Action Released;

        public PttInputController(OhControlSettings settings)
        {
            _joystick.Pressed += OnPressed;
            _joystick.Released += OnReleased;
            Rebind(settings);
        }

        public void Rebind(OhControlSettings settings)
        {
            _keyboard?.Dispose();
            _keyboard = null;

            if (Enum.TryParse(settings.PttKeyboardKey, true, out Keys key) &&
                key != Keys.None)
            {
                _keyboard = new PushToTalkHook(key);
                _keyboard.Pressed += OnPressed;
                _keyboard.Released += OnReleased;
            }

            _joystick.Rebind(
                settings.PttJoystickDeviceId,
                settings.PttJoystickButtonIndex);
        }

        public void Dispose()
        {
            if (_keyboard != null)
            {
                _keyboard.Pressed -= OnPressed;
                _keyboard.Released -= OnReleased;
                _keyboard.Dispose();
                _keyboard = null;
            }

            _joystick.Pressed -= OnPressed;
            _joystick.Released -= OnReleased;
            _joystick.Dispose();
        }

        private void OnPressed()
        {
            if (_isPressed)
            {
                return;
            }

            _isPressed = true;
            Pressed?.Invoke();
        }

        private void OnReleased()
        {
            if (!_isPressed)
            {
                return;
            }

            _isPressed = false;
            Released?.Invoke();
        }
    }
}
