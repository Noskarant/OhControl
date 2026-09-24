namespace OhControl.Lfly
{
    public static class LflyAirport
    {
        public const string Icao = "LFLY";
        public const string Name = "Lyon-Bron";

        // AIP France AD 2 LFLY, AIRAC 2026-09.
        public const double ArpLatitudeDeg = 45.7294444444;
        public const double ArpLongitudeDeg = 4.9388888889;
        public const int ReferenceElevationFt = 659;

        public const string Runway16 = "16";
        public const string Runway34 = "34";

        public const double Runway16TrueBearingDeg = 163.39;
        public const double Runway34TrueBearingDeg = 343.39;

        public const int PublishedCircuitAltitudeFtAmsl = 1500;
        public const int PublishedCircuitHeightFtAal = 800;

        public const string AeronauticalDataCycle = "AIRAC 2026-09";
        public const string AeronauticalDataSource = "AIP France AD 2 LFLY / VAC LFLY";
    }
}
