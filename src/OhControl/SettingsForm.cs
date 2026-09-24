using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OhControl.Audio;
using OhControl.Configuration;
using OhControl.Ui;

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

        private readonly ComboBox _inputDevice = new ComboBox();
        private readonly ComboBox _outputDevice = new ComboBox();
        private readonly TrackBar _atcVolume = new TrackBar();
        private readonly Label _atcVolumeValue = new Label();

        private readonly Label _keyboardPtt = new Label();
        private readonly Label _joystickPtt = new Label();

        private readonly CheckBox _multiplayerEnabled = new CheckBox();
        private readonly TextBox _supabaseUrl = new TextBox();
        private readonly TextBox _supabaseKey = new TextBox();
        private readonly TextBox _roomCode = new TextBox();

        private readonly Button _saveButton = new Button();
        private readonly Button _cancelButton = new Button();

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

            Text = "Réglages — OhControl";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(860, 690);
            Size = new Size(960, 760);
            BackColor = OhControlTheme.Background;
            ForeColor = OhControlTheme.TextPrimary;
            Font = OhControlTheme.Font(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
        }

        private void BuildUi()
        {
            LoadValues();
            PopulateAudioDevices();

            var header = BuildHeader();
            var footer = BuildFooter();

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(24, 10, 12, 12),
                ColumnCount = 2,
                RowCount = 2
            };

            content.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            content.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            content.RowStyles.Add(
                new RowStyle(SizeType.Percent, 54f));

            content.RowStyles.Add(
                new RowStyle(SizeType.Percent, 46f));

            content.Controls.Add(
                BuildPilotVoiceCard(),
                0,
                0);

            content.Controls.Add(
                BuildAudioPttCard(),
                1,
                0);

            var multi = BuildMultiplayerCard();
            content.Controls.Add(multi, 0, 1);
            content.SetColumnSpan(multi, 2);

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private Control BuildHeader()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(26, 16, 26, 10)
            };

            var title = new Label
            {
                Text = "Réglages",
                AutoSize = true,
                Font = OhControlTheme.Font(19f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary,
                Location = new Point(26, 14)
            };

            var subtitle = new Label
            {
                Text = "Tout ce qui concerne ton poste radio et ta session de vol.",
                AutoSize = true,
                Font = OhControlTheme.Font(9f),
                ForeColor = OhControlTheme.TextSecondary,
                Location = new Point(28, 47)
            };

            panel.Controls.Add(title);
            panel.Controls.Add(subtitle);
            return panel;
        }

        private Control BuildFooter()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 66,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(24, 12, 24, 12)
            };

            _saveButton.Text = "Sauvegarder";
            _saveButton.AutoSize = true;
            OhControlTheme.StylePrimaryButton(_saveButton);
            _saveButton.Click += (_, __) => SaveAndClose();

            _cancelButton.Text = "Annuler";
            _cancelButton.AutoSize = true;
            OhControlTheme.StyleSecondaryButton(_cancelButton);
            _cancelButton.Click += (_, __) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Dock = DockStyle.Right,
                BackColor = Color.Transparent
            };

            actions.Controls.Add(_saveButton);
            actions.Controls.Add(_cancelButton);
            panel.Controls.Add(actions);

            return panel;
        }

        private Control BuildPilotVoiceCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = CreateFormLayout(7);
            AddSectionHeader(layout, 0, "PILOTE & VOIX");

            AddField(
                layout,
                1,
                "Indicatif",
                "Ex. F-GABC",
                _callsign);

            AddField(
                layout,
                2,
                "Prénom",
                "Affiché uniquement dans OhControl",
                _displayName);

            AddField(
                layout,
                3,
                "Avion",
                "Ex. DR400",
                _aircraftType);

            AddDivider(layout, 4);

            AddField(
                layout,
                5,
                "Clé ElevenLabs",
                "Stockée uniquement sur ce PC",
                _elevenKey);

            AddField(
                layout,
                6,
                "Voice ID ATC",
                "Voix utilisée pour le contrôleur",
                _elevenVoice);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildAudioPttCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = CreateFormLayout(8);
            AddSectionHeader(layout, 0, "AUDIO & PTT");

            AddField(
                layout,
                1,
                "Microphone",
                "Ta voix pilote",
                _inputDevice);

            AddField(
                layout,
                2,
                "Sortie radio",
                "Casque ou haut-parleurs",
                _outputDevice);

            var volumeRow =
                BuildVolumeRow();

            AddCustomField(
                layout,
                3,
                "Volume ATC",
                "ATIS, Sol et Tour",
                volumeRow);

            AddDivider(layout, 4);

            var keyboardRow =
                BuildBindingRow(
                    _keyboardPtt,
                    "Enregistrer une touche",
                    CaptureKeyboardKey);

            AddCustomField(
                layout,
                5,
                "Clavier",
                "Maintiens cette touche pour émettre",
                keyboardRow);

            var joystickRow =
                BuildJoystickBindingRow();

            AddCustomField(
                layout,
                6,
                "Joystick / manche",
                "Optionnel · bouton physique PTT",
                joystickRow);

            var note = new Label
            {
                Text =
                    "Le PTT reste global : il fonctionne même lorsque MSFS est la fenêtre active.",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.4f),
                Margin = new Padding(0, 8, 0, 0)
            };

            layout.Controls.Add(note, 1, 7);
            card.Controls.Add(layout);
            return card;
        }

        private Control BuildMultiplayerCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = Color.Transparent
            };

            outer.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            outer.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            var title = new Label
            {
                Text = "SESSION À DEUX",
                AutoSize = true,
                Font = OhControlTheme.Font(8f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextSecondary,
                Margin = new Padding(0, 4, 18, 0)
            };

            _multiplayerEnabled.Text =
                "Activer quand la session partagée sera configurée";

            _multiplayerEnabled.AutoSize = true;
            _multiplayerEnabled.ForeColor =
                OhControlTheme.TextPrimary;

            _multiplayerEnabled.Font =
                OhControlTheme.Font(9f);

            _multiplayerEnabled.CheckedChanged +=
                (_, __) => RefreshMultiplayerEnabledState();

            header.Controls.Add(title);
            header.Controls.Add(_multiplayerEnabled);

            outer.Controls.Add(header, 0, 0);
            outer.SetColumnSpan(header, 2);

            var note = new Label
            {
                Text =
                    "Pour l’instant tu peux laisser ce bloc désactivé. À la fin, toi et Kélian utiliserez le même projet et le même code de salle.",
                AutoSize = true,
                MaximumSize = new Size(820, 0),
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.5f),
                Margin = new Padding(0, 8, 0, 12)
            };

            outer.Controls.Add(note, 0, 1);
            outer.SetColumnSpan(note, 2);

            var left = CreateCompactFields();
            AddCompactField(left, 0, "Projet Supabase", _supabaseUrl);
            AddCompactField(left, 1, "Clé publishable", _supabaseKey);

            var right = CreateCompactFields();

            var roomPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _roomCode.Width = 230;
            roomPanel.Controls.Add(_roomCode);

            var generateRoom = new Button
            {
                Text = "Générer",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(generateRoom);

            generateRoom.Click += (_, __) =>
            {
                _roomCode.Text =
                    "lfly-" +
                    Guid.NewGuid()
                        .ToString("N")
                        .Substring(0, 10);
            };

            roomPanel.Controls.Add(generateRoom);

            AddCompactCustomField(
                right,
                0,
                "Code de salle",
                roomPanel);

            var safety = new Label
            {
                Text = "Utilise uniquement une clé publishable, jamais une clé secrète/service-role.",
                AutoSize = true,
                MaximumSize = new Size(360, 0),
                ForeColor = OhControlTheme.Warning,
                Font = OhControlTheme.Font(8.3f),
                Margin = new Padding(0, 12, 0, 0)
            };

            right.Controls.Add(safety, 1, 1);

            outer.Controls.Add(left, 0, 2);
            outer.Controls.Add(right, 1, 2);

            card.Controls.Add(outer);

            RefreshMultiplayerEnabledState();
            return card;
        }

        private void LoadValues()
        {
            _callsign.Text = _settings.PilotCallsign;
            _displayName.Text = _settings.PlayerDisplayName;
            _aircraftType.Text = _settings.AircraftType;

            _elevenKey.Text = _settings.ElevenLabsApiKey;
            _elevenKey.UseSystemPasswordChar = true;

            _elevenVoice.Text = _settings.ElevenLabsVoiceId;

            _keyboardPtt.Text =
                string.IsNullOrWhiteSpace(_capturedKeyboardKey)
                    ? "Aucune"
                    : _capturedKeyboardKey;

            _joystickPtt.Text =
                _capturedJoystickDeviceId >= 0
                    ? "Joystick #" +
                      _capturedJoystickDeviceId +
                      " · bouton " +
                      (_capturedJoystickButtonIndex + 1)
                    : "Aucun";

            _multiplayerEnabled.Checked =
                _settings.MultiplayerEnabled;

            _supabaseUrl.Text =
                _settings.SupabaseProjectUrl;

            _supabaseKey.Text =
                _settings.SupabasePublishableKey;

            _supabaseKey.UseSystemPasswordChar = true;

            _roomCode.Text =
                _settings.MultiplayerRoomCode;

            _atcVolume.Value =
                Math.Max(
                    _atcVolume.Minimum,
                    Math.Min(
                        _atcVolume.Maximum,
                        _settings.AtcVolumePercent));

            _atcVolumeValue.Text =
                _atcVolume.Value + " %";

            foreach (TextBox textBox in new[]
            {
                _callsign,
                _displayName,
                _aircraftType,
                _elevenKey,
                _elevenVoice,
                _supabaseUrl,
                _supabaseKey,
                _roomCode
            })
            {
                OhControlTheme.StyleTextBox(textBox);
            }
        }

        private void PopulateAudioDevices()
        {
            _inputDevice.DropDownStyle =
                ComboBoxStyle.DropDownList;

            _outputDevice.DropDownStyle =
                ComboBoxStyle.DropDownList;

            OhControlTheme.StyleComboBox(_inputDevice);
            OhControlTheme.StyleComboBox(_outputDevice);

            foreach (AudioDeviceInfo device
                in AudioDeviceCatalog.GetInputDevices())
            {
                _inputDevice.Items.Add(device);

                if (device.DeviceNumber ==
                    _settings.MicrophoneDeviceNumber)
                {
                    _inputDevice.SelectedItem = device;
                }
            }

            foreach (AudioDeviceInfo device
                in AudioDeviceCatalog.GetOutputDevices())
            {
                _outputDevice.Items.Add(device);

                if (device.DeviceNumber ==
                    _settings.OutputDeviceNumber)
                {
                    _outputDevice.SelectedItem = device;
                }
            }

            if (_inputDevice.SelectedIndex < 0 &&
                _inputDevice.Items.Count > 0)
            {
                _inputDevice.SelectedIndex = 0;
            }

            if (_outputDevice.SelectedIndex < 0 &&
                _outputDevice.Items.Count > 0)
            {
                _outputDevice.SelectedIndex = 0;
            }
        }

        private Panel BuildVolumeRow()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 46,
                BackColor = Color.Transparent
            };

            _atcVolume.Minimum = 0;
            _atcVolume.Maximum = 100;
            _atcVolume.TickFrequency = 10;
            _atcVolume.SmallChange = 5;
            _atcVolume.LargeChange = 10;
            _atcVolume.AutoSize = false;
            _atcVolume.Height = 36;
            _atcVolume.Width = 250;
            _atcVolume.BackColor = OhControlTheme.Surface;

            _atcVolumeValue.AutoSize = true;
            _atcVolumeValue.Font =
                OhControlTheme.Font(
                    10f,
                    FontStyle.Bold);

            _atcVolumeValue.ForeColor =
                OhControlTheme.Accent;

            _atcVolumeValue.Location =
                new Point(265, 10);

            _atcVolume.Scroll +=
                (_, __) =>
                {
                    _atcVolumeValue.Text =
                        _atcVolume.Value + " %";
                };

            panel.Controls.Add(_atcVolume);
            panel.Controls.Add(_atcVolumeValue);

            return panel;
        }

        private Panel BuildBindingRow(
            Label value,
            string buttonText,
            Action action)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 38,
                BackColor = Color.Transparent
            };

            value.AutoSize = true;
            value.Font =
                OhControlTheme.Font(10f, FontStyle.Bold);

            value.ForeColor =
                OhControlTheme.Accent;

            value.Location = new Point(0, 9);

            var button = new Button
            {
                Text = buttonText,
                AutoSize = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            OhControlTheme.StyleSecondaryButton(button);
            button.Click += (_, __) => action();

            panel.Controls.Add(value);
            panel.Controls.Add(button);

            panel.Resize += (_, __) =>
            {
                button.Location =
                    new Point(
                        panel.ClientSize.Width -
                        button.Width,
                        0);
            };

            return panel;
        }

        private Panel BuildJoystickBindingRow()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 38,
                BackColor = Color.Transparent
            };

            _joystickPtt.AutoSize = true;
            _joystickPtt.Font =
                OhControlTheme.Font(10f, FontStyle.Bold);

            _joystickPtt.ForeColor =
                OhControlTheme.Accent;

            _joystickPtt.Location =
                new Point(0, 9);

            var capture = new Button
            {
                Text = "Enregistrer",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(capture);

            capture.Click +=
                async (_, __) =>
                    await CaptureJoystickAsync();

            var clear = new Button
            {
                Text = "Effacer",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(clear);

            clear.Click += (_, __) =>
            {
                _capturedJoystickDeviceId = -1;
                _capturedJoystickButtonIndex = -1;
                _joystickPtt.Text = "Aucun";
            };

            panel.Controls.Add(_joystickPtt);
            panel.Controls.Add(capture);
            panel.Controls.Add(clear);

            panel.Resize += (_, __) =>
            {
                clear.Location =
                    new Point(
                        panel.ClientSize.Width -
                        clear.Width,
                        0);

                capture.Location =
                    new Point(
                        clear.Left -
                        capture.Width -
                        6,
                        0);
            };

            return panel;
        }

        private static TableLayoutPanel CreateFormLayout(int rows)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = rows,
                BackColor = Color.Transparent
            };

            table.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 145));

            table.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            for (int i = 0; i < rows; i++)
            {
                table.RowStyles.Add(
                    new RowStyle(SizeType.Percent, 100f / rows));
            }

            return table;
        }

        private static TableLayoutPanel CreateCompactFields()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 14, 0)
            };

            table.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 120));

            table.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            table.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50f));

            table.RowStyles.Add(
                new RowStyle(SizeType.Percent, 50f));

            return table;
        }

        private static void AddSectionHeader(
            TableLayoutPanel table,
            int row,
            string title)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = OhControlTheme.Font(8f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextSecondary,
                Margin = new Padding(0, 2, 0, 8)
            };

            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 2);
        }

        private static void AddDivider(
            TableLayoutPanel table,
            int row)
        {
            var line = new Panel
            {
                Dock = DockStyle.Top,
                Height = 1,
                BackColor = OhControlTheme.Border,
                Margin = new Padding(0, 9, 0, 9)
            };

            table.Controls.Add(line, 0, row);
            table.SetColumnSpan(line, 2);
        }

        private static void AddField(
            TableLayoutPanel table,
            int row,
            string title,
            string hint,
            Control control)
        {
            var labelPanel =
                CreateFieldLabel(title, hint);

            control.Dock = DockStyle.Top;
            control.Margin =
                new Padding(0, 7, 0, 0);

            table.Controls.Add(
                labelPanel,
                0,
                row);

            table.Controls.Add(
                control,
                1,
                row);
        }

        private static void AddCustomField(
            TableLayoutPanel table,
            int row,
            string title,
            string hint,
            Control control)
        {
            var labelPanel =
                CreateFieldLabel(title, hint);

            control.Margin =
                new Padding(0, 4, 0, 0);

            table.Controls.Add(
                labelPanel,
                0,
                row);

            table.Controls.Add(
                control,
                1,
                row);
        }

        private static Panel CreateFieldLabel(
            string title,
            string hint)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 12, 0)
            };

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = OhControlTheme.Font(9f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary
            };

            var hintLabel = new Label
            {
                Text = hint,
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = OhControlTheme.Font(7.8f),
                ForeColor = OhControlTheme.TextSecondary,
                Padding = new Padding(0, 3, 0, 0),
                MaximumSize = new Size(135, 0)
            };

            panel.Controls.Add(hintLabel);
            panel.Controls.Add(titleLabel);

            return panel;
        }

        private static void AddCompactField(
            TableLayoutPanel table,
            int row,
            string title,
            Control control)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.5f, FontStyle.Bold),
                Margin = new Padding(0, 8, 8, 0)
            };

            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, 5, 0, 5);

            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private static void AddCompactCustomField(
            TableLayoutPanel table,
            int row,
            string title,
            Control control)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.5f, FontStyle.Bold),
                Margin = new Padding(0, 8, 8, 0)
            };

            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
        }

        private void RefreshMultiplayerEnabledState()
        {
            bool enabled =
                _multiplayerEnabled.Checked;

            _supabaseUrl.Enabled = enabled;
            _supabaseKey.Enabled = enabled;
            _roomCode.Enabled = enabled;
        }

        private void CaptureKeyboardKey()
        {
            _keyboardPtt.Text = "Appuie sur une touche…";
            _keyboardPtt.ForeColor =
                OhControlTheme.Warning;

            KeyPreview = true;

            KeyEventHandler handler = null;

            handler = (_, args) =>
            {
                KeyDown -= handler;
                KeyPreview = false;

                _capturedKeyboardKey =
                    args.KeyCode.ToString();

                _keyboardPtt.Text =
                    _capturedKeyboardKey;

                _keyboardPtt.ForeColor =
                    OhControlTheme.Accent;

                args.SuppressKeyPress = true;
            };

            KeyDown += handler;
            Focus();
        }

        private async Task CaptureJoystickAsync()
        {
            _captureCancellation?.Cancel();
            _captureCancellation?.Dispose();

            _captureCancellation =
                new CancellationTokenSource();

            _joystickPtt.Text =
                "Appuie sur un bouton…";

            _joystickPtt.ForeColor =
                OhControlTheme.Warning;

            try
            {
                Tuple<int, int> binding =
                    await JoystickPttMonitor
                        .CaptureNextButtonAsync(
                            TimeSpan.FromSeconds(10),
                            _captureCancellation.Token);

                if (binding == null)
                {
                    _joystickPtt.Text =
                        "Aucun bouton détecté";

                    _joystickPtt.ForeColor =
                        OhControlTheme.Danger;

                    return;
                }

                _capturedJoystickDeviceId =
                    binding.Item1;

                _capturedJoystickButtonIndex =
                    binding.Item2;

                _joystickPtt.Text =
                    "Joystick #" +
                    binding.Item1 +
                    " · bouton " +
                    (binding.Item2 + 1);

                _joystickPtt.ForeColor =
                    OhControlTheme.Accent;
            }
            catch (OperationCanceledException)
            {
                _joystickPtt.Text =
                    "Capture annulée";

                _joystickPtt.ForeColor =
                    OhControlTheme.TextSecondary;
            }
        }

        private void SaveAndClose()
        {
            _settings.PilotCallsign =
                _callsign.Text.Trim();

            _settings.PlayerDisplayName =
                _displayName.Text.Trim();

            _settings.AircraftType =
                _aircraftType.Text.Trim();

            _settings.ElevenLabsApiKey =
                _elevenKey.Text.Trim();

            _settings.ElevenLabsVoiceId =
                _elevenVoice.Text.Trim();

            if (_inputDevice.SelectedItem
                is AudioDeviceInfo input)
            {
                _settings.MicrophoneDeviceNumber =
                    input.DeviceNumber;
            }

            if (_outputDevice.SelectedItem
                is AudioDeviceInfo output)
            {
                _settings.OutputDeviceNumber =
                    output.DeviceNumber;
            }

            _settings.AtcVolumePercent =
                _atcVolume.Value;

            _settings.PttKeyboardKey =
                _capturedKeyboardKey;

            _settings.PttJoystickDeviceId =
                _capturedJoystickDeviceId;

            _settings.PttJoystickButtonIndex =
                _capturedJoystickButtonIndex;

            _settings.MultiplayerEnabled =
                _multiplayerEnabled.Checked;

            _settings.SupabaseProjectUrl =
                _supabaseUrl.Text.Trim();

            _settings.SupabasePublishableKey =
                _supabaseKey.Text.Trim();

            _settings.MultiplayerRoomCode =
                _roomCode.Text.Trim();

            _settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
