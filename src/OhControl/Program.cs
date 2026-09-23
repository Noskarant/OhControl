using System;
using System.Windows.Forms;
using OhControl.Configuration;

namespace OhControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            OhControlSettings settings =
                OhControlSettings.Load();

            if (!settings.FirstRunCompleted)
            {
                using (var wizard =
                    new FirstRunWizardForm(settings))
                {
                    if (wizard.ShowDialog() != DialogResult.OK)
                    {
                        return;
                    }
                }
            }

            Application.Run(new MainForm());
        }
    }
}
