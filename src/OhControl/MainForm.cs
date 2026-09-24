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
using OhControl.Ui;
using OhControl.Voice;

namespace OhControl
{
    public sealed class MainForm : Form
    {
        private readonly SimConnectClient _simConnect =
            new SimConnectClient();

        private readonly OhControlSettings _settings;
        private readonly VoiceSessionController _voice;

        private readonly StatusPill _simPill = new StatusPill();
        private readonly StatusPill _voicePill = new StatusPill();
        private readonly StatusPill _multiplayerPill = new StatusPill();

        private readonly Label _stationValue = new Label();
        private readonly Label _comValue = new Label();
        private readonly Label _voiceStatusValue = new Label();
        private readonly Label _pttValue = new Label();

        private readonly Label _positionValue = new Label();
        private readonly Label _altitudeValue = new Label();
        private readonly Label _headingValue = new Label();
        private readonly Label _speedValue = new Label();
        private readonly Label _groundValue = new Label();
        private readonly Label _weatherValue = new Label();
        private readonly Label _localPhaseValue = new Label();

        private readonly Label _pilotTextValue = new Label();
        private readonly Label _remoteRadioValue = new Label();
        private readonly Label _controllerTextValue = new Label();
        private readonly Label _feedbackValue = new Label();

        private readonly FlowLayoutPanel _conversationLog =
            new FlowLayoutPanel();

        private readonly List<Control> _conversationEntries =
            new List<Control>();

        private readonly Label _voiceConfigValue = new Label();
        private readonly Label _multiplayerStatusValue = new Label();
        private readonly Label _remotePlayersValue = new Label();

        private readonly ComboBox _testStation = new ComboBox();
        private readonly Button _connectButton = new Button();
        private readonly Button _settingsButton = new Button();

        public MainForm()
        {
            _settings = OhControlSettings.Load();
            _voice = new VoiceSessionController(_settings);

            Text = "OhControl — LFLY Lyon-Bron";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1040, 720);
            Size = new Size(1180, 820);
            BackColor = OhControlTheme.Background;
            ForeColor = OhControlTheme.TextPrimary;
            Font = OhControlTheme.Font(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;

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

        private void BuildUi()
        {
            SuspendLayout();

            Controls.Add(BuildWorkspace());
            Controls.Add(BuildHeader());

            InitializeLabels();
            ResumeLayout(true);
        }

        private Control BuildHeader()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(26, 16, 26, 10)
            };

            var title = new Label
            {
                Text = "OhControl",
                AutoSize = true,
                Font = OhControlTheme.Font(20f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary,
                Location = new Point(26, 13)
            };

            var subtitle = new Label
            {
                Text = "LFLY  ·  Lyon-Bron  ·  entraînement VFR",
                AutoSize = true,
                Font = OhControlTheme.Font(9f),
                ForeColor = OhControlTheme.TextSecondary,
                Location = new Point(28, 46)
            };

            _settingsButton.Text = "Réglages";
            _settingsButton.AutoSize = true;
            OhControlTheme.StyleSecondaryButton(_settingsButton);
            _settingsButton.Click +=
                async (_, __) => await OpenSettingsAsync();

            _connectButton.Text = "Connecter MSFS";
            _connectButton.AutoSize = true;
            OhControlTheme.StyleSecondaryButton(_connectButton);
            _connectButton.Click +=
                (_, __) => ConnectToSimulator();

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _simPill.SetNeutral("MSFS hors ligne");
            _voicePill.SetNeutral("Voix à configurer");
            _multiplayerPill.SetNeutral("Solo");

            actions.Controls.Add(_simPill);
            actions.Controls.Add(_voicePill);
            actions.Controls.Add(_multiplayerPill);
            actions.Controls.Add(_connectButton);
            actions.Controls.Add(_settingsButton);

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(actions);

            header.Resize += (_, __) =>
            {
                actions.Location = new Point(
                    header.ClientSize.Width -
                    actions.PreferredSize.Width -
                    26,
                    19);
            };

            return header;
        }

        private Control BuildWorkspace()
        {
            var workspace = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(26, 8, 14, 20),
                ColumnCount = 2,
                RowCount = 2
            };

            workspace.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 40f));

            workspace.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 60f));

            workspace.RowStyles.Add(
                new RowStyle(SizeType.Percent, 66f));

            workspace.RowStyles.Add(
                new RowStyle(SizeType.Percent, 34f));

            workspace.Controls.Add(BuildRadioCard(), 0, 0);
            workspace.Controls.Add(BuildConversationCard(), 1, 0);
            workspace.Controls.Add(BuildFlightCard(), 0, 1);
            workspace.Controls.Add(BuildTrafficCard(), 1, 1);

            return workspace;
        }

        private Control BuildRadioCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.Transparent
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(
                CreateSectionHeader("RADIO ACTIVE"),
                0,
                0);

            _stationValue.AutoSize = true;
            _stationValue.Font =
                OhControlTheme.Font(20f, FontStyle.Bold);

            _stationValue.ForeColor =
                OhControlTheme.Radio;

            _stationValue.Margin =
                new Padding(0, 8, 0, 2);

            layout.Controls.Add(_stationValue, 0, 1);

            _comValue.AutoSize = true;
            _comValue.Font = OhControlTheme.Font(8.7f);
            _comValue.ForeColor = OhControlTheme.TextSecondary;
            _comValue.MaximumSize = new Size(430, 0);
            layout.Controls.Add(_comValue, 0, 2);

            var pttPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.SurfaceRaised,
                Padding = new Padding(18),
                Margin = new Padding(0, 0, 0, 12)
            };

            _voiceStatusValue.Dock = DockStyle.Top;
            _voiceStatusValue.AutoSize = true;
            _voiceStatusValue.Font =
                OhControlTheme.Font(13f, FontStyle.Bold);

            _voiceStatusValue.ForeColor =
                OhControlTheme.TextPrimary;

            _voiceStatusValue.MaximumSize =
                new Size(400, 0);

            var pttCaption = new Label
            {
                Text = "PTT",
                AutoSize = true,
                Font = OhControlTheme.Font(8f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextSecondary,
                Location = new Point(18, 64)
            };

            _pttValue.AutoSize = true;
            _pttValue.Font =
                OhControlTheme.Font(11f, FontStyle.Bold);

            _pttValue.ForeColor =
                OhControlTheme.Accent;

            _pttValue.Location = new Point(18, 84);

            pttPanel.Controls.Add(_voiceStatusValue);
            pttPanel.Controls.Add(pttCaption);
            pttPanel.Controls.Add(_pttValue);
            layout.Controls.Add(pttPanel, 0, 4);

            var testLine = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };

            var testLabel = new Label
            {
                Text = "Test",
                AutoSize = true,
                ForeColor = OhControlTheme.TextSecondary,
                Margin = new Padding(0, 7, 10, 0)
            };

            _testStation.DropDownStyle = ComboBoxStyle.DropDownList;
            _testStation.Width = 185;
            OhControlTheme.StyleComboBox(_testStation);

            _testStation.Items.Add("Tour · 118.100");
            _testStation.Items.Add("Sol · 121.705");
            _testStation.Items.Add("ATIS · 128.130");
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

            var testOutput = new Button
            {
                Text = "Tester sortie",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(
                testOutput);

            testOutput.Click +=
                async (_, __) =>
                    await _voice.TestRadioOutputAsync();

            var testVoice = new Button
            {
                Text = "Tester voix ATC",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(
                testVoice);

            testVoice.Click +=
                async (_, __) =>
                    await _voice.TestControllerVoiceAsync();

            testLine.Controls.Add(testLabel);
            testLine.Controls.Add(_testStation);
            testLine.Controls.Add(testOutput);
            testLine.Controls.Add(testVoice);
            layout.Controls.Add(testLine, 0, 5);

            _voiceConfigValue.AutoSize = true;
            _voiceConfigValue.ForeColor = OhControlTheme.TextSecondary;
            _voiceConfigValue.Font = OhControlTheme.Font(8.5f);
            layout.Controls.Add(_voiceConfigValue, 0, 6);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildFlightCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = Color.Transparent
            };

            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            layout.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 50f));

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34f));

            var header = CreateSectionHeader("VOL");
            layout.Controls.Add(header, 0, 0);
            layout.SetColumnSpan(header, 2);

            layout.Controls.Add(
                CreateMetric("PHASE", _localPhaseValue),
                0,
                1);

            layout.Controls.Add(
                CreateMetric("ALTITUDE", _altitudeValue),
                1,
                1);

            layout.Controls.Add(
                CreateMetric("VITESSE", _speedValue),
                0,
                2);

            layout.Controls.Add(
                CreateMetric("CAP", _headingValue),
                1,
                2);

            layout.Controls.Add(
                CreateMetric("MÉTÉO", _weatherValue),
                0,
                3);

            var detail = CreateMetric("POSITION", _positionValue);
            detail.Controls.Add(_groundValue);
            _groundValue.Dock = DockStyle.Bottom;
            _groundValue.AutoSize = true;
            _groundValue.ForeColor = OhControlTheme.TextSecondary;
            _groundValue.Font = OhControlTheme.Font(8.2f);

            layout.Controls.Add(detail, 1, 3);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildConversationCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Color.Transparent
            };

            layout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            layout.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            layout.RowStyles.Add(
                new RowStyle(SizeType.Percent, 100f));

            layout.Controls.Add(
                CreateSectionHeader("ÉCHANGES RADIO"),
                0,
                0);

            var hint = new Label
            {
                Text =
                    "Historique de la session · molette pour remonter les échanges",
                AutoSize = true,
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.4f),
                Margin = new Padding(0, 2, 0, 8)
            };

            layout.Controls.Add(
                hint,
                0,
                1);

            _conversationLog.Dock = DockStyle.Fill;
            _conversationLog.FlowDirection =
                FlowDirection.TopDown;

            _conversationLog.WrapContents = false;
            _conversationLog.AutoScroll = true;
            _conversationLog.BackColor =
                OhControlTheme.Background;

            _conversationLog.Padding =
                new Padding(0, 0, 4, 4);

            _conversationLog.Margin =
                new Padding(0);

            _conversationLog.TabStop = true;

            _conversationLog.ClientSizeChanged +=
                (_, __) =>
                    ResizeConversationEntries();

            layout.Controls.Add(
                _conversationLog,
                0,
                2);

            card.Controls.Add(layout);
            return card;
        }

        private void AppendConversationEntry(
            string title,
            string text,
            Color accent)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            bool stayAtBottom =
                IsConversationNearBottom();

            var entry =
                CreateConversationEntry(
                    title,
                    text.Trim(),
                    accent);

            _conversationEntries.Add(entry);
            _conversationLog.Controls.Add(entry);

            ResizeConversationEntry(entry);

            if (stayAtBottom ||
                _conversationEntries.Count <= 1)
            {
                _conversationLog.ScrollControlIntoView(
                    entry);
            }
        }

        private Panel CreateConversationEntry(
            string title,
            string text,
            Color accent)
        {
            var panel = new Panel
            {
                AutoSize = true,
                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,
                BackColor =
                    OhControlTheme.SurfaceRaised,
                Padding =
                    new Padding(14, 11, 14, 12),
                Margin =
                    new Padding(0, 0, 0, 8)
            };

            var marker = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = accent
            };

            var body = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode =
                    AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.Transparent,
                Padding = new Padding(9, 0, 0, 0)
            };

            body.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100f));

            body.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            body.RowStyles.Add(
                new RowStyle(SizeType.AutoSize));

            var caption = new Label
            {
                Text = title,
                AutoSize = true,
                Font =
                    OhControlTheme.Font(
                        7.8f,
                        FontStyle.Bold),
                ForeColor = accent,
                Margin = new Padding(0, 0, 0, 5)
            };

            var value = new Label
            {
                Text = text,
                AutoSize = true,
                Font =
                    OhControlTheme.Font(10.4f),
                ForeColor =
                    OhControlTheme.TextPrimary,
                Margin = new Padding(0)
            };

            body.Controls.Add(
                caption,
                0,
                0);

            body.Controls.Add(
                value,
                0,
                1);

            panel.Tag = value;
            panel.Controls.Add(body);
            panel.Controls.Add(marker);

            return panel;
        }

        private void ResizeConversationEntries()
        {
            foreach (Control entry in
                     _conversationEntries.ToArray())
            {
                ResizeConversationEntry(entry);
            }
        }

        private void ResizeConversationEntry(
            Control entry)
        {
            if (entry == null ||
                entry.IsDisposed)
            {
                return;
            }

            int scrollbarAllowance =
                _conversationLog.VerticalScroll.Visible
                    ? SystemInformation
                        .VerticalScrollBarWidth
                    : 0;

            int width =
                Math.Max(
                    180,
                    _conversationLog.ClientSize.Width -
                    _conversationLog.Padding.Horizontal -
                    scrollbarAllowance -
                    4);

            entry.Width = width;

            var value =
                entry.Tag as Label;

            if (value != null)
            {
                value.MaximumSize =
                    new Size(
                        Math.Max(
                            120,
                            width - 48),
                        0);
            }
        }

        private bool IsConversationNearBottom()
        {
            if (!_conversationLog.AutoScroll)
            {
                return true;
            }

            int visibleHeight =
                _conversationLog.ClientSize.Height;

            int contentHeight =
                _conversationLog.DisplayRectangle.Height;

            int scrollTop =
                Math.Abs(
                    _conversationLog.AutoScrollPosition.Y);

            return contentHeight <= visibleHeight ||
                   contentHeight -
                   (scrollTop + visibleHeight) <= 48;
        }

        private Control BuildTrafficCard()
        {
            var card = new AviationCard { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            layout.Controls.Add(
                CreateSectionHeader("TRAFIC & SESSION"),
                0,
                0);

            _multiplayerStatusValue.AutoSize = true;
            _multiplayerStatusValue.ForeColor =
                OhControlTheme.TextSecondary;

            _multiplayerStatusValue.Margin =
                new Padding(0, 8, 0, 10);

            layout.Controls.Add(
                _multiplayerStatusValue,
                0,
                1);

            var trafficBox = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.SurfaceRaised,
                Padding = new Padding(14),
                Margin = new Padding(0, 0, 0, 10)
            };

            _remotePlayersValue.Dock = DockStyle.Fill;
            _remotePlayersValue.AutoSize = false;
            _remotePlayersValue.ForeColor =
                OhControlTheme.TextPrimary;

            _remotePlayersValue.Font =
                OhControlTheme.Font(10f);

            trafficBox.Controls.Add(_remotePlayersValue);
            layout.Controls.Add(trafficBox, 0, 2);

            var note = new Label
            {
                Text =
                    "L’ATC tient compte du trafic partagé lorsqu’une session à deux est active.",
                AutoSize = true,
                MaximumSize = new Size(580, 0),
                ForeColor = OhControlTheme.TextSecondary,
                Font = OhControlTheme.Font(8.5f)
            };

            layout.Controls.Add(note, 0, 3);
            card.Controls.Add(layout);
            return card;
        }

        private static Label CreateSectionHeader(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = OhControlTheme.Font(8f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, 4)
            };
        }

        private static Panel CreateMetric(
            string title,
            Label value)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.SurfaceRaised,
                Padding = new Padding(12),
                Margin = new Padding(0, 6, 8, 2)
            };

            var caption = new Label
            {
                Text = title,
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = OhControlTheme.Font(7.8f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextSecondary
            };

            value.Dock = DockStyle.Fill;
            value.AutoSize = false;
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.Font = OhControlTheme.Font(11f, FontStyle.Bold);
            value.ForeColor = OhControlTheme.TextPrimary;

            panel.Controls.Add(value);
            panel.Controls.Add(caption);
            return panel;
        }

        private static Panel CreateRadioMessage(
            string title,
            Label value,
            Color accent)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OhControlTheme.SurfaceRaised,
                Padding = new Padding(14),
                Margin = new Padding(0, 7, 0, 0)
            };

            var marker = new Panel
            {
                Dock = DockStyle.Left,
                Width = 3,
                BackColor = accent,
                Margin = new Padding(0)
            };

            var caption = new Label
            {
                Text = title,
                AutoSize = true,
                Dock = DockStyle.Top,
                Font = OhControlTheme.Font(7.8f, FontStyle.Bold),
                ForeColor = accent,
                Padding = new Padding(9, 0, 0, 0)
            };

            value.Dock = DockStyle.Fill;
            value.AutoSize = false;
            value.Font = OhControlTheme.Font(10f);
            value.ForeColor = OhControlTheme.TextPrimary;
            value.Padding = new Padding(9, 7, 2, 0);

            panel.Controls.Add(value);
            panel.Controls.Add(caption);
            panel.Controls.Add(marker);
            return panel;
        }

        private void InitializeLabels()
        {
            _stationValue.Text = "BRON TOUR · 118.100";
            _comValue.Text = "COM1 · mode test";
            _voiceStatusValue.Text = "Prêt à émettre";
            _pttValue.Text = _voice.PttDescription;

            _positionValue.Text = "—";
            _altitudeValue.Text = "—";
            _headingValue.Text = "—";
            _speedValue.Text = "—";
            _groundValue.Text = "—";
            _weatherValue.Text = "—";
            _localPhaseValue.Text = "—";

            _pilotTextValue.Text = "Aucune transmission";
            _remoteRadioValue.Text = "Aucun autre pilote";
            _controllerTextValue.Text = "En attente d’un appel radio";
            _feedbackValue.Text = "—";

            AppendConversationEntry(
                "OHCONTROL",
                "En attente du premier échange radio.",
                OhControlTheme.TextSecondary);

            _multiplayerStatusValue.Text =
                "Session multijoueur désactivée";

            _remotePlayersValue.Text =
                "Aucun autre appareil connecté";
        }

        private void WireEvents()
        {
            _simConnect.Connected += OnConnected;
            _simConnect.Disconnected += OnDisconnected;
            _simConnect.TelemetryReceived += OnTelemetryReceived;
            _simConnect.Error += OnSimConnectError;

            _voice.StatusChanged += value =>
                Ui(() => UpdateVoiceStatus(value));

            _voice.StationChanged += value =>
                Ui(() => UpdateStation(value));

            _voice.PilotTextReceived += value =>
                Ui(() =>
                {
                    _pilotTextValue.Text = value;
                    AppendConversationEntry(
                        "VOUS",
                        value,
                        OhControlTheme.Accent);
                });

            _voice.RemoteRadioTextReceived += value =>
                Ui(() =>
                {
                    _remoteRadioValue.Text = value;
                    AppendConversationEntry(
                        "AUTRE PILOTE",
                        value,
                        OhControlTheme.TextSecondary);
                });

            _voice.ControllerTextGenerated += value =>
                Ui(() =>
                {
                    _controllerTextValue.Text = value;
                    AppendConversationEntry(
                        "CONTRÔLEUR",
                        value,
                        OhControlTheme.Radio);
                });

            _voice.FeedbackGenerated += value =>
                Ui(() =>
                {
                    _feedbackValue.Text = value;
                    AppendConversationEntry(
                        "RETOUR PÉDAGOGIQUE",
                        value,
                        OhControlTheme.Warning);
                });

            _voice.MultiplayerStatusChanged += value =>
                Ui(() => UpdateMultiplayerStatus(value));

            _voice.RemotePlayersChanged += players =>
                Ui(() => UpdateRemotePlayers(players));

            _voice.LocalPhaseChanged += value =>
                Ui(() => _localPhaseValue.Text = value);
        }

        private void UpdateStation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                _stationValue.Text = "AUCUNE STATION";
                return;
            }

            _stationValue.Text =
                value
                    .Replace("BRON Information", "BRON INFORMATION")
                    .Replace("BRON Sol", "BRON SOL")
                    .Replace("BRON Tour", "BRON TOUR");

            _comValue.Text =
                _simConnect.IsConnected
                    ? "Sélection automatique depuis COM1"
                    : "Mode test hors simulateur";
        }

        private void UpdateVoiceStatus(string value)
        {
            _voiceStatusValue.Text =
                string.IsNullOrWhiteSpace(value)
                    ? "Prêt"
                    : value;

            string lower =
                (value ?? "").ToLowerInvariant();

            if (lower.Contains("double transmission") ||
                lower.Contains("bloquée") ||
                lower.Contains("erreur"))
            {
                _voicePill.SetDanger("Radio occupée");
                _voiceStatusValue.ForeColor =
                    OhControlTheme.Danger;
            }
            else if (lower.Contains("transmission") &&
                     !lower.Contains("traitée"))
            {
                _voicePill.SetRadio("TX");
                _voiceStatusValue.ForeColor =
                    OhControlTheme.Radio;
            }
            else if (lower.Contains("reconnaissance") ||
                     lower.Contains("réponse"))
            {
                _voicePill.SetWarning("Traitement");
                _voiceStatusValue.ForeColor =
                    OhControlTheme.Warning;
            }
            else
            {
                _voicePill.SetGood("Radio prête");
                _voiceStatusValue.ForeColor =
                    OhControlTheme.TextPrimary;
            }
        }

        private void UpdateMultiplayerStatus(string value)
        {
            _multiplayerStatusValue.Text =
                string.IsNullOrWhiteSpace(value)
                    ? "Session multijoueur désactivée"
                    : value;

            string lower =
                (value ?? "").ToLowerInvariant();

            if (lower.Contains("connected") &&
                !lower.Contains("disconnected"))
            {
                _multiplayerPill.SetGood("À deux");
            }
            else if (_settings.MultiplayerEnabled)
            {
                _multiplayerPill.SetWarning("Session");
            }
            else
            {
                _multiplayerPill.SetNeutral("Solo");
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
                UpdateVoiceStatus(
                    "Réglages : " + ex.Message);
            }
        }

        private void RefreshSettingsUi()
        {
            if (_settings.IsElevenLabsConfigured)
            {
                _voiceConfigValue.Text =
                    "Voix ATC configurée · " +
                    _settings.PilotCallsign;

                _voicePill.SetGood("Radio prête");
            }
            else
            {
                _voiceConfigValue.Text =
                    "Ajoute la clé ElevenLabs dans Réglages pour activer la voix.";

                _voicePill.SetWarning("Voix à configurer");
            }

            _pttValue.Text = _voice.PttDescription;

            if (!_settings.MultiplayerEnabled)
            {
                _multiplayerPill.SetNeutral("Solo");
                _multiplayerStatusValue.Text =
                    "Session multijoueur désactivée";
            }
            else if (!_settings.IsMultiplayerConfigured)
            {
                _multiplayerPill.SetWarning("Session");
                _multiplayerStatusValue.Text =
                    "Multijoueur activé mais non configuré";
            }
            else
            {
                _multiplayerPill.SetGood("À deux");
                _multiplayerStatusValue.Text =
                    "Salle · " +
                    _settings.MultiplayerRoomCode;
            }
        }

        private void UpdateRemotePlayers(
            IReadOnlyList<MultiplayerPlayerState> players)
        {
            if (players == null ||
                players.Count == 0)
            {
                _remotePlayersValue.Text =
                    "Aucun autre appareil connecté";
                return;
            }

            _remotePlayersValue.Text =
                string.Join(
                    Environment.NewLine +
                    Environment.NewLine,
                    players.Select(
                        player =>
                            (player.IsTransmitting
                                ? "● TX   "
                                : "○       ") +
                            player.Callsign +
                            "   " +
                            player.AircraftType +
                            Environment.NewLine +
                            "   " +
                            (string.IsNullOrWhiteSpace(
                                player.CircuitPhase)
                                ? "position inconnue"
                                : player.CircuitPhase) +
                            "   ·   COM1 " +
                            player.Com1ActiveMhz.ToString("F3")));
        }

        private void ConnectToSimulator()
        {
            _connectButton.Enabled = false;
            _connectButton.Text = "Connexion…";
            _simPill.SetWarning("Connexion MSFS");
            _simConnect.Connect(Handle);
        }

        private void OnConnected()
        {
            Ui(() =>
            {
                _simPill.SetGood("MSFS connecté");
                _connectButton.Text = "MSFS connecté";
                _connectButton.Enabled = false;
                _comValue.Text =
                    "COM1 pilotée par l’avion";
            });

            _voice.SetSimulatorConnected(true);
        }

        private void OnDisconnected()
        {
            Ui(() =>
            {
                _simPill.SetNeutral("MSFS hors ligne");
                _connectButton.Text = "Connecter MSFS";
                _connectButton.Enabled = true;
                _comValue.Text =
                    "Mode test hors simulateur";
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
                    telemetry.LatitudeDeg.ToString("F4") +
                    " / " +
                    telemetry.LongitudeDeg.ToString("F4");

                _altitudeValue.Text =
                    telemetry.AltitudeFt.ToString("F0") +
                    " ft";

                _headingValue.Text =
                    telemetry.HeadingMagneticDeg.ToString("F0") +
                    "°";

                _speedValue.Text =
                    telemetry.IndicatedAirspeedKt.ToString("F0") +
                    " kt IAS";

                _groundValue.Text =
                    telemetry.IsOnGround
                        ? "Au sol · " +
                          telemetry.GroundSpeedKt.ToString("F0") +
                          " kt"
                        : "En vol · " +
                          telemetry.GroundSpeedKt.ToString("F0") +
                          " kt sol";

                string station =
                    string.IsNullOrWhiteSpace(
                        telemetry.Com1ActiveIdent)
                        ? ""
                        : " · " +
                          telemetry.Com1ActiveIdent;

                _comValue.Text =
                    "COM1 " +
                    telemetry.Com1ActiveMhz.ToString("F3") +
                    station +
                    " · " +
                    (telemetry.Com1Receive ? "RX" : "RX OFF") +
                    " / " +
                    (telemetry.Com1Transmit ? "TX" : "TX OFF");

                _weatherValue.Text =
                    telemetry.WindDirectionTrueDeg.ToString("F0") +
                    "° / " +
                    telemetry.WindSpeedKt.ToString("F0") +
                    " kt   QNH " +
                    telemetry.SeaLevelPressureMb.ToString("F0");
            });
        }

        private void OnSimConnectError(string message)
        {
            Ui(() =>
            {
                _simPill.SetNeutral("MSFS hors ligne");
                _connectButton.Text = "Connecter MSFS";
                _connectButton.Enabled = true;
                _comValue.Text =
                    "Mode test disponible · MSFS non connecté";
            });

            _voice.SetSimulatorConnected(false);
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
