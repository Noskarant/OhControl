using System;

namespace OhControl.Multiplayer
{
    public sealed class MultiplayerPlayerState
    {
        public string PlayerId { get; set; }
        public string DisplayName { get; set; }
        public string Callsign { get; set; }
        public string AircraftType { get; set; }

        public double LatitudeDeg { get; set; }
        public double LongitudeDeg { get; set; }
        public double AltitudeFt { get; set; }
        public double HeadingDeg { get; set; }
        public double IndicatedAirspeedKt { get; set; }
        public double GroundSpeedKt { get; set; }
        public bool IsOnGround { get; set; }

        public double Com1ActiveMhz { get; set; }
        public bool Com1Receive { get; set; }
        public bool Com1Transmit { get; set; }

        public string CircuitPhase { get; set; }
        public double DistanceToThresholdMeters { get; set; }
        public string ActiveRunway { get; set; }
        public string NearbyReportingPoint { get; set; }

        public bool IsTransmitting { get; set; }
        public string LastRadioTranscript { get; set; }

        public long SentAtUnixMs { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        public DateTime ReceivedAtUtc { get; set; }

        public override string ToString()
        {
            string phase = string.IsNullOrWhiteSpace(CircuitPhase)
                ? "unknown"
                : CircuitPhase;

            string report =
                string.IsNullOrWhiteSpace(NearbyReportingPoint)
                    ? ""
                    : " · " + NearbyReportingPoint;

            return Callsign + " · " + AircraftType + " · " + phase +
                   report +
                   " · COM1 " + Com1ActiveMhz.ToString("F3");
        }
    }
}
