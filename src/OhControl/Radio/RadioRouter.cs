using System;
using System.Linq;

namespace OhControl.Radio
{
    public sealed class RadioRouter
    {
        private const double FrequencyToleranceMhz = 0.006;

        public RadioStation Resolve(double activeFrequencyMhz)
        {
            if (activeFrequencyMhz <= 0)
            {
                return null;
            }

            return LflyRadioProfile.Stations.FirstOrDefault(
                station => Math.Abs(
                    station.FrequencyMhz - activeFrequencyMhz) <= FrequencyToleranceMhz);
        }

        public RadioStation Resolve(RadioStationKind kind)
        {
            return LflyRadioProfile.Stations.FirstOrDefault(
                station => station.Kind == kind);
        }
    }
}
