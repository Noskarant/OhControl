using System;
using System.Collections.Generic;
using System.Linq;

namespace OhControl.Lfly
{
    public sealed class LflyReportingPoint
    {
        public string Code { get; set; }
        public string SpokenCode { get; set; }
        public string Name { get; set; }
        public double LatitudeDeg { get; set; }
        public double LongitudeDeg { get; set; }
        public IReadOnlyList<string> RecognitionTerms { get; set; }

        public override string ToString()
        {
            return Code + " · " + Name;
        }
    }

    public sealed class LflyReportingPointMatch
    {
        public LflyReportingPoint Point { get; set; }
        public double DistanceNm { get; set; }
    }

    public static class LflyReportingPoints
    {
        // Published LFLY VFR reporting points.
        // Coordinates are decimal conversions of the SIA VAC values.
        public static readonly IReadOnlyList<LflyReportingPoint> All =
            new[]
            {
                new LflyReportingPoint
                {
                    Code = "N",
                    SpokenCode = "November",
                    Name = "Saint-Germain-au-Mont-d'Or",
                    LatitudeDeg = 45.8919444444,
                    LongitudeDeg = 4.8011111111,
                    RecognitionTerms = new[]
                    {
                        "point november",
                        "november",
                        "saint germain",
                        "saint germain au mont d or"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "S",
                    SpokenCode = "Sierra",
                    Name = "Chasse-sur-Rhône",
                    LatitudeDeg = 45.5897222222,
                    LongitudeDeg = 4.7966666667,
                    RecognitionTerms = new[]
                    {
                        "point sierra",
                        "sierra",
                        "chasse sur rhone"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "MS",
                    SpokenCode = "Mike Sierra",
                    Name = "Genas Sud-Est",
                    LatitudeDeg = 45.7233333333,
                    LongitudeDeg = 5.0233333333,
                    RecognitionTerms = new[]
                    {
                        "mike sierra",
                        "point ms",
                        "genas",
                        "genas sud est"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "NA",
                    SpokenCode = "November Alpha",
                    Name = "Intersection A46 / TGV / D1083",
                    LatitudeDeg = 45.8569444444,
                    LongitudeDeg = 4.9133333333,
                    RecognitionTerms = new[]
                    {
                        "november alpha",
                        "point na",
                        "a46",
                        "tgv d1083"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "SA",
                    SpokenCode = "Sierra Alpha",
                    Name = "Feyzin",
                    LatitudeDeg = 45.6497222222,
                    LongitudeDeg = 4.8405555556,
                    RecognitionTerms = new[]
                    {
                        "sierra alpha",
                        "point sa",
                        "feyzin"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "NW",
                    SpokenCode = "November Whiskey",
                    Name = "Barrage de Couzon",
                    LatitudeDeg = 45.8450000000,
                    LongitudeDeg = 4.8333333333,
                    RecognitionTerms = new[]
                    {
                        "november whiskey",
                        "point nw",
                        "couzon",
                        "barrage de couzon"
                    }
                },
                new LflyReportingPoint
                {
                    Code = "TW",
                    SpokenCode = "Tango Whiskey",
                    Name = "Rond-point de l'Europe",
                    LatitudeDeg = 45.7169444444,
                    LongitudeDeg = 5.0705555556,
                    RecognitionTerms = new[]
                    {
                        "tango whiskey",
                        "point tw",
                        "rond point de l europe",
                        "rond point europe"
                    }
                }
            };

        public static LflyReportingPoint FindInTranscript(
            string normalizedTranscript)
        {
            if (string.IsNullOrWhiteSpace(normalizedTranscript))
            {
                return null;
            }

            return All.FirstOrDefault(
                point =>
                    point.RecognitionTerms.Any(
                        term => normalizedTranscript.Contains(term)));
        }

        public static LflyReportingPointMatch FindNearest(
            double latitudeDeg,
            double longitudeDeg,
            double maximumDistanceNm = 1.0)
        {
            LflyReportingPointMatch best = null;

            foreach (LflyReportingPoint point in All)
            {
                double distance =
                    DistanceNm(
                        latitudeDeg,
                        longitudeDeg,
                        point.LatitudeDeg,
                        point.LongitudeDeg);

                if (distance > maximumDistanceNm)
                {
                    continue;
                }

                if (best == null ||
                    distance < best.DistanceNm)
                {
                    best = new LflyReportingPointMatch
                    {
                        Point = point,
                        DistanceNm = distance
                    };
                }
            }

            return best;
        }

        private static double DistanceNm(
            double latitude1,
            double longitude1,
            double latitude2,
            double longitude2)
        {
            const double EarthRadiusNm = 3440.065;

            double lat1 = DegreesToRadians(latitude1);
            double lat2 = DegreesToRadians(latitude2);
            double dLat = lat2 - lat1;
            double dLon =
                DegreesToRadians(
                    longitude2 - longitude1);

            double a =
                Math.Sin(dLat / 2) *
                Math.Sin(dLat / 2) +
                Math.Cos(lat1) *
                Math.Cos(lat2) *
                Math.Sin(dLon / 2) *
                Math.Sin(dLon / 2);

            double c =
                2 *
                Math.Atan2(
                    Math.Sqrt(a),
                    Math.Sqrt(1 - a));

            return EarthRadiusNm * c;
        }

        private static double DegreesToRadians(double value)
        {
            return value * Math.PI / 180.0;
        }
    }
}
