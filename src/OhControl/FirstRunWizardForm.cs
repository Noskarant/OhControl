using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OhControl.Audio;
using OhControl.Configuration;
using OhControl.Setup;
using OhControl.Ui;

namespace OhControl
{
    public sealed class FirstRunWizardForm : Form
    {
        private readonly OhControlSettings _settings;

        private readonly Panel _pageHost = new Panel();
        private readonly Label _stepLabel = new Label();
        private readonly Button _backButton = new Button();
        private readonly Button _nextButton = new Button();

        private readonly TextBox _callsign = new TextBox();
        private readonly TextBox _displayName = new TextBox();
        private readonly TextBox _aircraftType = new TextBox();

        private readonly TextBox _elevenKey = new TextBox();
        private readonly TextBox _elevenVoice = new TextBox();

        private readonly ComboBox _inputDevice = new ComboBox();
        private readonly ComboBox _outputDevice = new ComboBox();
        private readonly Label _keyboardPtt = new Label();
        private readonly Label _joystickPtt = new Label();

        private readonly StatusPill _simConnectPill = new StatusPill();
        private readonly Label _simConnectDetail = new Label();

        private string _capturedKeyboardKey;
        private int _capturedJoystickDeviceId;
        private int _capturedJoystickButtonIndex;
        private CancellationTokenSource _joystickCapture;

        private int _step;

        public FirstRunWizardForm(OhControlSettings settings)
        {
            _settings = settings;
            _capturedKeyboardKey = settings.PttKeyboardKey;
            _capturedJoystickDeviceId = settings.PttJoystickDeviceId;
            _capturedJoystickButtonIndex = settings.PttJoystickButtonIndex;

            Text = "Bienvenue dans OhControl";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(820, 620);
            Size = new Size(900, 680);
            BackColor = OhControlTheme.Background;
            ForeColor = OhControlTheme.TextPrimary;
            Font = OhControlTheme.Font(9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildUi();
            ShowStep(0);
        }

        private void BuildUi()
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 92,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(28, 18, 28, 10)
            };

            header.Controls.Add(new Label
            {
                Text = "Configuration initiale",
                AutoSize = true,
                Font = OhControlTheme.Font(20f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary,
                Location = new Point(28, 15)
            });

            _stepLabel.AutoSize = true;
            _stepLabel.Font = OhControlTheme.Font(9f);
            _stepLabel.ForeColor = OhControlTheme.TextSecondary;
            _stepLabel.Location = new Point(30, 51);
            header.Controls.Add(_stepLabel);

            _pageHost.Dock = DockStyle.Fill;
            _pageHost.BackColor = OhControlTheme.Background;
            _pageHost.Padding = new Padding(28, 12, 28, 16);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 70,
                BackColor = OhControlTheme.Background,
                Padding = new Padding(28, 12, 28, 14)
            };

            _backButton.Text = "Retour";
            _backButton.AutoSize = true;
            OhControlTheme.StyleSecondaryButton(_backButton);
            _backButton.Click += (_, __) =>
            {
                if (_step > 0)
                {
                    ShowStep(_step - 1);
                }
            };

            _nextButton.Text = "Continuer";
            _nextButton.AutoSize = true;
            OhControlTheme.StylePrimaryButton(_nextButton);
            _nextButton.Click += (_, __) => Next();

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            actions.Controls.Add(_nextButton);
            actions.Controls.Add(_backButton);
            footer.Controls.Add(actions);

            Controls.Add(_pageHost);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void ShowStep(int step)
        {
            _step = Math.Max(0, Math.Min(3, step));
            _pageHost.Controls.Clear();

            _stepLabel.Text =
                "Étape " + (_step + 1) + " sur 4";

            _backButton.Visible = _step > 0;
            _nextButton.Text =
                _step == 3
                    ? "Ouvrir OhControl"
                    : "Continuer";

            Control page;

            switch (_step)
            {
                case 0:
                    page = BuildPrerequisitePage();
                    break;

                case 1:
                    page = BuildPilotPage();
                    break;

                case 2:
                    page = BuildVoicePage();
                    break;

                default:
                    page = BuildAudioPage();
                    break;
            }

            page.Dock = DockStyle.Fill;
            _pageHost.Controls.Add(page);
        }

        private Control BuildPrerequisitePage()
        {
            var card = NewCard();

            var title = NewTitle("Connexion à Microsoft Flight Simulator");
            var intro = NewBody(
                "OhControl se connecte automatiquement à MSFS 2024 avec SimConnect. " +
                "Tu n’as rien à renseigner : on vérifie simplement que le composant Microsoft est présent.");

            _simConnectDetail.AutoSize = true;
            _simConnectDetail.MaximumSize = new Size(720, 0);
            _simConnectDetail.ForeColor = OhControlTheme.TextSecondary;
            _simConnectDetail.Font = OhControlTheme.Font(9f);

            var retry = new Button
            {
                Text = "Vérifier à nouveau",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(retry);
            retry.Click += (_, __) => RefreshPrerequisites();

            var line = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 18, 0, 0)
            };

            line.Controls.Add(_simConnectPill);
            line.Controls.Add(retry);

            var note = NewBody(
                "Si le SDK manque, c’est la seule étape Microsoft à faire une fois. " +
                "Après ça, OhControl détectera et reconnectera MSFS automatiquement à chaque lancement.");

            var layout = Stack(
                title,
                intro,
                line,
                _simConnectDetail,
                note);

            card.Controls.Add(layout);
            RefreshPrerequisites();
            return card;
        }

        private Control BuildPilotPage()
        {
            var card = NewCard();

            _callsign.Text = _settings.PilotCallsign;
            _displayName.Text =
                string.IsNullOrWhiteSpace(_settings.PlayerDisplayName)
                    ? Environment.UserName
                    : _settings.PlayerDisplayName;

            _aircraftType.Text =
                string.IsNullOrWhiteSpace(_settings.AircraftType)
                    ? "DR400"
                    : _settings.AircraftType;

            foreach (TextBox box in new[]
            {
                _callsign,
                _displayName,
                _aircraftType
            })
            {
                OhControlTheme.StyleTextBox(box);
                box.Width = 360;
            }

            var layout = Stack(
                NewTitle("Ton profil pilote"),
                NewBody(
                    "Ces informations servent à la radio. Elles restent enregistrées uniquement sur ce PC."),
                Field("Indicatif", "Ex. F-GABC", _callsign),
                Field("Prénom", "Affiché dans OhControl", _displayName),
                Field("Avion", "Par défaut : DR400", _aircraftType));

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildVoicePage()
        {
            var card = NewCard();

            _elevenKey.Text = _settings.ElevenLabsApiKey;
            _elevenVoice.Text = _settings.ElevenLabsVoiceId;
            _elevenKey.UseSystemPasswordChar = true;

            OhControlTheme.StyleTextBox(_elevenKey);
            OhControlTheme.StyleTextBox(_elevenVoice);

            _elevenKey.Width = 500;
            _elevenVoice.Width = 500;

            var skip = NewBody(
                "Pour le test complet il faut une clé API et un Voice ID ElevenLabs. " +
                "Tu peux néanmoins continuer sans les renseigner et les ajouter plus tard dans Réglages.");

            var layout = Stack(
                NewTitle("Voix du contrôle"),
                NewBody(
                    "OhControl utilise ElevenLabs pour comprendre tes appels radio et générer la voix du contrôleur."),
                Field("Clé API ElevenLabs", "Elle ne sera jamais envoyée sur GitHub", _elevenKey),
                Field("Voice ID", "La voix française choisie pour l’ATC", _elevenVoice),
                skip);

            card.Controls.Add(layout);
            return card;
        }

        private Control BuildAudioPage()
        {
            var card = NewCard();

            PopulateAudioDevices();

            var keyboardRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _keyboardPtt.Text =
                string.IsNullOrWhiteSpace(_capturedKeyboardKey)
                    ? "Aucune"
                    : _capturedKeyboardKey;

            _keyboardPtt.AutoSize = true;
            _keyboardPtt.Font = OhControlTheme.Font(10f, FontStyle.Bold);
            _keyboardPtt.ForeColor = OhControlTheme.Accent;
            _keyboardPtt.Margin = new Padding(0, 9, 12, 0);

            var captureKeyboard = new Button
            {
                Text = "Choisir une touche",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(captureKeyboard);
            captureKeyboard.Click += (_, __) => CaptureKeyboard();
            keyboardRow.Controls.Add(_keyboardPtt);
            keyboardRow.Controls.Add(captureKeyboard);

            var joystickRow = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent
            };

            _joystickPtt.Text =
                _capturedJoystickDeviceId >= 0
                    ? "Joystick #" + _capturedJoystickDeviceId +
                      " · bouton " + (_capturedJoystickButtonIndex + 1)
                    : "Optionnel";

            _joystickPtt.AutoSize = true;
            _joystickPtt.Font = OhControlTheme.Font(10f, FontStyle.Bold);
            _joystickPtt.ForeColor = OhControlTheme.TextSecondary;
            _joystickPtt.Margin = new Padding(0, 9, 12, 0);

            var captureJoystick = new Button
            {
                Text = "Bouton du manche",
                AutoSize = true
            };

            OhControlTheme.StyleSecondaryButton(captureJoystick);
            captureJoystick.Click += async (_, __) =>
                await CaptureJoystickAsync();

            joystickRow.Controls.Add(_joystickPtt);
            joystickRow.Controls.Add(captureJoystick);

            var layout = Stack(
                NewTitle("Audio & Push-to-talk"),
                NewBody(
                    "Choisis ce que tu utilises réellement pour voler. Tu pourras tout modifier ensuite."),
                Field("Microphone", "Ta voix pilote", _inputDevice),
                Field("Sortie radio", "Casque ou haut-parleurs", _outputDevice),
                Field("PTT clavier", "Fonctionne même quand MSFS est au premier plan", keyboardRow),
                Field("PTT manche", "Optionnel", joystickRow),
                NewBody(
                    "C’est terminé. Supabase et le mode à deux restent volontairement désactivés pour l’instant."));

            card.Controls.Add(layout);
            return card;
        }

        private void RefreshPrerequisites()
        {
            PrerequisiteStatus status =
                PrerequisiteChecker.Check();

            if (status.SimConnectSdkFound)
            {
                _simConnectPill.SetGood("SimConnect prêt");
                _simConnectDetail.Text =
                    "Détecté : " +
                    status.SimConnectManagedDllPath;

                _nextButton.Enabled = true;
            }
            else
            {
                _simConnectPill.SetWarning("SDK à installer");
                _simConnectDetail.Text = status.Message;

                // Allow configuration to continue. The main app can still run
                // in offline radio test mode after the dependency is installed.
                _nextButton.Enabled = true;
            }
        }

        private void Next()
        {
            SaveCurrentPage();

            if (_step < 3)
            {
                ShowStep(_step + 1);
                return;
            }

            PrerequisiteStatus prerequisite =
                PrerequisiteChecker.Check();

            if (!prerequisite.SimConnectSdkFound)
            {
                MessageBox.Show(
                    this,
                    "Il reste une seule étape : installer le SDK MSFS 2024 depuis Developer Mode → Help → SDK Installer. " +
                    "Une fois installé, reviens ici et clique sur Ouvrir OhControl.",
                    "SimConnect requis",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                ShowStep(0);
                return;
            }

            _settings.FirstRunCompleted = true;
            _settings.MultiplayerEnabled = false;
            _settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        }

        private void SaveCurrentPage()
        {
            if (_step == 1)
            {
                _settings.PilotCallsign =
                    string.IsNullOrWhiteSpace(_callsign.Text)
                        ? "F-GABC"
                        : _callsign.Text.Trim();

                _settings.PlayerDisplayName =
                    _displayName.Text.Trim();

                _settings.AircraftType =
                    string.IsNullOrWhiteSpace(_aircraftType.Text)
                        ? "DR400"
                        : _aircraftType.Text.Trim();
            }
            else if (_step == 2)
            {
                _settings.ElevenLabsApiKey =
                    _elevenKey.Text.Trim();

                _settings.ElevenLabsVoiceId =
                    _elevenVoice.Text.Trim();
            }
            else if (_step == 3)
            {
                if (_inputDevice.SelectedItem is AudioDeviceInfo input)
                {
                    _settings.MicrophoneDeviceNumber =
                        input.DeviceNumber;
                }

                if (_outputDevice.SelectedItem is AudioDeviceInfo output)
                {
                    _settings.OutputDeviceNumber =
                        output.DeviceNumber;
                }

                _settings.PttKeyboardKey =
                    _capturedKeyboardKey;

                _settings.PttJoystickDeviceId =
                    _capturedJoystickDeviceId;

                _settings.PttJoystickButtonIndex =
                    _capturedJoystickButtonIndex;
            }
        }

        private void PopulateAudioDevices()
        {
            if (_inputDevice.Items.Count == 0)
            {
                _inputDevice.DropDownStyle = ComboBoxStyle.DropDownList;
                _outputDevice.DropDownStyle = ComboBoxStyle.DropDownList;
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

            _inputDevice.Width = 440;
            _outputDevice.Width = 440;
        }

        private void CaptureKeyboard()
        {
            _keyboardPtt.Text = "Appuie sur une touche…";
            _keyboardPtt.ForeColor = OhControlTheme.Warning;
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
            _joystickCapture?.Cancel();
            _joystickCapture?.Dispose();
            _joystickCapture = new CancellationTokenSource();

            _joystickPtt.Text = "Appuie sur un bouton…";
            _joystickPtt.ForeColor = OhControlTheme.Warning;

            try
            {
                Tuple<int, int> binding =
                    await JoystickPttMonitor.CaptureNextButtonAsync(
                        TimeSpan.FromSeconds(10),
                        _joystickCapture.Token);

                if (binding == null)
                {
                    _joystickPtt.Text = "Aucun bouton détecté";
                    _joystickPtt.ForeColor = OhControlTheme.Danger;
                    return;
                }

                _capturedJoystickDeviceId = binding.Item1;
                _capturedJoystickButtonIndex = binding.Item2;

                _joystickPtt.Text =
                    "Joystick #" + binding.Item1 +
                    " · bouton " + (binding.Item2 + 1);

                _joystickPtt.ForeColor = OhControlTheme.Accent;
            }
            catch (OperationCanceledException)
            {
                _joystickPtt.Text = "Capture annulée";
                _joystickPtt.ForeColor = OhControlTheme.TextSecondary;
            }
        }

        private static AviationCard NewCard()
        {
            return new AviationCard
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28)
            };
        }

        private static Label NewTitle(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = OhControlTheme.Font(17f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary,
                Margin = new Padding(0, 0, 0, 10)
            };
        }

        private static Label NewBody(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(720, 0),
                Font = OhControlTheme.Font(9.2f),
                ForeColor = OhControlTheme.TextSecondary,
                Margin = new Padding(0, 0, 0, 18)
            };
        }

        private static Control Field(
            string title,
            string hint,
            Control control)
        {
            var row = new TableLayoutPanel
            {
                AutoSize = true,
                ColumnCount = 2,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 6, 0, 10)
            };

            row.ColumnStyles.Add(
                new ColumnStyle(SizeType.Absolute, 185));

            row.ColumnStyles.Add(
                new ColumnStyle(SizeType.Percent, 100));

            var labelPanel = new Panel
            {
                Width = 175,
                Height = 52,
                BackColor = Color.Transparent
            };

            labelPanel.Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = OhControlTheme.Font(9f, FontStyle.Bold),
                ForeColor = OhControlTheme.TextPrimary,
                Location = new Point(0, 2)
            });

            labelPanel.Controls.Add(new Label
            {
                Text = hint,
                AutoSize = true,
                MaximumSize = new Size(170, 0),
                Font = OhControlTheme.Font(7.8f),
                ForeColor = OhControlTheme.TextSecondary,
                Location = new Point(0, 24)
            });

            control.Margin = new Padding(0, 7, 0, 0);

            row.Controls.Add(labelPanel, 0, 0);
            row.Controls.Add(control, 1, 0);

            return row;
        }

        private static FlowLayoutPanel Stack(params Control[] controls)
        {
            var stack = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            foreach (Control control in controls.Where(c => c != null))
            {
                stack.Controls.Add(control);
            }

            return stack;
        }
    }
}
