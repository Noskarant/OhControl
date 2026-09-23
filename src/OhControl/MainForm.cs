using System;
using System.Drawing;
using System.Windows.Forms;
using OhControl.Lfly;

namespace OhControl
{
    public sealed class MainForm : Form
    {
        private readonly SimConnectClient _simConnect = new SimConnectClient();

        private readonly Label _statusValue = new Label();
        private readonly Label _positionValue = new Label();
        private readonly Label _altitudeValue = new Label();
        private readonly Label _headingValue = new Label();
        private readonly Label _speedValue = new Label();
        private readonly Label _groundValue = new Label();
        private readonly Label _comValue = new Label();
        private readonly Button _connectButton = new Button();

        public MainForm()
        {
            Text = "OhControl — " + LflyAirport.Icao + " " + LflyAirport.Name;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 480);
            Size = new Size(820, 540);

            BuildUi();

            _simConnect.Connected += OnConnected;
            _simConnect.Disconnected += OnDisconnected;
            _simConnect.TelemetryReceived += OnTelemetryReceived;
            _simConnect.Error += OnSimConnectError;

            FormClosed += (_, __) => _simConnect.Dispose();
            Shown += (_, __) => ConnectToSimulator();
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
            var title = new Label
            {
                Text = "OhControl",
                Font = new Font(Font.FontFamily, 24, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 4)
            };

            var subtitle = new Label
            {
                Text = "VFR ATC prototype — " + LflyAirport.Icao + " " + LflyAirport.Name,
                AutoSize = true,
                ForeColor = Color.DimGray,
                Margin = new Padding(0, 0, 0, 24)
            };

            _statusValue.Text = "Disconnected";
            _positionValue.Text = "—";
            _altitudeValue.Text = "—";
            _headingValue.Text = "—";
            _speedValue.Text = "—";
            _groundValue.Text = "—";
            _comValue.Text = "—";

            _connectButton.Text = "Connect to MSFS 2024";
            _connectButton.AutoSize = true;
            _connectButton.Click += (_, __) => ConnectToSimulator();

            var telemetryTable = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 7,
                Dock = DockStyle.Top,
                Margin = new Padding(0, 16, 0, 16)
            };

            telemetryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
            telemetryTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddRow(telemetryTable, 0, "SimConnect", _statusValue);
            AddRow(telemetryTable, 1, "Position", _positionValue);
            AddRow(telemetryTable, 2, "Altitude", _altitudeValue);
            AddRow(telemetryTable, 3, "Heading", _headingValue);
            AddRow(telemetryTable, 4, "Speed", _speedValue);
            AddRow(telemetryTable, 5, "Aircraft state", _groundValue);
            AddRow(telemetryTable, 6, "COM1", _comValue);

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
            root.Controls.Add(_connectButton);
            root.Controls.Add(telemetryTable);

            Controls.Add(root);
        }

        private static void AddRow(
            TableLayoutPanel table,
            int row,
            string name,
            Label value)
        {
            var nameLabel = new Label
            {
                Text = name,
                AutoSize = true,
                Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Margin = new Padding(0, 8, 12, 8)
            };

            value.AutoSize = true;
            value.Margin = new Padding(0, 8, 0, 8);

            table.Controls.Add(nameLabel, 0, row);
            table.Controls.Add(value, 1, row);
        }

        private void ConnectToSimulator()
        {
            _connectButton.Enabled = false;
            _statusValue.Text = "Connecting…";
            _simConnect.Connect(Handle);

            if (!_simConnect.IsConnected)
            {
                // The actual Open notification arrives asynchronously via WndProc.
                _connectButton.Enabled = false;
            }
        }

        private void OnConnected()
        {
            _statusValue.Text = "Connected";
            _statusValue.ForeColor = Color.DarkGreen;
            _connectButton.Enabled = false;
        }

        private void OnDisconnected()
        {
            _statusValue.Text = "Disconnected";
            _statusValue.ForeColor = Color.DarkRed;
            _connectButton.Enabled = true;
        }

        private void OnTelemetryReceived(TelemetrySnapshot telemetry)
        {
            _positionValue.Text =
                telemetry.LatitudeDeg.ToString("F6") + ", " +
                telemetry.LongitudeDeg.ToString("F6");

            _altitudeValue.Text = telemetry.AltitudeFt.ToString("F0") + " ft";
            _headingValue.Text = telemetry.HeadingMagneticDeg.ToString("F0") + "°";
            _speedValue.Text =
                telemetry.IndicatedAirspeedKt.ToString("F0") + " kt IAS · " +
                telemetry.GroundSpeedKt.ToString("F0") + " kt GS";

            _groundValue.Text = telemetry.IsOnGround ? "On ground" : "Airborne";
            _comValue.Text =
                telemetry.Com1ActiveMhz.ToString("F3") + " MHz active · " +
                telemetry.Com1StandbyMhz.ToString("F3") + " MHz standby";
        }

        private void OnSimConnectError(string message)
        {
            _statusValue.Text = "Error";
            _statusValue.ForeColor = Color.DarkRed;
            _connectButton.Enabled = true;

            MessageBox.Show(
                this,
                message,
                "OhControl — SimConnect",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
