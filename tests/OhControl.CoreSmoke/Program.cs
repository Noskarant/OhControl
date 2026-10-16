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
            VerifyElevenV4Speech();
            VerifyStationVoiceSelectionAndBackwardCompatibility();
            VerifyPttCaptureState();
            VerifyGroundProfile();
            VerifyIdentifierSpeech();
            VerifyFrequencySpeech();
            VerifyReportingPoints();
            VerifyAtcFlow();
            VerifyGroundHandoffOnlyAtHoldingPoint();
            VerifyVhfNoiseProfile();

            Console.WriteLine("OhControl core smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyElevenV4Speech()
    {
        var dictionary = OhControl.ElevenLabs.ElevenV4Speech.CreateDictionaryRequest();
        var rules = (Newtonsoft.Json.Linq.JArray)dictionary["rules"];
        Expect(rules.Count == 3, "Bron pronunciation dictionary case variants.");
        Expect((string)rules[0]["phoneme"] == "bʁɔ̃",
            "Bron should have a French nasal vowel.");

        var locator = OhControl.ElevenLabs.ElevenV4Speech.CreateLocator(
            "dictionary-1", "version-1");
        string message = "Fox Golf Alpha Bravo Charlie, Bron Sol, transmettez vos intentions.";
        var payload = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            message, "existing-voice", locator);
        Expect((string)payload["model_id"] == "eleven_v4",
            "Must use Eleven v4.");
        Expect((string)payload["language_code"] == "fr",
            "French locale must be enforced.");
        Expect((string)payload["text"] == message,
            "Synthesis must use the exact text without long scene prompts.");
        Expect(payload["inputs"] == null,
            "Single-speaker TTS must never include a dialogue inputs array.");
        Expect((double)payload["voice_settings"]["stability"] == 0.85 &&
               (double)payload["voice_settings"]["similarity_boost"] == 0.85,
            "Consistent shared v4 settings must be applied.");
        Expect(payload["voice_settings"]["speed"] == null &&
               payload["voice_settings"]["style"] == null,
            "Unsupported v4 parameters must not be sent.");
        Expect((string)payload["pronunciation_dictionary_locators"][0]
            ["pronunciation_dictionary_id"] == "dictionary-1",
            "The pronunciation dictionary should be attached.");

        var fallback = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            message, "existing-voice", null);
        Expect(((string)fallback["text"]).Contains("/bʁɔ̃/ Sol"),
            "Inline IPA must correct Bron when the dictionary is unavailable.");
        Expect(fallback["pronunciation_dictionary_locators"] == null,
            "Do not attach an unavailable dictionary.");

        var ground = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            message, "ground-voice", locator,
            OhControl.ElevenLabs.AtcVoiceMode.Ground);
        var atis = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            message, "atis-voice", locator,
            OhControl.ElevenLabs.AtcVoiceMode.Atis);
        Expect((string)ground["text"] == (string)atis["text"] &&
               (string)ground["text"] == (string)payload["text"],
            "All stations must use identical text preparation.");
        Expect(Newtonsoft.Json.Linq.JToken.DeepEquals(
                ground["voice_settings"], atis["voice_settings"]) &&
               Newtonsoft.Json.Linq.JToken.DeepEquals(
                ground["voice_settings"], payload["voice_settings"]),
            "All three voices must share EXACTLY the same speaking parameters.");

        string url = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechUrl(
            " existing-voice ");
        Expect(url == "https://api.elevenlabs.io/v1/text-to-speech/existing-voice?output_format=mp3_44100_128",
            "Must use the official v4-compatible single-speaker endpoint.");

        var normal = "Fox Golf Alpha Bravo Charlie, transmettez vos intentions.";
        Expect(OhControl.ElevenLabs.ElevenV4Speech.IsAudioDurationPlausible(
            normal, 6.5),
            "Ordinary radio replies must be accepted.");
        Expect(!OhControl.ElevenLabs.ElevenV4Speech.IsAudioDurationPlausible(
            normal, 80),
            "Repeated callsign for 80 seconds must be rejected.");
        var bulletin = new OhControl.Radio.AtisService().Build(null);
        Expect(OhControl.ElevenLabs.ElevenV4Speech.IsAudioDurationPlausible(
            bulletin.Text, 35),
            "A 35-second ATIS must not be discarded.");
        Expect(!OhControl.ElevenLabs.ElevenV4Speech.IsAudioDurationPlausible(
            bulletin.Text, 240),
            "Pathologically long ATIS generation must not be accepted.");
        Expect(!OhControl.ElevenLabs.ElevenV4Speech.IsAudioDurationPlausible(
            normal, double.NaN),
            "Invalid MP3 durations must not be accepted.");

        var forbidden = new Newtonsoft.Json.Linq.JObject
        {
            ["detail"] = new Newtonsoft.Json.Linq.JObject
            {
                ["type"] = "authentication_error",
                ["code"] = "unauthorized",
                ["status"] = "missing_permissions",
                ["message"] = "The API key is missing the permission pronunciation_dictionaries_write."
            }
        }.ToString(Newtonsoft.Json.Formatting.None);

        Expect(OhControl.ElevenLabs.ElevenV4Speech.IsDictionaryWritePermissionMissing(
            System.Net.HttpStatusCode.Unauthorized, forbidden),
            "Dictionary permission denial must fall back to inline IPA.");
        Expect(!OhControl.ElevenLabs.ElevenV4Speech.IsDictionaryWritePermissionMissing(
            System.Net.HttpStatusCode.BadRequest, forbidden),
            "Other errors should not be swallowed.");
    }

    private static void VerifyStationVoiceSelectionAndBackwardCompatibility()
    {
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<
            OhControl.Configuration.OhControlSettings>(
            "{\"ElevenLabsVoiceId\":\"old-tower-voice\",\"ElevenLabsApiKey\":\"test\"}");

        Expect(legacy.IsElevenLabsConfigured,
            "Legacy voice settings must remain configured.");
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.SelectVoiceId(
                legacy.ElevenLabsVoiceId, legacy.ElevenLabsGroundVoiceId,
                legacy.ElevenLabsAtisVoiceId, OhControl.ElevenLabs.AtcVoiceMode.Tower)
                == "old-tower-voice",
            "Legacy voice ID must remain Tower.");
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.SelectVoiceId(
                legacy.ElevenLabsVoiceId, legacy.ElevenLabsGroundVoiceId,
                legacy.ElevenLabsAtisVoiceId, OhControl.ElevenLabs.AtcVoiceMode.Ground)
                == "old-tower-voice",
            "Ground must use Tower voice if blank.");
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.SelectVoiceId(
                legacy.ElevenLabsVoiceId, legacy.ElevenLabsGroundVoiceId,
                legacy.ElevenLabsAtisVoiceId, OhControl.ElevenLabs.AtcVoiceMode.Atis)
                == "old-tower-voice",
            "ATIS must use Tower voice if blank.");

        legacy.ElevenLabsGroundVoiceId = " ground-voice ";
        legacy.ElevenLabsAtisVoiceId = " atis-voice ";
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.SelectVoiceId(
                legacy.ElevenLabsVoiceId, legacy.ElevenLabsGroundVoiceId,
                legacy.ElevenLabsAtisVoiceId, OhControl.ElevenLabs.AtcVoiceMode.Ground)
                == "ground-voice",
            "Ground must use its own selected voice.");
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.SelectVoiceId(
                legacy.ElevenLabsVoiceId, legacy.ElevenLabsGroundVoiceId,
                legacy.ElevenLabsAtisVoiceId, OhControl.ElevenLabs.AtcVoiceMode.Atis)
                == "atis-voice",
            "ATIS must use its own selected voice.");

        var persisted = Newtonsoft.Json.JsonConvert.DeserializeObject<
            OhControl.Configuration.OhControlSettings>(
            Newtonsoft.Json.JsonConvert.SerializeObject(legacy));
        Expect(persisted.ElevenLabsVoiceId == "old-tower-voice" &&
               persisted.ElevenLabsGroundVoiceId == " ground-voice " &&
               persisted.ElevenLabsAtisVoiceId == " atis-voice ",
               "All three voice IDs must survive a JSON settings roundtrip.");

        string sample = "F-GABC, Bron Tour, bonjour.";
        var tower = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            sample, legacy.ElevenLabsVoiceId, null,
            OhControl.ElevenLabs.AtcVoiceMode.Tower);
        var ground = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            sample, "ground-voice", null,
            OhControl.ElevenLabs.AtcVoiceMode.Ground);
        var atis = OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechRequest(
            sample, "atis-voice", null,
            OhControl.ElevenLabs.AtcVoiceMode.Atis);

        Expect((string)tower["text"] == (string)ground["text"] &&
               (string)tower["text"] == (string)atis["text"],
               "All controllers use identical speech content rules.");
        Expect(Newtonsoft.Json.Linq.JToken.DeepEquals(
                tower["voice_settings"], ground["voice_settings"]) &&
               Newtonsoft.Json.Linq.JToken.DeepEquals(
                tower["voice_settings"], atis["voice_settings"]),
               "Each station must use the same speed and delivery tuning.");
        Expect(
            OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechUrl("ground-voice")
                .Contains("/ground-voice?") &&
            OhControl.ElevenLabs.ElevenV4Speech.CreateSpeechUrl("atis-voice")
                .Contains("/atis-voice?"),
            "Different station IDs must route to their own TTS voice.");

    }

    private static void VerifyPttCaptureState()
    {
        var capture = new OhControl.Voice.PttCaptureState();

        Expect(capture.TryBeginCapture(), "Initial PTT press must start capture.");
        Expect(!capture.TryBeginCapture(), "Duplicate PTT press must be ignored.");
        Expect(capture.TryBeginStop(), "PTT release must begin WAV finalization.");
        Expect(!capture.TryBeginStop(), "Duplicate PTT release must be ignored.");
        Expect(!capture.TryBeginCapture(),
            "PTT must not restart while the old microphone is still stopping.");

        capture.CompleteStop();
        Expect(capture.TryBeginCapture(),
            "PTT must immediately work again after WAV finalization.");

        capture.CancelCapture();
        Expect(capture.TryBeginCapture(),
            "Failed microphone startup must not leave PTT blocked.");
        Expect(capture.TryBeginStop(), "PTT must still release after retry.");
        capture.CompleteStop();
        Expect(capture.TryBeginCapture(),
            "Successive pilot transmissions must remain available.");
        capture.CancelCapture();
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

    private static void VerifyIdentifierSpeech()
    {
        Expect(
            AviationFrenchNumbers.Identifier("A4") == "Alpha quatre",
            "A4 must be spoken as Alpha quatre.");

        Expect(
            AviationFrenchNumbers.Identifier("TN2") == "Tango November deux",
            "Compound taxiway identifiers must use ICAO spelling and French digits.");
    }

    private static void VerifyFrequencySpeech()
    {
        Expect(
            AviationFrenchNumbers.Frequency(118.100) ==
            "unité, unité, huit décimale unité",
            "118.100 must keep both leading 'un' words and omit trailing double zero.");

        Expect(
            AviationFrenchNumbers.Frequency(121.705) ==
            "unité, deux, unité décimale sept, zéro, cinq",
            "121.705 must be spoken digit by digit.");

        Expect(
            AviationFrenchNumbers.Frequency(128.130) ==
            "unité, deux, huit décimale unité, trois, zéro",
            "128.130 must preserve all three decimal digits.");
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

    private static void VerifyVhfNoiseProfile()
    {
        double clear = OhControl.Audio.VhfNoiseProfile.SpeechHiss(1.0);
        double weak = OhControl.Audio.VhfNoiseProfile.SpeechHiss(0.35);

        Expect(clear >= 0.025 && clear <= 0.045,
            "Clear VHF signal must keep audible but subtle static.");
        Expect(weak > clear && weak < 0.09,
            "Noise grows moderately for weak radio reception.");
        Expect(OhControl.Audio.VhfNoiseProfile.CrackleChance(0.35) >
               OhControl.Audio.VhfNoiseProfile.CrackleChance(1.0),
            "Weak frequencies should crackle slightly more.");
        Expect(OhControl.Audio.VhfNoiseProfile.CrackleChance(1.0) < 0.00002,
            "Strong frequencies must not crackle continuously.");
        Expect(OhControl.Audio.VhfNoiseProfile.CarrierLeadMilliseconds >= 70 &&
               OhControl.Audio.VhfNoiseProfile.CarrierLeadMilliseconds <= 150,
            "Radio carrier should open briefly before speech.");
        Expect(OhControl.Audio.VhfNoiseProfile.CarrierTailMilliseconds >= 50 &&
               OhControl.Audio.VhfNoiseProfile.CarrierTailMilliseconds <= 130,
            "Carrier static should close shortly after speech.");
        Expect(!double.IsNaN(OhControl.Audio.VhfNoiseProfile.SpeechHiss(double.NaN)),
            "Invalid telemetry must not break the audio profile.");
    }

    private static void VerifyGroundHandoffOnlyAtHoldingPoint()
    {
        var ground = new RadioStation(
            RadioStationKind.Ground, "BRON Sol", 121.705);
        var atis = new AtisService();
        var location = new TelemetrySnapshot
        {
            IsOnGround = true,
            GroundSpeedKt = 0,
            WindDirectionTrueDeg = 160,
            WindSpeedKt = 5,
            SeaLevelPressureMb = 1018
        };
        var engine = new AtcEngine(atis);

        var premature = engine.Handle(
            ground, "F-GABC je suis prêt au départ, au parking",
            "F-GABC", location);
        Expect(!((premature.Text ?? "").Contains("Bron Tour")),
            "Before taxi clearance ATC must not transfer to Tower.");

        engine.Handle(ground,
            "Bron Sol F-GABC demande roulage pour tours de piste",
            "F-GABC", location);

        var taxiReadback = engine.Handle(
            ground,
            "F-GABC je roule point d'attente A1 piste 16 QNH 1018, " +
            "je rappellerai prêt au départ au point d'attente",
            "F-GABC", location);
        Expect(string.IsNullOrWhiteSpace(taxiReadback.Text) ||
               !taxiReadback.Text.Contains("Bron Tour"),
            "Reading back 'rappellerai prêt' must not trigger a Tower handoff.");

        var parking = engine.Handle(
            ground, "F-GABC toujours au parking, prêt au départ, je n'ai pas bougé",
            "F-GABC", location);
        Expect(!((parking.Text ?? "").Contains("Bron Tour")),
            "Pilot still at parking must never be transferred.");

        var taxiing = engine.Handle(
            ground, "F-GABC je roule vers le point d'attente A1 prêt au départ",
            "F-GABC", location);
        Expect(!((taxiing.Text ?? "").Contains("Bron Tour")),
            "Taxiing towards the hold must not trigger transfer.");

        var destination = engine.Handle(
            ground, "F-GABC je suis au point d'arrêt A1",
            "F-GABC", location);
        ExpectContains(destination.Text, "Bron Tour",
            "Only explicit arrival at the holding point triggers Tower.");
        ExpectContains(destination.Text, "au revoir",
            "Tower handoff must include goodbye.");

        var separate = new AtcEngine(atis);
        separate.Handle(ground,
            "Bron Sol F-GABC demande roulage pour tours de piste",
            "F-GABC", location);
        var properReadback = separate.Handle(
            ground, "F-GABC je roule point d'attente A1 piste 16 QNH 1018",
            "F-GABC", location);
        Expect(string.IsNullOrWhiteSpace(properReadback.Text),
            "Ordinary taxi readback must remain silent.");
        var departure = separate.Handle(
            ground, "F-GABC point d'attente A1 prêt au départ",
            "F-GABC", location);
        ExpectContains(departure.Text, "Bron Tour",
            "Normal short ready-at-hold report must trigger handoff.");

        var wrongContext = new AtcEngine(atis);
        var noClearance = wrongContext.Handle(
            ground, "F-GABC point d'attente A1 prêt",
            "F-GABC", location);
        Expect(!((noClearance.Text ?? "").Contains("Bron Tour")),
            "A station contact without taxi clearance should not transfer.");
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

        var naturalEngine =
            new AtcEngine(atis);

        AtcResponse naturalCircuitRequest =
            naturalEngine.Handle(
                ground,
                "Bron Sol bonjour F-GABC au parking demande une clairance pour faire des tours de piste information Alpha",
                "F-GABC",
                telemetry);

        ExpectContains(
            naturalCircuitRequest.Text,
            "roulez point d'attente",
            "Natural circuit/departure request on Ground must produce taxi instructions, not a generic transmettez.");

        AtcResponse naturalCircuitRequestShort =
            new AtcEngine(atis).Handle(
                ground,
                "Bron Sol bonjour F-GABC au parking pour des tours de piste",
                "F-GABC",
                telemetry);

        ExpectContains(
            naturalCircuitRequestShort.Text,
            "roulez point d'attente",
            "A circuit request embedded in the initial call must be handled operationally.");

        AtcResponse naturalCircuitRequestVariant =
            new AtcEngine(atis).Handle(
                ground,
                "Bron Sol F-GABC je voudrais effectuer des tours de piste",
                "F-GABC",
                telemetry);

        ExpectContains(
            naturalCircuitRequestVariant.Text,
            "roulez point d'attente",
            "Natural wording such as voudrais effectuer des tours de piste must be recognized.");

        AtcResponse taxi =
            engine.Handle(
                ground,
                "Bron Sol F-GABC demande roulage tour de piste information Alpha",
                "F-GABC",
                telemetry);

        ExpectContains(
            taxi.Text,
            "Alpha un",
            "RWY 16 taxi clearance should speak A1 as Alpha un.");

        AtcResponse groundRepeat =
            engine.Handle(
                ground,
                "Bron Sol F-GABC répétez s'il vous plaît",
                "F-GABC",
                telemetry);

        ExpectContains(
            groundRepeat.Text,
            "je répète",
            "Ground repeat request must replay the last ATC transmission.");

        ExpectContains(
            groundRepeat.Text,
            "Alpha un",
            "Repeated Ground transmission must preserve the spoken holding point.");

        AtcResponse groundRepeatAgain =
            engine.Handle(
                ground,
                "F-GABC pouvez-vous répéter",
                "F-GABC",
                telemetry);

        Expect(
            groundRepeatAgain.Text.IndexOf(
                "je répète, je répète",
                StringComparison.OrdinalIgnoreCase) < 0,
            "Repeated repeat requests must not recursively repeat the previous repeat wrapper.");

        var acknowledgementEngine =
            new AtcEngine(atis);

        AtcResponse acknowledgementTaxi =
            acknowledgementEngine.Handle(
                ground,
                "Bron Sol F-GABC demande roulage pour tours de piste",
                "F-GABC",
                telemetry);

        AtcResponse bareAcknowledgement =
            acknowledgementEngine.Handle(
                ground,
                "F-GABC bien reçu",
                "F-GABC",
                telemetry);

        ExpectContains(
            bareAcknowledgement.Text,
            "collationnez",
            "A bare acknowledgement must not satisfy a mandatory taxi readback.");

        AtcResponse taxiReadback =
            engine.Handle(
                ground,
                "Je roule point d'attente A1 piste 16 QNH 1018 F-GABC",
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

        ExpectContains(
            ready.Text,
            "au revoir",
            "Every Ground-to-Tower frequency handoff should end with au revoir.");

        AtcResponse frequencyReadback =
            engine.Handle(
                ground,
                "unité unité huit décimale unité F-GABC au revoir",
                "F-GABC",
                telemetry);

        ExpectContains(
            frequencyReadback.Text,
            "au revoir",
            "Frequency readback with goodbye should close the Ground exchange naturally.");

        var combinedReadyEngine =
            new AtcEngine(atis);

        AtcResponse combinedTaxi =
            combinedReadyEngine.Handle(
                ground,
                "Bron Sol F-GABC demande roulage pour tours de piste",
                "F-GABC",
                telemetry);

        AtcResponse combinedReady =
            combinedReadyEngine.Handle(
                ground,
                "F-GABC point d'attente A1 piste un six QNH 1018, on est prêt au départ",
                "F-GABC",
                telemetry);

        ExpectContains(
            combinedReady.Text,
            "Bron Tour",
            "Combined taxi readback and ready call must immediately trigger the Tower handoff.");

        ExpectContains(
            combinedReady.Text,
            "unité, unité, huit décimale unité",
            "Ground-to-Tower handoff must include the spoken 118.100 frequency.");

        ExpectContains(
            combinedReady.Text,
            "au revoir",
            "Combined ready handoff must end with au revoir.");

        var spokenReadbackEngine =
            new AtcEngine(atis);

        telemetry.WindDirectionTrueDeg = 340;
        telemetry.Com1ActiveMhz = 121.705;

        AtcResponse taxi34 =
            spokenReadbackEngine.Handle(
                ground,
                "Bron Sol F-GABC demande roulage pour tours de piste",
                "F-GABC",
                telemetry);

        ExpectContains(
            taxi34.Text,
            "Alpha quatre",
            "RWY 34 taxi clearance must speak A4 as Alpha quatre.");

        AtcResponse spokenTaxiReadback =
            spokenReadbackEngine.Handle(
                ground,
                "Je roule point d'attente Alpha quatre piste trois quatre QNH 1018 F-GABC",
                "F-GABC",
                telemetry);

        Expect(
            string.IsNullOrWhiteSpace(spokenTaxiReadback.Text),
            "Spoken readback Alpha quatre must be accepted as A4.");

        telemetry.WindDirectionTrueDeg = 160;
        telemetry.Com1ActiveMhz = 118.100;

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

        AtcResponse towerRepeat =
            engine.Handle(
                tower,
                "Bron Tour F-GABC répétez",
                "F-GABC",
                telemetry);

        ExpectContains(
            towerRepeat.Text,
            "je répète",
            "Tower repeat request must replay the last ATC transmission.");

        ExpectContains(
            towerRepeat.Text,
            "alignez-vous et attendez",
            "Tower repeat must preserve the operational instruction.");

        AtcResponse lineUpReadback =
            engine.Handle(
                tower,
                "Fox Alpha Charlie on s'aligne et on attend",
                "F-GABC",
                telemetry);

        Expect(
            string.IsNullOrWhiteSpace(
                lineUpReadback.Text),
            "A correct line-up-and-wait readback must not trigger an immediate ATC reply.");

        Expect(
            lineUpReadback.ControllerFollowUpDelaySeconds == 18,
            "A line-up-and-wait readback should schedule a Tower follow-up after about twenty seconds.");

        AtcResponse takeoff =
            engine.BuildScheduledFollowUp(
                "F-GABC",
                tower,
                telemetry);

        ExpectContains(
            takeoff.Text,
            "autorisé décollage",
            "The scheduled Tower follow-up should issue take-off clearance when the runway is free.");

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

        AtcResponse reportAcknowledgement =
            engine.Handle(
                tower,
                "F-GABC je rappelle vent arrière",
                "F-GABC",
                telemetry);

        Expect(
            string.IsNullOrWhiteSpace(
                reportAcknowledgement.Text),
            "Report acknowledgement must not be mistaken for an actual downwind report.");

        var goodbyeEngine =
            new AtcEngine(atis);

        AtcResponse goodbye =
            goodbyeEngine.Handle(
                tower,
                "F-GABC merci au revoir",
                "F-GABC",
                telemetry);

        ExpectContains(
            goodbye.Text,
            "au revoir",
            "ATC should respond naturally to a pilot goodbye.");
        var handoffEngine =
            new AtcEngine(atis);

        AtcResponse towerToGround =
            handoffEngine.Handle(
                tower,
                "F-GABC piste dégagée",
                "F-GABC",
                telemetry);

        ExpectContains(
            towerToGround.Text,
            "Bron Sol",
            "Tower-to-Ground handoff should include Bron Sol.");

        ExpectContains(
            towerToGround.Text,
            "unité, deux, unité décimale sept, zéro, cinq",
            "Tower-to-Ground handoff should include 121.705.");

        ExpectContains(
            towerToGround.Text,
            "au revoir",
            "Tower-to-Ground handoff should end with au revoir.");

        AtcResponse frequencyRelease =
            handoffEngine.Handle(
                tower,
                "F-GABC quitte la fréquence",
                "F-GABC",
                telemetry);

        ExpectContains(
            frequencyRelease.Text,
            "au revoir",
            "Frequency release should end with au revoir.");

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
