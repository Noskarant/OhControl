using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OhControl.Configuration;
using OhControl.Lfly;
using OhControl.Multiplayer;
using OhControl.Radio;
using OhControl.Voice;

namespace OhControl
{
    public sealed class MainForm : Form
    {
        private readonly SimConnectClient _simConnect =
            new SimConnectClient();

        private readonly OhControlSettings _settings;
        private readonly VoiceSessionController _voice;

        private readonly Label _statusValue = new Label();
        private readonly Label _positionValue = new Label();
        private readonly Label _altitudeValue = new Label();
        private readonly Label _headingValue = new Label();
        private readonly Label _speedValue = new Label();
        private readonly Label _groundValue = new Label();
        private readonly Label _comValue = new Label();
        private readonly Label _weatherValue = new Label();
        private readonly Label _localPhaseValue = new Label();

        private readonly Label _voiceConfigValue = new Label();
        private readonly Label _stationValue = new Label();
        private readonly Label _voiceStatusValue = new Label();
        private readonly Label _pilotTextValue = new Label();
        private readonly Label _remoteRadioValue = new Label();
        private readonly Label _controllerTextValue = new Label();
        private readonly Label _feedbackValue = new Label();
        private readonly Label _pttValue = new Label();

        private readonly Label _multiplayerStatusValue = new Label();
        private readonly Label _remotePlayersValue = new Label();

        private readonly ComboBox _testStation = new ComboBox();
        private readonly Button _connectButton = new Button();
        private readonly Button _settingsButton = new Button();

        public MainForm()
        {
            _settings = OhControlSettings.Load();
            _voice = new VoiceSessionController(_settings);

            Text = "OhControl — " +
                   LflyAirport.Icao +
                   " " +
                   LflyAirport.Name;

            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(900, 760);
            Size = new Size(1040, 900);

            BuildUi();
            WireEvents();
            RefreshSettingsUi();

            FormClosed += (_, __) =>
            {
                _voice.Dispose();
                _simConnect.Dispose();
            };

            Shown += async (_, __) =>
            {
                await _voice.StartAsync();
                ConnectToSimulator();
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == SimConnectClient.WindowMessageId)
            {
                _simConnect.ReceiveMessage();
            }

            base.WndProc(ref m);
        }

        private void WireEvents()
        {
            _simConnect.Connected += OnConnected;
            _simConnect.Disconnected += OnDisconnected;
            _simConnect.TelemetryReceived += OnTelemetryReceived;
            _simConnect.Error += OnSimConnectError;

            _voice.StatusChanged += value =>
                Ui(() => _voiceStatusValue.Text = value);

            _voice.StationChanged += value =>
                Ui(() => _stationValue.Text = value);

            _voice.PilotTextReceived += value =>
                Ui(() => _pilotTextValue.Text = value);

            _voice.RemoteRadioTextReceived += value =>
                Ui(() => _remoteRadioValue.Text = value);

            _voice.ControllerTextGenerated += value =>
                Ui(() => _controllerTextValue.Text = value);

            _voice.FeedbackGenerated += value =>
                Ui(() => _feedbackValue.Text = value);

            _voice.MultiplayerStatusChanged += value =>
                Ui(() => _multiplayerStatusValue.Text = value);

            _voice.RemotePlayersChanged += players =>
                Ui(() => UpdateRemotePlayers(players));

            _voice.LocalPhaseChanged += value =>
                Ui(() => _localPhaseValue.Text = value);
        }

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "OhControl",
                Font = new Font(
                    Font.FontFamily,
                    24,
                    FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var subtitle = new Label
            {
                Text =
                    "VFR ATC trainer — " +
                    LflyAirport.Icao +
                    " " +
                    LflyAirport.Name,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 18)
            };

            InitializeLabels();

            _connectButton.Text =
                "Connect to MSFS 2024";

            _connectButton.AutoSize = true;
            _connectButton.Click +=
                (_, __) => ConnectToSimulator();

            _settingsButton.Text = "Settings";
            _settingsButton.AutoSize = true;
            _settingsButton.Click +=
                async (_, __) => await OpenSettingsAsync();

            _testStation.DropDownStyle =
                ComboBoxStyle.DropDownList;

            _testStation.Items.Add("Tower — 118.100");
            _testStation.Items.Add("Ground — 121.705");
            _testStation.Items.Add("ATIS — 128.130");
            _testStation.SelectedIndex = 0;

            _testStation.SelectedIndexChanged +=
                (_, __) =>
                {
                    RadioStationKind kind =
                        _testStation.SelectedIndex == 2
                            ? RadioStationKind.Atis
                            : _testStation.SelectedIndex == 1
                                ? RadioStationKind.Ground
                                : RadioStationKind.Tower;

                    _voice.SetTestStation(kind);
                };

            var actionPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 0, 0, 10)
            };

            actionPanel.Controls.Add(_connectButton);
            actionPanel.Controls.Add(_settingsButton);

            var testPanel = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 8, 0, 12)
            };

            testPanel.Controls.Add(new Label
            {
                Text = "Offline voice test:",
                AutoSize = true,
                Margin = new Padding(0, 7, 10, 0)
            });

            testPanel.Controls.Add(_testStation);

            var simTable = CreateTable(9);
            AddRow(simTable, 0, "SimConnect", _statusValue);
            AddRow(simTable, 1, "Position", _positionValue);
            AddRow(simTable, 2, "Altitude", _altitudeValue);
            AddRow(simTable, 3, "Heading", _headingValue);
            AddRow(simTable, 4, "Speed", _speedValue);
            AddRow(simTable, 5, "Aircraft state", _groundValue);
            AddRow(simTable, 6, "COM1", _comValue);
            AddRow(simTable, 7, "Weather", _weatherValue);
            AddRow(simTable, 8, "Detected phase", _localPhaseValue);

            var voiceTable = CreateTable(8);
            AddRow(voiceTable, 0, "ElevenLabs", _voiceConfigValue);
            AddRow(voiceTable, 1, "Current station", _stationValue);
            AddRow(voiceTable, 2, "Voice status", _voiceStatusValue);
            AddRow(voiceTable, 3, "PTT", _pttValue);
            AddRow(voiceTable, 4, "You said", _pilotTextValue);
            AddRow(voiceTable, 5, "Other pilot", _remoteRadioValue);
            AddRow(voiceTable, 6, "Controller", _controllerTextValue);
            AddRow(voiceTable, 7, "Training feedback", _feedbackValue);

            var multiplayerTable = CreateTable(2);
            AddRow(
                multiplayerTable,
                0,
                "Connection",
                _multiplayerStatusValue);

            AddRow(
                multiplayerTable,
                1,
                "Other aircraft",
                _remotePlayersValue);

            var root = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(32)
            };

            root.Controls.Add(title);
            root.Controls.Add(subtitle);
            root.Controls.Add(actionPanel);
            root.Controls.Add(testPanel);
            root.Controls.Add(simTable);

            root.Controls.Add(SectionLabel("Voice / radio"));
            root.Controls.Add(voiceTable);

            root.Controls.Add(SectionLabel("Multiplayer"));
            root.Controls.Add(multiplayerTable);

            Controls.Add(root);
        }

        private void InitializeLabels()
        {
            _statusValue.Text = "Disconnected";
            _positionValue.Text = "—";
            _altitudeValue.Text = "—";
            _headingValue.Text = "—";
            _speedValue.Text = "—";
            _groundValue.Text = "—";
            _comValue.Text = "—";
            _weatherValue.Text = "—";
            _localPhaseValue.Text = "—";

            _stationValue.Text =
                "BRON Tour 118.100 MHz (test mode)";

            _voiceStatusValue.Text =
                "Ready.";

            _pilotTextValue.Text = "—";
            _remoteRadioValue.Text = "—";
            _controllerTextValue.Text = "—";
            _feedbackValue.Text = "—";

            _multiplayerStatusValue.Text =
                "Disabled";

            _remotePlayersValue.Text =
                "No remote aircraft";

            foreach (Label label in new[]
            {
                _pilotTextValue,
                _remoteRadioValue,
                _controllerTextValue,
                _feedbackValue,
                _voiceStatusValue,
                _comValue,
                _weatherValue,
                _remotePlayersValue,
                _multiplayerStatusValue
            })
            {
                label.MaximumSize =
                    new Size(720, 0);

                label.AutoSize = true;
            }
        }

        private async Task OpenSettingsAsync()
        {
            using (var form = new SettingsForm(_settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }
            }

            RefreshSettingsUi();

            try
            {
                await _voice.UpdateSettingsAsync(_settings);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    "OhControl Settings",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void RefreshSettingsUi()
        {
            _voiceConfigValue.Text =
                _settings.IsElevenLabsConfigured
                    ? "Configured"
                    : "Not configured";

            _pttValue.Text =
                _voice.PttDescription;

            if (!_settings.MultiplayerEnabled)
            {
                _multiplayerStatusValue.Text =
                    "Disabled";
            }
            else if (!_settings.IsMultiplayerConfigured)
            {
                _multiplayerStatusValue.Text =
                    "Enabled but incomplete";
            }
            else
            {
                _multiplayerStatusValue.Text =
                    "Configured · room " +
                    _settings.MultiplayerRoomCode;
            }
        }

        private void UpdateRemotePlayers(
            IReadOnlyList<MultiplayerPlayerState> players)
        {
            if (players == null || players.Count == 0)
            {
                _remotePlayersValue.Text =
                    "No remote aircraft";
                return;
            }

            _remotePlayersValue.Text =
                string.Join(
                    Environment.NewLine,
                    players.Select(
                        player =>
                            (player.IsTransmitting
                                ? "TX · "
                                : "") +
                            player.ToString()));
        }

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font(
                    SystemFonts.DefaultFont.FontFamily,
                    16,
                    FontStyle.Bold),
                Margin = new Padding(0, 20, 0, 4)
            };
        }

        private static TableLayoutPanel CreateTable(
            int rows)
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = rows,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 10, 0, 10)
            };

            table.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Absolute,
                    190));

            table.ColumnStyles.Add(
                new ColumnStyle(
                    SizeType.Absolute,
                    720));

            return table;
        }

        private static void AddRow(
            TableLayoutPanel table,
            int row,
            string name,
            Control value)
        {
            var nameLabel = new Label
            {
                Text = name,
                AutoSize = true,
                Font = new Font(
                    SystemFonts.DefaultFont,
                    FontStyle.Bold),
                Margin = new Padding(
                    0,
                    8,
                    12,
                    8)
            };

            value.Margin =
                new Padding(0, 8, 0, 8);

            table.Controls.Add(
                nameLabel,
                0,
                row);

            table.Controls.Add(
                value,
                1,
                row);
        }

        private void ConnectToSimulator()
        {
            _connectButton.Enabled = false;
            _statusValue.Text = "Connecting…";
            _simConnect.Connect(Handle);
        }

        private void OnConnected()
        {
            Ui(() =>
            {
                _statusValue.Text = "Connected";
                _statusValue.ForeColor =
                    Color.DarkGreen;

                _connectButton.Enabled = false;
            });

            _voice.SetSimulatorConnected(true);
        }

        private void OnDisconnected()
        {
            Ui(() =>
            {
                _statusValue.Text =
                    "Disconnected";

                _statusValue.ForeColor =
                    Color.DarkRed;

                _connectButton.Enabled = true;
            });

            _voice.SetSimulatorConnected(false);
        }

        private void OnTelemetryReceived(
            TelemetrySnapshot telemetry)
        {
            _voice.UpdateTelemetry(telemetry);

            Ui(() =>
            {
                _positionValue.Text =
                    telemetry.LatitudeDeg.ToString("F6") +
                    ", " +
                    telemetry.LongitudeDeg.ToString("F6");

                _altitudeValue.Text =
                    telemetry.AltitudeFt.ToString("F0") +
                    " ft";

                _headingValue.Text =
                    telemetry.HeadingMagneticDeg.ToString("F0") +
                    "°";

                _speedValue.Text =
                    telemetry.IndicatedAirspeedKt.ToString("F0") +
                    " kt IAS · " +
                    telemetry.GroundSpeedKt.ToString("F0") +
                    " kt GS";

                _groundValue.Text =
                    telemetry.IsOnGround
                        ? "On ground"
                        : "Airborne";

                string ident =
                    string.IsNullOrWhiteSpace(
                        telemetry.Com1ActiveIdent)
                        ? ""
                        : " · " +
                          telemetry.Com1ActiveIdent;

                string type =
                    string.IsNullOrWhiteSpace(
                        telemetry.Com1ActiveType)
                        ? ""
                        : " [" +
                          telemetry.Com1ActiveType +
                          "]";

                _comValue.Text =
                    telemetry.Com1ActiveMhz.ToString("F3") +
                    " MHz" +
                    ident +
                    type +
                    " · RX " +
                    (telemetry.Com1Receive
                        ? "ON"
                        : "OFF") +
                    " · TX " +
                    (telemetry.Com1Transmit
                        ? "ON"
                        : "OFF");

                _weatherValue.Text =
                    telemetry.WindDirectionTrueDeg.ToString("F0") +
                    "°/" +
                    telemetry.WindSpeedKt.ToString("F0") +
                    " kt · " +
                    telemetry.AmbientTemperatureC.ToString("F0") +
                    " °C · QNH " +
                    telemetry.SeaLevelPressureMb.ToString("F0");
            });
        }

        private void OnSimConnectError(string message)
        {
            Ui(() =>
            {
                _statusValue.Text =
                    "Disconnected";

                _statusValue.ForeColor =
                    Color.DarkRed;

                _connectButton.Enabled = true;
            });

            _voice.SetSimulatorConnected(false);

            Ui(() => MessageBox.Show(
                this,
                message +
                Environment.NewLine +
                Environment.NewLine +
                "Voice and multiplayer test modes remain available without MSFS.",
                "OhControl — SimConnect",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information));
        }

        private void Ui(Action action)
        {
            if (IsDisposed)
            {
                return;
            }

            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }
    }
}
