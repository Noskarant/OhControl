using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using OhControl.Configuration;

namespace OhControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveSimConnectAssembly;

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

        private static Assembly ResolveSimConnectAssembly(
            object sender,
            ResolveEventArgs args)
        {
            AssemblyName requested;

            try
            {
                requested = new AssemblyName(args.Name);
            }
            catch
            {
                return null;
            }

            if (!string.Equals(
                requested.Name,
                "Microsoft.FlightSimulator.SimConnect",
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            foreach (string root in GetSdkCandidates())
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                string dll = Path.Combine(
                    root,
                    "SimConnect SDK",
                    "lib",
                    "managed",
                    "Microsoft.FlightSimulator.SimConnect.dll");

                if (!File.Exists(dll))
                {
                    continue;
                }

                try
                {
                    return Assembly.LoadFrom(dll);
                }
                catch
                {
                    // Try the next candidate.
                }
            }

            return null;
        }

        private static string[] GetSdkCandidates()
        {
            string systemDrive =
                Environment.GetEnvironmentVariable("SystemDrive")
                ?? "C:";

            return new[]
            {
                Environment.GetEnvironmentVariable("MSFS2024_SDK"),
                Path.Combine(
                    systemDrive + Path.DirectorySeparatorChar,
                    "MSFS 2024 SDK"),
                @"C:\MSFS 2024 SDK",
                @"D:\MSFS 2024 SDK"
            };
        }
    }
}
