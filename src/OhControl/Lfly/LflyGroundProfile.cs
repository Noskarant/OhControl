using System;
using System.Collections.Generic;

namespace OhControl.Lfly
{
    public sealed class LflyRunwayGroundProfile
    {
        public string Runway { get; set; }
        public string FullLengthHoldingPoint { get; set; }
        public IReadOnlyList<string> OtherRunwayHoldingPoints { get; set; }
    }

    public static class LflyGroundProfile
    {
        // AIP France AD 2 LFLY, AIRAC 2026-09.
        // A1/A4 are the reference full-length alignment points for RWY 16/34.
        // A2/A3 are also runway-protected holding points.
        // A5 may only be used with ATC clearance.
        public static readonly IReadOnlyList<string> Taxiways =
            new[]
            {
                "A1", "A2", "A3", "A4", "A5",
                "T1", "T2", "T3",
                "TA", "TB", "TC",
                "TN", "TN2",
                "TC1", "TC2", "TC3", "TC4",
                "TC5", "TC6", "TC7"
            };

        public static readonly IReadOnlyList<string> RunwayHoldingPoints =
            new[] { "A1", "A2", "A3", "A4" };

        public const string RestrictedTaxiway = "A5";

        public static LflyRunwayGroundProfile ForRunway(string runway)
        {
            return runway == "34"
                ? new LflyRunwayGroundProfile
                {
                    Runway = "34",
                    FullLengthHoldingPoint = "A4",
                    OtherRunwayHoldingPoints = new[] { "A3", "A2", "A1" }
                }
                : new LflyRunwayGroundProfile
                {
                    Runway = "16",
                    FullLengthHoldingPoint = "A1",
                    OtherRunwayHoldingPoints = new[] { "A2", "A3", "A4" }
                };
        }

        public static bool IsKnownHoldingPoint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            foreach (string point in RunwayHoldingPoints)
            {
                if (string.Equals(
                    point,
                    value.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
