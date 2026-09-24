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
        public string Com1ActiveIdent { get; set; }
        public string Com1ActiveType { get; set; }
        public double Com1ActiveDistanceMeters { get; set; }
        public bool Com1Receive { get; set; }
        public bool Com1Transmit { get; set; }

        public double WindDirectionTrueDeg { get; set; }
        public double WindSpeedKt { get; set; }
        public double AmbientTemperatureC { get; set; }
        public double VisibilityMeters { get; set; }
        public double SeaLevelPressureMb { get; set; }
    }
}
