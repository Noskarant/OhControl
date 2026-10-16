using System;
using System.IO;

namespace OhControl.Setup
{
    public sealed class PrerequisiteStatus
    {
        public bool SimConnectSdkFound { get; set; }
        public string SimConnectManagedDllPath { get; set; }
        public string Message { get; set; }
    }

    public static class PrerequisiteChecker
    {
        public static PrerequisiteStatus Check()
        {
            foreach (string root in CandidateSdkRoots())
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

                if (File.Exists(dll))
                {
                    return new PrerequisiteStatus
                    {
                        SimConnectSdkFound = true,
                        SimConnectManagedDllPath = dll,
                        Message = "SimConnect MSFS 2024 détecté"
                    };
                }
            }

            return new PrerequisiteStatus
            {
                SimConnectSdkFound = false,
                Message =
                    "SDK MSFS 2024 / SimConnect non détecté. " +
                    "Dans MSFS 2024 : active Developer Mode, puis Help → SDK Installer. " +
                    "Relance ensuite la vérification."
            };
        }

        private static string[] CandidateSdkRoots()
        {
            string environment =
                Environment.GetEnvironmentVariable("MSFS2024_SDK");

            string systemDrive =
                Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";

            return new[]
            {
                environment,
                Path.Combine(systemDrive + Path.DirectorySeparatorChar, "MSFS 2024 SDK"),
                @"C:\MSFS 2024 SDK",
                @"D:\MSFS 2024 SDK"
            };
        }
    }
}
