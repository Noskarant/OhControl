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
            VerifyLineupReadbackVariationsAndPhysicalAlignment();
            VerifyCrossFrequencyRunwaySeparation();
            VerifyFrequencyTransferReadbackLifecycle();
            VerifyAtisCorrectionAndReadbackErrors();
            VerifyShortCallsignReadbacks();
            VerifyGroundHandoffOnlyAtHoldingPoint();
            VerifyFutureReadyCallDoesNotHandoff();
            VerifyVhfNoiseProfile();
            VerifyDirectHostAndGuestNetwork();

            Console.WriteLine("OhControl core smoke tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void VerifyDirectHostAndGuestNetwork()
    {
        // End-to-end loopback: two PCs, two callsigns, one room, radio/audio.
        int port;
        var probe = new System.Net.Sockets.TcpListener(
            System.Net.IPAddress.Loopback, 0);
        probe.Start();
        port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var hostSettings = new OhControl.Configuration.OhControlSettings
        {
            PlayerId = "pc-noe",
            PilotCallsign = "F-GABC",
            DirectMultiplayerEnabled = true,
            DirectMultiplayerIsHost = true,
            DirectMultiplayerPort = port,
            DirectMultiplayerRoomCode = "test-room-strong-shared-code"
        };
        var guestSettings = new OhControl.Configuration.OhControlSettings
        {
            PlayerId = "pc-kelian",
            PilotCallsign = "F-HXYZ",
            DirectMultiplayerEnabled = true,
            DirectMultiplayerIsHost = false,
            DirectMultiplayerHostAddress = "127.0.0.1",
            DirectMultiplayerPort = port,
            DirectMultiplayerRoomCode = hostSettings.DirectMultiplayerRoomCode
        };

        using (var host =
            new OhControl.Multiplayer.DirectMultiplayerSession(hostSettings))
        using (var guest =
            new OhControl.Multiplayer.DirectMultiplayerSession(guestSettings))
        {
            string pilot = null;
            string atc = null;
            byte[] audio = null;
            byte[] pilotAudio = null;
            host.RemoteTranscriptReceived += (p, message) =>
                pilot = p.Callsign + ": " + message;
            guest.RemoteAtcResponseReceived += (p, message, freq) =>
                atc = message;
            guest.RemoteAtcAudioReceived += (p, freq, bytes) =>
                audio = bytes;
            host.RemoteAudioChunkReceived += (p, freq, bytes) =>
                pilotAudio = bytes;

            host.ConnectAsync().GetAwaiter().GetResult();
            guest.ConnectAsync().GetAwaiter().GetResult();
            WaitFor(() => host.RemotePlayers.Count == 1 &&
                guest.RemotePlayers.Count == 1,
                "Direct host and guest must discover both identities.");
            Expect(host.RemotePlayers[0].Callsign == "F-HXYZ" &&
                guest.RemotePlayers[0].Callsign == "F-GABC",
                "Pilot identification must distinguish the two aircraft.");

            guest.UpdateLocalTelemetry(
                new OhControl.Multiplayer.MultiplayerPlayerState
                {
                    PlayerId = guestSettings.PlayerId,
                    Callsign = guestSettings.PilotCallsign,
                    LatitudeDeg = 45.735,
                    LongitudeDeg = 4.941,
                    Com1ActiveMhz = 118.100,
                    CircuitPhase = "Final",
                    IsOnGround = false
                });
            WaitFor(() =>
                host.RemotePlayers[0].CircuitPhase == "Final",
                "Host must receive live guest telemetry.");

            guest.BroadcastTranscriptAsync(
                "Bron Tour F-HXYZ en finale piste 16", 118.100)
                .GetAwaiter().GetResult();
            WaitFor(() => pilot != null, "Host must receive guest transcript.");
            Expect(pilot.Contains("F-HXYZ"), "Guest identity must accompany radio.");

            byte[] testAudio = new byte[] { 0x01, 0x02, 0x03 };
            host.BroadcastAtcResponseAsync(
                "F-HXYZ poursuivez", 118.100, "F-HXYZ")
                .GetAwaiter().GetResult();
            host.BroadcastAtcAudioAsync(testAudio, 118.100)
                .GetAwaiter().GetResult();
            WaitFor(() => atc != null && audio != null,
                "Guest must receive one host ATC text and one audio packet.");
            Expect(atc.Contains("F-HXYZ") && audio.Length == 3,
                "ATC voice bytes must be shared, not regenerated.");

            guest.BroadcastVoiceChunkAsync(testAudio, 118.100)
                .GetAwaiter().GetResult();
            WaitFor(() => pilotAudio != null,
                "Host must receive live pilot radio audio.");
            Expect(pilotAudio.Length == 3,
                "Pilot radio packets must not be lost.");
        }
    }

    private static void WaitFor(Func<bool> condition, string error)
    {
        var limit = DateTime.UtcNow.AddSeconds(6);
        while (!condition() && DateTime.UtcNow < limit)
            System.Threading.Thread.Sleep(25);
        Expect(condition(), error);
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

    private static void VerifyFutureReadyCallDoesNotHandoff()
    {
        var station = new RadioStation(
            RadioStationKind.Ground, "BRON Sol", 121.705);
        var position = new TelemetrySnapshot
        {
            IsOnGround = true,
            GroundSpeedKt = 0,
            WindDirectionTrueDeg = 160,
            WindSpeedKt = 5,
            SeaLevelPressureMb = 1018
        };
        string[] readbacks =
        {
            "F-GABC point d'attente A1 piste 16 QNH 1018 et on vous rappelle prêt au départ",
            "F-GABC roulez point d'attente A1 piste 16 QNH 1018, on vous rappelle prêt au départ",
            "F-GABC point d'attente A1 piste 16 QNH 1018 on vous rappellera prêt au départ",
            "F-GABC point d'attente A1 piste 16 QNH 1018, nous vous rappelons prêt au départ",
            "F-GABC point d'attente A1 piste 16 QNH 1018, je vous rappelle prêt au départ",
            "F-GABC point d'attente A1 piste 16 QNH 1018, rappelez prêt au départ"
        };

        foreach (string readback in readbacks)
        {
            var engine = new AtcEngine(new AtisService());
            engine.Handle(station,
                "Bron Sol F-GABC demande roulage pour tours de piste",
                "F-GABC", position);

            var response = engine.Handle(
                station, readback, "F-GABC", position);
            Expect(!((response.Text ?? "").Contains("Bron Tour")),
                "A future ready call during taxi readback must NEVER transfer: " +
                readback);

            // Even on a subsequent radio message, a future statement
            // alone must not turn into a position report.
            var future = engine.Handle(
                station, "F-GABC et on vous rappelle prêt au départ",
                "F-GABC", position);
            Expect(!((future.Text ?? "").Contains("Bron Tour")),
                "The phrase 'on vous rappelle' announces a future call.");
            var arrival = engine.Handle(
                station, "F-GABC je suis au point d'attente A1",
                "F-GABC", position);
            ExpectContains(arrival.Text, "Bron Tour",
                "An explicit subsequent holding-point report must transfer.");
        }
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

    private static void VerifyFrequencyTransferReadbackLifecycle()
    {
        var ground = new RadioStation(
            RadioStationKind.Ground, "Bron Sol", 121.705);
        var tower = new RadioStation(
            RadioStationKind.Tower, "Bron Tour", 118.100);
        var telemetry = new TelemetrySnapshot
        {
            IsOnGround = true,
            WindDirectionTrueDeg = 160,
            WindSpeedKt = 5,
            SeaLevelPressureMb = 1018
        };

        var accepted = new[]
        {
            "118.100 F-GABC",
            "118,1 F-GABC au revoir",
            "118 100 F-GABC",
            "118 1 F-GABC",
            "unité unité huit décimale unité F-GABC",
            "un un huit décimale un F-GABC",
            "cent dix-huit virgule un F-GABC",
            "cent dix huit un F-GABC",
            "On passe sur Bron Tour cent dix-huit décimale un, F-GABC"
        };

        foreach (string readback in accepted)
        {
            var engine = CreateGroundTransfer(ground, telemetry);
            var response = engine.Handle(
                ground, readback, "F-GABC", telemetry);

            Expect((response.Text ?? "").IndexOf(
                       "transmettez", StringComparison.OrdinalIgnoreCase) < 0 &&
                   (response.Text ?? "").IndexOf(
                       "collationnez", StringComparison.OrdinalIgnoreCase) < 0,
                "Correct frequency readback must not be repeated: " + readback);

            var redundant = engine.Handle(
                ground, "F-GABC bien reçu", "F-GABC", telemetry);

            Expect(string.IsNullOrWhiteSpace(redundant.Text),
                "A closed Ground exchange must not restart: " + readback);

            var firstTowerCall = engine.Handle(
                tower, "Bron Tour F-GABC bonjour", "F-GABC", telemetry);
            ExpectContains(firstTowerCall.Text, "Bron Tour",
                "Tower must accept a new contact after the frequency change.");
        }

        var wrongEngine = CreateGroundTransfer(ground, telemetry);
        var wrong = wrongEngine.Handle(
            ground, "121.705 F-GABC", "F-GABC", telemetry);
        ExpectContains(wrong.Text, "collationnez",
            "An incorrect frequency must not complete the transfer.");

        var oneReminder = wrongEngine.Handle(
            ground, "Je passe sur la tour F-GABC", "F-GABC", telemetry);
        Expect(string.IsNullOrWhiteSpace(oneReminder.Text),
            "A second ambiguous readback must not repeat the correction.");

        var recovery = wrongEngine.Handle(
            ground, "cent dix huit décimale un F-GABC au revoir",
            "F-GABC", telemetry);
        ExpectContains(recovery.Text, "au revoir",
            "After clarification a valid readback must be accepted.");

        var truncated = CreateGroundTransfer(ground, telemetry);
        var unknown = truncated.Handle(
            ground, "F-GABC", "F-GABC", telemetry);
        Expect(string.IsNullOrWhiteSpace(unknown.Text),
            "Unrecognized handoff acknowledgement must not become a new call.");

        var retry = truncated.Handle(
            ground, "F-GABC 118.1", "F-GABC", telemetry);
        Expect(string.IsNullOrWhiteSpace(retry.Text),
            "A valid numeric readback after an ambiguous message must close.");
    }

    private static AtcEngine CreateGroundTransfer(
        RadioStation ground, TelemetrySnapshot telemetry)
    {
        var engine = new AtcEngine(new AtisService());
        engine.Handle(ground,
            "Bron Sol F-GABC demande roulage pour tours de piste",
            "F-GABC", telemetry);
        engine.Handle(ground,
            "F-GABC je roule point d'attente A1 piste 16 QNH 1018",
            "F-GABC", telemetry);
        var handoff = engine.Handle(ground,
            "F-GABC je suis au point d'attente A1 prêt au départ",
            "F-GABC", telemetry);
        ExpectContains(handoff.Text, "Bron Tour",
            "Ground handoff must be issued before testing its readback.");
        return engine;
    }

    private static void VerifyAtisCorrectionAndReadbackErrors()
    {
        var atis = new AtisService();
        var telemetry = new TelemetrySnapshot
        {
            IsOnGround = true, WindDirectionTrueDeg = 160,
            WindSpeedKt = 4, SeaLevelPressureMb = 1018
        };
        var ground = new RadioStation(
            RadioStationKind.Ground, "Bron Sol", 121.705);
        var tower = new RadioStation(
            RadioStationKind.Tower, "Bron Tour", 118.100);
        string actual = atis.Build(telemetry).Information;
        string wrong = actual == "Echo" ? "Charlie" : "Echo";

        var engine = new AtcEngine(atis);
        AtcResponse incorrect = engine.Handle(
            ground, "Bron Sol F-GABC au parking demande roulage pour tours de piste information " + wrong,
            "F-GABC", telemetry);
        ExpectContains(incorrect.Text, "négatif, information " + actual,
            "Wrong ATIS information must be corrected at initial Ground contact.");
        Expect((incorrect.Text ?? "").IndexOf("roulez point", StringComparison.OrdinalIgnoreCase) < 0,
            "Do not grant a clearance following an explicitly incorrect ATIS letter.");

        var correct = engine.Handle(
            ground, "Bron Sol F-GABC demande roulage pour tours de piste information " + actual,
            "F-GABC", telemetry);
        ExpectContains(correct.Text, "roulez point",
            "Correct ATIS information allows normal Ground clearance.");

        AtcResponse wrongPoint = engine.Handle(
            ground, "F-GABC je roule point d'attente A2 piste 16 QNH 1018",
            "F-GABC", telemetry);
        ExpectContains(wrongPoint.Text, "négatif, point d'attente Alpha un",
            "Wrong A2 must be corrected to A1.");

        AtcResponse wrongPointSpoken = engine.Handle(
            ground, "F-GABC je roule point d'attente Alpha deux piste 16 QNH 1018",
            "F-GABC", telemetry);
        ExpectContains(wrongPointSpoken.Text, "négatif, point d'attente Alpha un",
            "Spoken Alpha deux must be corrected to A1.");

        AtcResponse wrongRunway = engine.Handle(
            ground, "F-GABC point d'attente A1 piste trois quatre QNH 1018",
            "F-GABC", telemetry);
        ExpectContains(wrongRunway.Text, "négatif, piste un six",
            "Wrong runway 34 must be corrected to 16.");

        AtcResponse wrongQnh = engine.Handle(
            ground, "F-GABC point d'attente A1 piste 16 QNH 1013",
            "F-GABC", telemetry);
        ExpectContains(wrongQnh.Text, "négatif, Q N H",
            "Wrong QNH must be explicitly corrected.");

        AtcResponse correctReadback = engine.Handle(
            ground, "F-GABC je roule point d'attente A1 piste 16 QNH 1018, et on vous rappelle prêt au départ",
            "F-GABC", telemetry);
        Expect(string.IsNullOrWhiteSpace(correctReadback.Text),
            "After correction, taxi readback must not produce Tower handoff.");

        AtcResponse arrival = engine.Handle(
            ground, "F-GABC je suis au point d'attente A1 prêt au départ",
            "F-GABC", telemetry);
        ExpectContains(arrival.Text, "Bron Tour",
            "Confirmed holding-point report triggers tower handoff.");

        var towerEngine = new AtcEngine(atis);
        var wrongTowerInfo = towerEngine.Handle(
            tower, "Bron Tour F-GABC au point d'attente A1 prêt information " + wrong,
            "F-GABC", telemetry);
        ExpectContains(wrongTowerInfo.Text, "information " + actual,
            "Wrong ATIS information must also be corrected on Tower.");

        var noAtisAnnounced = new AtcEngine(atis).Handle(
            ground, "Bron Sol F-GABC demande roulage pour tour de piste",
            "F-GABC", telemetry);
        ExpectContains(noAtisAnnounced.Text, "roulez point",
            "No explicitly wrong ATIS letter should not block normal request.");
    }

    private static void VerifyShortCallsignReadbacks()
    {
        var atis = new AtisService();
        var telemetry = new TelemetrySnapshot
        {
            IsOnGround = true, WindDirectionTrueDeg = 160,
            WindSpeedKt = 5, SeaLevelPressureMb = 1018
        };
        var ground = new RadioStation(
            RadioStationKind.Ground, "Bron Sol", 121.705);
        var engine = new AtcEngine(atis);
        engine.Handle(ground,
            "Bron Sol Fox Golf Alpha Bravo Charlie demande roulage",
            "F-GABC", telemetry);

        var wrongPoint = engine.Handle(ground,
            "Fox Bravo Charlie point d'attente A2 piste 16 QNH 1018",
            "F-GABC", telemetry);
        ExpectContains(wrongPoint.Text, "Fox Bravo Charlie",
            "Unambiguous abbreviated F-BC must be accepted and repeated in the reply.");

        var correctReadback = engine.Handle(ground,
            "Fox Bravo Charlie je roule point d'attente A1 piste 16 QNH 1018",
            "F-GABC", telemetry);
        Expect(string.IsNullOrWhiteSpace(correctReadback.Text),
            "Abbreviated callsign must not invalidate a readback.");

        var hold = engine.Handle(ground,
            "Fox Bravo Charlie au point d'attente A1 prêt au départ",
            "F-GABC", telemetry);
        ExpectContains(hold.Text, "Bron Tour",
            "Unambiguous short callsign must be accepted at holding point.");

        var shortFreq = engine.Handle(ground,
            "Fox Bravo Charlie cent dix huit virgule un au revoir",
            "F-GABC", telemetry);
        ExpectContains(shortFreq.Text, "Fox Bravo Charlie, au revoir",
            "Short callsign may close the frequency readback.");

        var ambiguous = new AtcEngine(atis);
        ambiguous.UpdateTraffic(new[]
        {
            new OhControl.Multiplayer.MultiplayerPlayerState { Callsign = "F-DABC" }
        });
        ambiguous.Handle(ground,
            "Bron Sol F-GABC demande roulage", "F-GABC", telemetry);
        var collision = ambiguous.Handle(ground,
            "Fox Bravo Charlie point d'attente A2 piste 16 QNH 1018",
            "F-GABC", telemetry);
        ExpectContains(collision.Text, "Fox Golf Alpha Bravo Charlie",
            "Do not shorten ATC callsigns when another pilot has the same suffix.");
    }

    private static void VerifyCrossFrequencyRunwaySeparation()
    {
        var tower = new RadioStation(
            RadioStationKind.Tower, "Bron Tour", 118.100);
        var telemetry = new TelemetrySnapshot
        {
            IsOnGround = true,
            LatitudeDeg = 45.729,
            LongitudeDeg = 4.9436,
            HeadingMagneticDeg = 163.39,
            WindDirectionTrueDeg = 160,
            WindSpeedKt = 4,
            SeaLevelPressureMb = 1018
        };
        var engine = new AtcEngine(new AtisService());
        engine.Handle(tower,
            "Bron Tour F-GABC au point d'attente A1 prêt",
            "F-GABC", telemetry);
        engine.Handle(tower,
            "F-GABC on s'aligne et on attend",
            "F-GABC", telemetry);
        engine.UpdateTraffic(new[] {
            new OhControl.Multiplayer.MultiplayerPlayerState
            {
                Callsign = "F-HXYZ",
                Com1ActiveMhz = 121.705, // OTHER pilot is on Ground.
                ActiveRunway = "16",
                CircuitPhase = "Runway",
                DistanceToThresholdMeters = 400
            }
        });
        var now = new DateTimeOffset(2026,10,16,12,0,0,TimeSpan.Zero);
        engine.ObserveAlignedForTakeoff("F-GABC",tower,telemetry,now);
        Expect(engine.ObserveAlignedForTakeoff("F-GABC",
            tower,telemetry,now.AddSeconds(6)) == null,
            "Do not clear runway occupied by another aircraft on Ground.");
        engine.UpdateTraffic(Array.Empty<OhControl.Multiplayer.MultiplayerPlayerState>());
        ExpectContains(engine.ObserveAlignedForTakeoff(
            "F-GABC",tower,telemetry,now.AddSeconds(6.5))?.Text,
            "autorisé décollage",
            "Once traffic clears, tower can authorize takeoff.");
    }

    private static void VerifyLineupReadbackVariationsAndPhysicalAlignment()
    {
        var ground = new RadioStation(RadioStationKind.Ground, "Bron Sol", 121.705);
        var tower = new RadioStation(RadioStationKind.Tower, "Bron Tour", 118.100);
        var telemetry = new TelemetrySnapshot {
            IsOnGround = true, WindDirectionTrueDeg = 160,
            WindSpeedKt = 4, SeaLevelPressureMb = 1018
        };
        string[] samples = {
            "Fox Bravo Charlie on s'aligne et on attend",
            "F-GABC on s aligne et on attend",
            "Fox Bravo Charlie nous nous alignons et attendons",
            "F-GABC piste un six on s'aligne et on attend",
            "F-GABC je m'aligne et j'attends",
            "F-GABC alignement piste seize et attente",
            "F-GABC on va s'aligner et attendre"
        };
        foreach (string sample in samples)
        {
            var engine = new AtcEngine(new AtisService());
            engine.Handle(tower,
                "Bron Tour F-GABC point d'attente A1 prêt au départ",
                "F-GABC", telemetry);
            AtcResponse response = engine.Handle(tower, sample, "F-GABC", telemetry);
            Expect(string.IsNullOrWhiteSpace(response.Text),
                "Lineup readback should be accepted: " + sample);
            Expect(engine.IsAwaitingRunwayAlignment("F-GABC"),
                "Airplane must wait for physical alignment.");
        }
        var geo = new AtcEngine(new AtisService());
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        geo.Handle(tower, "Bron Tour F-GABC point d'attente A1 prêt au départ",
            "F-GABC", telemetry);
        geo.Handle(tower, "F-GABC on s aligne et on attend", "F-GABC", telemetry);
        telemetry.LatitudeDeg = 45.7290000;
        telemetry.LongitudeDeg = 4.9436000;
        telemetry.HeadingMagneticDeg = 163.39;
        Expect(geo.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, now) == null, "Begin alignment watch.");
        telemetry.HeadingMagneticDeg = 90;
        geo.ObserveAlignedForTakeoff("F-GABC", tower, telemetry, now.AddSeconds(3));
        telemetry.HeadingMagneticDeg = 163.39;
        geo.ObserveAlignedForTakeoff("F-GABC", tower, telemetry, now.AddSeconds(4));
        Expect(geo.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, now.AddSeconds(8)) == null, "Reset on heading change.");
        ExpectContains(geo.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, now.AddSeconds(9.1))?.Text, "autorisé décollage",
            "Auto-clear after five continuous seconds.");
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

        // ATIS letters change every half-hour. Tests that are not testing
        // incorrect ATIS must announce the currently active information.
        string currentInformation = atis.Build(telemetry).Information;

        var naturalEngine =
            new AtcEngine(atis);

        AtcResponse naturalCircuitRequest =
            naturalEngine.Handle(
                ground,
                "Bron Sol bonjour F-GABC au parking demande une clairance pour faire des tours de piste information " + currentInformation,
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
                "Bron Sol F-GABC demande roulage tour de piste information " + currentInformation,
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

        Expect(!((combinedReady.Text ?? "").Contains("Bron Tour")),
            "Taxi readback combined with 'on est pret' is NOT proof of arrival at the hold.");

        AtcResponse combinedArrival =
            combinedReadyEngine.Handle(
                ground,
                "F-GABC je suis au point d'attente A1, prêt au départ",
                "F-GABC",
                telemetry);

        ExpectContains(
            combinedArrival.Text,
            "Bron Tour",
            "Explicit holding-point arrival must trigger the Tower handoff.");

        ExpectContains(
            combinedArrival.Text,
            "unité, unité, huit décimale unité",
            "Ground-to-Tower handoff must include 118.100 frequency.");

        ExpectContains(
            combinedArrival.Text,
            "au revoir",
            "True holding-point report handoff must end with au revoir.");

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

        Expect(lineUpReadback.ControllerFollowUpDelaySeconds == null,
            "No fixed delay without actual lineup on the runway.");

        telemetry.LatitudeDeg = 45.7290000;
        telemetry.LongitudeDeg = 4.9436000;
        telemetry.HeadingMagneticDeg = 163.39;
        telemetry.GroundSpeedKt = 0;
        telemetry.IsOnGround = true;
        var alignedAt = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        Expect(engine.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, alignedAt) == null, "Must first detect lineup.");
        Expect(engine.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, alignedAt.AddSeconds(4.9)) == null,
            "Cannot clear before five full seconds.");
        AtcResponse takeoff = engine.ObserveAlignedForTakeoff(
            "F-GABC", tower, telemetry, alignedAt.AddSeconds(5.1));
        ExpectContains(takeoff?.Text, "autorisé décollage",
            "Spontaneous clearance when runway aligned for five seconds.");
        Expect(engine.ObserveAlignedForTakeoff("F-GABC", tower,
            telemetry, alignedAt.AddSeconds(6)) == null,
            "No duplicate departure clearance.");

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
                "F-GABC unité deux unité décimale sept zéro cinq au revoir",
                "F-GABC",
                telemetry);

        ExpectContains(
            frequencyRelease.Text,
            "au revoir",
            "Tower handoff must close after the correct frequency readback.");

        AtcResponse afterwards =
            handoffEngine.Handle(
                tower,
                "F-GABC quitte la fréquence",
                "F-GABC",
                telemetry);

        Expect(string.IsNullOrWhiteSpace(afterwards.Text),
            "After a completed handoff Tower must not restart the conversation.");

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
