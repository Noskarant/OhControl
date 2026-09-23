using System;

namespace OhControl.Lfly
{
    public enum LflyFlightPhase
    {
        Unknown,
        Parked,
        Taxiing,
        Runway,
        InitialClimb,
        Crosswind,
        Downwind,
        Base,
        Final,
        Departed
    }

    public sealed class LflyFlightSituation
    {
        public LflyFlightPhase Phase { get; set; }
        public double DistanceToLandingThresholdMeters { get; set; }
        public double CrossTrackMeters { get; set; }
        public double AlongTrackMeters { get; set; }
    }

    public sealed class LflyFlightPhaseDetector
    {
        // AIP France AD 2 LFLY, AIRAC 2026-09.
        private const double RunwayTrueHeading16 = 163.39;
        private const double RunwayTrueHeading34 = 343.39;

        private static readonly GeoPoint Thr16 =
            new GeoPoint(45.7350000000, 4.9409388889);

        private static readonly GeoPoint Dthr16 =
            new GeoPoint(45.7324222222, 4.9420388889);

        private static readonly GeoPoint Thr34 =
            new GeoPoint(45.7193277778, 4.9476138889);

        private static readonly GeoPoint Dthr34 =
            new GeoPoint(45.7219027778, 4.9465166667);

        private static readonly GeoPoint Arp =
            new GeoPoint(45.7294444444, 4.9388888889);

        public LflyFlightSituation Detect(
            TelemetrySnapshot telemetry,
            string activeRunway)
        {
            if (telemetry == null)
            {
                return new LflyFlightSituation
                {
                    Phase = LflyFlightPhase.Unknown
                };
            }

            string runway = activeRunway == "34" ? "34" : "16";
            GeoPoint landingThreshold = runway == "34" ? Dthr34 : Dthr16;
            double runwayHeading =
                runway == "34" ? RunwayTrueHeading34 : RunwayTrueHeading16;

            LocalVector relative = ToLocalMeters(
                telemetry.LatitudeDeg,
                telemetry.LongitudeDeg,
                landingThreshold.Latitude,
                landingThreshold.Longitude);

            double headingRad = DegreesToRadians(runwayHeading);
            double ux = Math.Sin(headingRad);
            double uy = Math.Cos(headingRad);
            double rx = Math.Cos(headingRad);
            double ry = -Math.Sin(headingRad);

            double along = relative.East * ux + relative.North * uy;
            double cross = relative.East * rx + relative.North * ry;

            double distanceToThreshold =
                Math.Sqrt(relative.East * relative.East +
                          relative.North * relative.North);

            if (telemetry.IsOnGround)
            {
                LocalVector fromArp = ToLocalMeters(
                    telemetry.LatitudeDeg,
                    telemetry.LongitudeDeg,
                    Arp.Latitude,
                    Arp.Longitude);

                double airportDistance = Math.Sqrt(
                    fromArp.East * fromArp.East +
                    fromArp.North * fromArp.North);

                bool onRunway =
                    Math.Abs(cross) < 90 &&
                    along > -450 &&
                    along < 2250;

                return new LflyFlightSituation
                {
                    Phase = onRunway
                        ? LflyFlightPhase.Runway
                        : airportDistance < 2300 && telemetry.GroundSpeedKt > 3
                            ? LflyFlightPhase.Taxiing
                            : airportDistance < 2300
                                ? LflyFlightPhase.Parked
                                : LflyFlightPhase.Unknown,
                    DistanceToLandingThresholdMeters = distanceToThreshold,
                    CrossTrackMeters = cross,
                    AlongTrackMeters = along
                };
            }

            double headingDiff =
                AngularDifference(telemetry.HeadingMagneticDeg, runwayHeading);

            double oppositeDiff =
                AngularDifference(
                    telemetry.HeadingMagneticDeg,
                    NormalizeHeading(runwayHeading + 180));

            bool atCircuitAltitude =
                telemetry.AltitudeFt >= 1150 &&
                telemetry.AltitudeFt <= 1900;

            bool circuitSide =
                runway == "34" ? cross > 350 : cross < -350;

            LflyFlightPhase phase = LflyFlightPhase.Unknown;

            if (along >= -6000 &&
                along <= 350 &&
                Math.Abs(cross) < 420 &&
                headingDiff < 42)
            {
                phase = LflyFlightPhase.Final;
            }
            else if (along >= 250 &&
                     along <= 3200 &&
                     Math.Abs(cross) < 550 &&
                     headingDiff < 45)
            {
                phase = LflyFlightPhase.InitialClimb;
            }
            else if (atCircuitAltitude &&
                     circuitSide &&
                     Math.Abs(cross) >= 650 &&
                     Math.Abs(cross) <= 3000 &&
                     oppositeDiff < 48 &&
                     along >= -2200 &&
                     along <= 4000)
            {
                phase = LflyFlightPhase.Downwind;
            }
            else if (atCircuitAltitude &&
                     circuitSide &&
                     along < 200 &&
                     along > -3200 &&
                     Math.Abs(cross) >= 500 &&
                     Math.Abs(cross) <= 3000 &&
                     IsApproximatelyPerpendicular(
                         telemetry.HeadingMagneticDeg,
                         runwayHeading))
            {
                phase = LflyFlightPhase.Base;
            }
            else if (circuitSide &&
                     along > 400 &&
                     along < 3000 &&
                     IsApproximatelyPerpendicular(
                         telemetry.HeadingMagneticDeg,
                         runwayHeading))
            {
                phase = LflyFlightPhase.Crosswind;
            }
            else
            {
                LocalVector fromArp = ToLocalMeters(
                    telemetry.LatitudeDeg,
                    telemetry.LongitudeDeg,
                    Arp.Latitude,
                    Arp.Longitude);

                double distanceFromAirport = Math.Sqrt(
                    fromArp.East * fromArp.East +
                    fromArp.North * fromArp.North);

                if (distanceFromAirport > 6500)
                {
                    phase = LflyFlightPhase.Departed;
                }
            }

            return new LflyFlightSituation
            {
                Phase = phase,
                DistanceToLandingThresholdMeters = distanceToThreshold,
                CrossTrackMeters = cross,
                AlongTrackMeters = along
            };
        }

        private static bool IsApproximatelyPerpendicular(
            double heading,
            double runwayHeading)
        {
            double diff = AngularDifference(heading, runwayHeading);
            return diff >= 45 && diff <= 135;
        }

        private static double AngularDifference(double a, double b)
        {
            double diff = Math.Abs(
                NormalizeHeading(a) - NormalizeHeading(b));

            return diff > 180 ? 360 - diff : diff;
        }

        private static double NormalizeHeading(double value)
        {
            double normalized = value % 360;
            return normalized < 0 ? normalized + 360 : normalized;
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static LocalVector ToLocalMeters(
            double latitude,
            double longitude,
            double referenceLatitude,
            double referenceLongitude)
        {
            const double EarthRadiusMeters = 6371000.0;

            double lat = DegreesToRadians(latitude);
            double refLat = DegreesToRadians(referenceLatitude);
            double dLat = lat - refLat;
            double dLon =
                DegreesToRadians(longitude - referenceLongitude);

            return new LocalVector(
                dLon * Math.Cos((lat + refLat) * 0.5) * EarthRadiusMeters,
                dLat * EarthRadiusMeters);
        }

        private struct GeoPoint
        {
            public GeoPoint(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }

            public double Latitude { get; }
            public double Longitude { get; }
        }

        private struct LocalVector
        {
            public LocalVector(double east, double north)
            {
                East = east;
                North = north;
            }

            public double East { get; }
            public double North { get; }
        }
    }
}
