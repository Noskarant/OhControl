using System;
using System.Security.Cryptography;
using System.Text;

namespace OhControl.Radio
{
    public sealed class AtisBroadcast
    {
        public string CacheKey { get; set; }
        public string Information { get; set; }
        public string Runway { get; set; }
        public int Qnh { get; set; }
        public string Text { get; set; }
    }

    public sealed class AtisService
    {
        public AtisBroadcast Build(TelemetrySnapshot telemetry)
        {
            telemetry = telemetry ?? CreateFallbackTelemetry();

            int windDirection =
                RoundToNearest10(
                    telemetry.WindDirectionTrueDeg);

            int windSpeed =
                Math.Max(
                    0,
                    (int)Math.Round(
                        telemetry.WindSpeedKt));

            int qnh =
                telemetry.SeaLevelPressureMb > 800
                    ? (int)Math.Round(
                        telemetry.SeaLevelPressureMb)
                    : 1013;

            int temperature =
                (int)Math.Round(
                    telemetry.AmbientTemperatureC);

            int visibilityKm =
                telemetry.VisibilityMeters > 0
                    ? Math.Max(
                        1,
                        (int)Math.Round(
                            telemetry.VisibilityMeters /
                            1000.0))
                    : 10;

            string runway =
                SelectRunway(
                    windDirection,
                    windSpeed);

            string information =
                GetSynchronizedInformationLetter(
                    DateTimeOffset.UtcNow);

            string visibilityText =
                visibilityKm >= 10
                    ? "visibilité supérieure à dix kilomètres"
                    : "visibilité " +
                      AviationFrenchNumbers.DigitsOnly(
                          visibilityKm) +
                      " kilomètres";

            string text =
                "Bron information " +
                information +
                ". " +
                "Piste en service " +
                AviationFrenchNumbers.Runway(
                    runway) +
                ". " +
                "Vent " +
                AviationFrenchNumbers.DigitsOnly(
                    windDirection,
                    3) +
                " degrés, " +
                AviationFrenchNumbers.DigitsOnly(
                    windSpeed) +
                " noeuds. " +
                visibilityText +
                ". " +
                "Température " +
                AviationFrenchNumbers.DigitsOnly(
                    temperature) +
                " degrés. " +
                "Q N H " +
                AviationFrenchNumbers.DigitsOnly(
                    qnh) +
                ". " +
                "Signalez information " +
                information +
                " reçue.";

            return new AtisBroadcast
            {
                Information = information,
                Runway = runway,
                Qnh = qnh,
                Text = text,
                CacheKey = ComputeHash(text)
            };
        }

        private static string GetSynchronizedInformationLetter(
            DateTimeOffset now)
        {
            // Multiplayer-oriented simplification:
            // every client derives the same ATIS letter from UTC time.
            // This keeps both PCs synchronized without a database row.
            long halfHourSlot =
                now.ToUnixTimeSeconds() / (30 * 60);

            int index =
                (int)(halfHourSlot %
                      InformationNames.Length);

            return InformationNames[index];
        }

        private static string SelectRunway(
            int windDirection,
            int windSpeed)
        {
            if (windSpeed < 3)
            {
                return "16";
            }

            double differenceTo16 =
                AngularDifference(
                    windDirection,
                    160);

            double differenceTo34 =
                AngularDifference(
                    windDirection,
                    340);

            return differenceTo16 <=
                   differenceTo34
                ? "16"
                : "34";
        }

        private static double AngularDifference(
            double a,
            double b)
        {
            double difference =
                Math.Abs(a - b) % 360.0;

            return difference > 180.0
                ? 360.0 - difference
                : difference;
        }

        private static int RoundToNearest10(
            double value)
        {
            if (value < 0 ||
                value > 360)
            {
                return 0;
            }

            int rounded =
                (int)(
                    Math.Round(
                        value / 10.0) *
                    10.0);

            return rounded == 360
                ? 0
                : rounded;
        }

        private static TelemetrySnapshot
            CreateFallbackTelemetry()
        {
            return new TelemetrySnapshot
            {
                WindDirectionTrueDeg = 160,
                WindSpeedKt = 5,
                AmbientTemperatureC = 20,
                VisibilityMeters = 10000,
                SeaLevelPressureMb = 1018
            };
        }

        private static string ComputeHash(
            string text)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes =
                    sha.ComputeHash(
                        Encoding.UTF8.GetBytes(
                            text));

                return BitConverter
                    .ToString(bytes)
                    .Replace("-", "")
                    .ToLowerInvariant();
            }
        }

        private static readonly string[]
            InformationNames =
        {
            "Alpha",
            "Bravo",
            "Charlie",
            "Delta",
            "Echo",
            "Foxtrot",
            "Golf",
            "Hotel",
            "India",
            "Juliett",
            "Kilo",
            "Lima",
            "Mike",
            "November",
            "Oscar",
            "Papa",
            "Quebec",
            "Romeo",
            "Sierra",
            "Tango",
            "Uniform",
            "Victor",
            "Whiskey",
            "X ray",
            "Yankee",
            "Zulu"
        };
    }
}
