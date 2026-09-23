using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OhControl.Audio;
using OhControl.Configuration;

namespace OhControl
{
    public sealed class SettingsForm : Form
    {
        private readonly OhControlSettings _settings;

        private readonly TextBox _callsign = new TextBox();
        private readonly TextBox _displayName = new TextBox();
        private readonly TextBox _aircraftType = new TextBox();
        private readonly TextBox _elevenKey = new TextBox();
        private readonly TextBox _elevenVoice = new TextBox();

        private readonly Label _keyboardPtt = new Label();
        private readonly Label _joystickPtt = new Label();

        private readonly CheckBox _multiplayerEnabled = new CheckBox();
        private readonly TextBox _supabaseUrl = new TextBox();
        private readonly TextBox _supabaseKey = new TextBox();
        private readonly TextBox _roomCode = new TextBox();

        private CancellationTokenSource _captureCancellation;
        private string _capturedKeyboardKey;
        private int _capturedJoystickDeviceId;
        private int _capturedJoystickButtonIndex;

        public SettingsForm(OhControlSettings settings)
        {
            _settings = settings;
            _capturedKeyboardKey = settings.PttKeyboardKey;
            _capturedJoystickDeviceId = settings.PttJoystickDeviceId;
            _capturedJoystickButtonIndex = settings.PttJoystickButtonIndex;

            Text = "OhControl Settings";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(700, 700);
            Size = new Size(760, 760);

            BuildUi();
        }

        private void BuildUi()
        {
            _callsign.Text = _settings.PilotCallsign;
            _displayName.Text = _settings.PlayerDisplayName;
            _aircraftType.Text = _settings.AircraftType;
            _elevenKey.Text = _settings.ElevenLabsApiKey;
            _elevenVoice.Text = _settings.ElevenLabsVoiceId;
            _elevenKey.UseSystemPasswordChar = true;

            _keyboardPtt.Text = string.IsNullOrWhiteSpace(_capturedKeyboardKey)
                ? "None"
                : _capturedKeyboardKey;

            _joystickPtt.Text = _capturedJoystickDeviceId >= 0
                ? "Joystick #" + _capturedJoystickDeviceId +
                  " · button " + (_capturedJoystickButtonIndex + 1)
                : "None";

            _multiplayerEnabled.Checked = _settings.MultiplayerEnabled;
            _supabaseUrl.Text = _settings.SupabaseProjectUrl;
            _supabaseKey.Text = _settings.SupabasePublishableKey;
            _supabaseKey.UseSystemPasswordChar = true;
            _roomCode.Text = _settings.MultiplayerRoomCode;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(20),
                ColumnCount = 2
            };

            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            AddSection(root, ref row, "Pilot");
            AddField(root, ref row, "Callsign", _callsign);
            AddField(root, ref row, "Name", _displayName);
            AddField(root, ref row, "Aircraft", _aircraftType);

            AddSection(root, ref row, "ElevenLabs");
            AddField(root, ref row, "API key", _elevenKey);
            AddField(root, ref row, "Voice ID", _elevenVoice);

            AddSection(root, ref row, "Push-to-talk");

            var keyboardPanel = new FlowLayoutPanel { AutoSize = true };
            keyboardPanel.Controls.Add(_keyboardPtt);

            var captureKeyboard = new Button
            {
                Text = "Capture keyboard key",
                AutoSize = true
            };

            captureKeyboard.Click += (_, __) => CaptureKeyboardKey();
            keyboardPanel.Controls.Add(captureKeyboard);
            AddField(root, ref row, "Keyboard", keyboardPanel);

            var joystickPanel = new FlowLayoutPanel { AutoSize = true };
            joystickPanel.Controls.Add(_joystickPtt);

            var captureJoystick = new Button
            {
                Text = "Capture joystick button",
                AutoSize = true
            };

            captureJoystick.Click += async (_, __) =>
                await CaptureJoystickAsync();

            joystickPanel.Controls.Add(captureJoystick);

            var clearJoystick = new Button
            {
                Text = "Clear",
                AutoSize = true
            };

            clearJoystick.Click += (_, __) =>
            {
                _capturedJoystickDeviceId = -1;
                _capturedJoystickButtonIndex = -1;
                _joystickPtt.Text = "None";
            };

            joystickPanel.Controls.Add(clearJoystick);
            AddField(root, ref row, "Joystick / yoke", joystickPanel);

            AddSection(root, ref row, "Multiplayer");
            _multiplayerEnabled.Text = "Enable shared OhControl session";
            AddField(root, ref row, "Enabled", _multiplayerEnabled);
            AddField(root, ref row, "Supabase project URL", _supabaseUrl);
            AddField(root, ref row, "Publishable key", _supabaseKey);

            var roomPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            _roomCode.Width = 250;
            roomPanel.Controls.Add(_roomCode);

            var generateRoom = new Button
            {
                Text = "Generate",
                AutoSize = true
            };

            generateRoom.Click += (_, __) =>
            {
                _roomCode.Text =
                    "lfly-" +
                    Guid.NewGuid()
                        .ToString("N")
                        .Substring(0, 10);
            };

            roomPanel.Controls.Add(generateRoom);
            AddField(root, ref row, "Room code", roomPanel);

            var note = new Label
            {
                Text =
                    "Use the same Supabase project and room code on both PCs. " +
                    "Use a publishable key, never a service-role/secret key.",
                AutoSize = true,
                MaximumSize = new Size(470, 0),
                ForeColor = Color.DimGray
            };

            AddField(root, ref row, "", note);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            var save = new Button { Text = "Save", AutoSize = true };
            save.Click += (_, __) => SaveAndClose();
            buttons.Controls.Add(save);

            var cancel = new Button { Text = "Cancel", AutoSize = true };
            cancel.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            buttons.Controls.Add(cancel);
            AddField(root, ref row, "", buttons);

            Controls.Add(root);
        }

        private void CaptureKeyboardKey()
        {
            _keyboardPtt.Text = "Press a key…";
            KeyPreview = true;

            KeyEventHandler handler = null;

            handler = (_, args) =>
            {
                KeyDown -= handler;
                KeyPreview = false;
                _capturedKeyboardKey = args.KeyCode.ToString();
                _keyboardPtt.Text = _capturedKeyboardKey;
                args.SuppressKeyPress = true;
            };

            KeyDown += handler;
            Focus();
        }

        private async Task CaptureJoystickAsync()
        {
            _captureCancellation?.Cancel();
            _captureCancellation?.Dispose();
            _captureCancellation = new CancellationTokenSource();

            _joystickPtt.Text = "Press a joystick button…";

            try
            {
                Tuple<int, int> binding =
                    await JoystickPttMonitor.CaptureNextButtonAsync(
                        TimeSpan.FromSeconds(10),
                        _captureCancellation.Token);

                if (binding == null)
                {
                    _joystickPtt.Text = "No button detected";
                    return;
                }

                _capturedJoystickDeviceId = binding.Item1;
                _capturedJoystickButtonIndex = binding.Item2;

                _joystickPtt.Text =
                    "Joystick #" + binding.Item1 +
                    " · button " + (binding.Item2 + 1);
            }
            catch (OperationCanceledException)
            {
                _joystickPtt.Text = "Capture cancelled";
            }
        }

        private void SaveAndClose()
        {
            _settings.PilotCallsign = _callsign.Text.Trim();
            _settings.PlayerDisplayName = _displayName.Text.Trim();
            _settings.AircraftType = _aircraftType.Text.Trim();
            _settings.ElevenLabsApiKey = _elevenKey.Text.Trim();
            _settings.ElevenLabsVoiceId = _elevenVoice.Text.Trim();

            _settings.PttKeyboardKey = _capturedKeyboardKey;
            _settings.PttJoystickDeviceId = _capturedJoystickDeviceId;
            _settings.PttJoystickButtonIndex = _capturedJoystickButtonIndex;

            _settings.MultiplayerEnabled = _multiplayerEnabled.Checked;
            _settings.SupabaseProjectUrl = _supabaseUrl.Text.Trim();
            _settings.SupabasePublishableKey = _supabaseKey.Text.Trim();
            _settings.MultiplayerRoomCode = _roomCode.Text.Trim();

            _settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        }

        private static void AddSection(
            TableLayoutPanel table,
            ref int row,
            string text)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont.FontFamily, 12, FontStyle.Bold),
                Margin = new Padding(0, 18, 0, 6)
            };

            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 2);
            row++;
        }

        private static void AddField(
            TableLayoutPanel table,
            ref int row,
            string name,
            Control control)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var label = new Label
            {
                Text = name,
                AutoSize = true,
                Margin = new Padding(0, 8, 10, 8)
            };

            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 5, 0, 5);

            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
            row++;
        }
    }
}
