namespace OhControl
{
    public sealed class TelemetrySnapshot
    {
        public double LatitudeDeg { get; set; }
        public double LongitudeDeg { get; set; }
        public double AltitudeFt { get; set; }
        public double HeadingMagneticDeg { get; set; }
        public double IndicatedAirspeedKt { get; set; }
        public double GroundSpeedKt { get; set; }
        public bool IsOnGround { get; set; }
        public double Com1ActiveMhz { get; set; }
        public double Com1StandbyMhz { get; set; }
    }
}
