using System;

namespace OhControl.Audio
{
    // AM VHF receive noise profile: modest constant static on a clear
    // frequency, growing slightly as signal quality drops.
    public static class VhfNoiseProfile
    {
        public const int CarrierLeadMilliseconds = 105;
        public const int CarrierTailMilliseconds = 90;

        public static double SpeechHiss(double signalQuality)
        {
            double quality = ClampQuality(signalQuality);
            return 0.034 + (1.0 - quality) * 0.035;
        }

        public static double CrackleChance(double signalQuality)
        {
            double quality = ClampQuality(signalQuality);
            return 0.000008 + (1.0 - quality) * 0.00010;
        }

        private static double ClampQuality(double signalQuality)
        {
            if (double.IsNaN(signalQuality) ||
                double.IsInfinity(signalQuality))
                return 0.92;

            return Math.Max(0.25, Math.Min(1.0, signalQuality));
        }
    }
}
