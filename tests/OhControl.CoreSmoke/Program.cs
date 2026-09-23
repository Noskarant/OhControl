using System;
using OhControl;
using OhControl.Atc;
using OhControl.Lfly;
using OhControl.Radio;

internal static class Program
{
    private static int Main()
    {
        try
        {
            VerifyGroundProfile();
            VerifyReportingPoints();
            VerifyAtcFlow();

            Console.WriteLine("OhControl core smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyGroundProfile()
    {
        Expect(
            LflyGroundProfile.ForRunway("16").FullLengthHoldingPoint == "A1",
            "RWY 16 full-length holding point must be A1.");

        Expect(
            LflyGroundProfile.ForRunway("34").FullLengthHoldingPoint == "A4",
            "RWY 34 full-length holding point must be A4.");
    }

    private static void VerifyReportingPoints()
    {
        LflyReportingPoint point =
            LflyReportingPoints.FindInTranscript(
                "bron tour fox golf alpha bravo charlie november whiskey arrivee");

        Expect(
            point != null && point.Code == "NW",
            "November Whiskey should resolve to NW.");

        LflyReportingPointMatch nearest =
            LflyReportingPoints.FindNearest(
                45.8450000000,
                4.8333333333,
                0.2);

        Expect(
            nearest != null && nearest.Point.Code == "NW",
            "NW coordinate lookup failed.");
    }

    private static void VerifyAtcFlow()
    {
        var atis = new AtisService();
        var engine = new AtcEngine(atis);

        var ground =
            new RadioStation(
                RadioStationKind.Ground,
                "BRON Sol",
                121.705);

        var tower =
            new RadioStation(
                RadioStationKind.Tower,
                "BRON Tour",
                118.100);

        var telemetry = new TelemetrySnapshot
        {
            LatitudeDeg = 45.7294444444,
            LongitudeDeg = 4.9388888889,
            AltitudeFt = 659,
            IsOnGround = true,
            WindDirectionTrueDeg = 160,
            WindSpeedKt = 5,
            AmbientTemperatureC = 20,
            VisibilityMeters = 10000,
            SeaLevelPressureMb = 1018,
            Com1ActiveMhz = 121.705
        };

        AtcResponse taxi =
            engine.Handle(
                ground,
                "Bron Sol F-GABC demande roulage tour de piste information Alpha",
                "F-GABC",
                telemetry);

        ExpectContains(
            taxi.Text,
            "A1",
            "RWY 16 taxi clearance should use A1.");

        AtcResponse taxiReadback =
            engine.Handle(
                ground,
                "Je roule point d'attente A1 piste 16 F-GABC",
                "F-GABC",
                telemetry);

        Expect(
            string.IsNullOrWhiteSpace(taxiReadback.Text),
            "Correct taxi readback should not create a new ATC clearance.");

        AtcResponse ready =
            engine.Handle(
                ground,
                "F-GABC point d'attente A1 prêt",
                "F-GABC",
                telemetry);

        ExpectContains(
            ready.Text,
            "Bron Tour",
            "Ground should transfer a ready aircraft to Tower.");

        telemetry.Com1ActiveMhz = 118.100;

        AtcResponse lineUp =
            engine.Handle(
                tower,
                "Bron Tour F-GABC point d'attente A1 prêt au départ",
                "F-GABC",
                telemetry);

        ExpectContains(
            lineUp.Text,
            "alignez-vous et attendez",
            "Tower should issue line-up-and-wait.");

        AtcResponse takeoff =
            engine.Handle(
                tower,
                "Je m'aligne et j'attends piste 16 F-GABC",
                "F-GABC",
                telemetry);

        ExpectContains(
            takeoff.Text,
            "autorisé décollage",
            "Correct line-up readback should progress to take-off clearance.");

        AtcResponse takeoffReadback =
            engine.Handle(
                tower,
                "Piste 16 je décolle F-GABC",
                "F-GABC",
                telemetry);

        Expect(
            string.IsNullOrWhiteSpace(takeoffReadback.Text),
            "Correct take-off readback should not trigger another ATC instruction.");

        telemetry.IsOnGround = false;
        telemetry.LatitudeDeg = 45.8450000000;
        telemetry.LongitudeDeg = 4.8333333333;
        telemetry.AltitudeFt = 2000;

        AtcResponse arrival =
            engine.Handle(
                tower,
                "Bron Tour F-GABC November Whiskey arrivée pour Bron",
                "F-GABC",
                telemetry);

        ExpectContains(
            arrival.Text,
            "intégrez vent arrière",
            "Arrival from a published reporting point should receive a circuit integration.");
    }

    private static void Expect(
        bool condition,
        string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void ExpectContains(
        string value,
        string expected,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.IndexOf(
                expected,
                StringComparison.OrdinalIgnoreCase) < 0)
        {
            throw new InvalidOperationException(
                message +
                " Actual: " +
                (value ?? "<null>"));
        }
    }
}
