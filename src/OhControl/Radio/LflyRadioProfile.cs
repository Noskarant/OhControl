using System.Collections.Generic;

namespace OhControl.Radio
{
    public static class LflyRadioProfile
    {
        // Source: AIP France AD 2 LFLY, AIRAC effective September 2026.
        // Keep these values versioned and re-check them when aeronautical data changes.
        public static readonly IReadOnlyList<RadioStation> Stations =
            new[]
            {
                new RadioStation(RadioStationKind.Atis, "BRON Information", 128.130),
                new RadioStation(RadioStationKind.Ground, "BRON Sol", 121.705),
                new RadioStation(RadioStationKind.Tower, "BRON Tour", 118.100)
            };

        public const string DataCycle = "AIRAC 2026-09";
        public const string DataSource = "AIP France AD 2 LFLY";
    }
}
