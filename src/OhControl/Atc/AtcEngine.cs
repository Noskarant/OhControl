using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OhControl.Lfly;
using OhControl.Multiplayer;
using OhControl.Radio;

namespace OhControl.Atc
{
    public sealed class AtcEngine
    {
        private enum TrainingState
        {
            Parked,
            Taxiing,
            HoldingPoint,
            LineUpAndWait,
            ClearedForTakeoff,
            Airborne,
            Downwind,
            Base,
            Final,
            ClearedToLand,
            Landed,
            Departed
        }

        private sealed class PendingReadback
        {
            public string Kind { get; set; }
            public string Runway { get; set; }
            public string HoldingPoint { get; set; }
            public int? Qnh { get; set; }
            public double? FrequencyMhz { get; set; }
            public RadioStationKind? SourceStationKind { get; set; }
            public bool ReminderIssued { get; set; }
        }

        private readonly AtisService _atisService;

        private readonly Dictionary<string, TrainingState> _states =
            new Dictionary<string, TrainingState>(
                StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PendingReadback> _pendingReadbacks =
            new Dictionary<string, PendingReadback>(
                StringComparer.OrdinalIgnoreCase);

        // A completed handoff stays closed until the pilot changes to the
        // other station or explicitly starts a new operational request.
        private readonly Dictionary<string, RadioStationKind>
            _completedFrequencyHandoffs =
                new Dictionary<string, RadioStationKind>(
                    StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, string> _lastAtcTransmissions =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

        private IReadOnlyList<MultiplayerPlayerState> _traffic =
            Array.Empty<MultiplayerPlayerState>();

        public AtcEngine(AtisService atisService)
        {
            _atisService = atisService;
        }

        public void UpdateTraffic(
            IReadOnlyList<MultiplayerPlayerState> traffic)
        {
            _traffic = traffic ?? Array.Empty<MultiplayerPlayerState>();
        }

        public AtcResponse Handle(
            RadioStation station,
            string transcript,
            string callsign,
            TelemetrySnapshot telemetry)
        {
            if (station == null)
            {
                return new AtcResponse
                {
                    Feedback = "Aucune station OhControl sur cette fréquence."
                };
            }

            if (station.Kind == RadioStationKind.Atis)
            {
                return new AtcResponse
                {
                    Feedback =
                        "L'ATIS est une fréquence d'écoute : aucune émission pilote attendue."
                };
            }

            string normalized = Normalize(transcript);
            string spokenCallsign = AviationCallsign.ToSpeech(callsign);
            string handoffKey = NormalizeCallsignKey(callsign);

            if (_completedFrequencyHandoffs.TryGetValue(
                handoffKey, out RadioStationKind previousStation))
            {
                if (previousStation != station.Kind ||
                    IsNewOperationalRequestAfterHandoff(
                        normalized, station.Kind))
                {
                    _completedFrequencyHandoffs.Remove(handoffKey);
                }
                else if (!IsRepeatRequest(normalized))
                {
                    return new AtcResponse
                    {
                        Feedback = "Changement de fréquence déjà confirmé. " +
                            "Passez sur la fréquence du nouveau contrôleur."
                    };
                }
            }
            AtisBroadcast atis = _atisService.Build(telemetry);

            AtcResponse atisCorrection = CheckAtisInformation(
                normalized, spokenCallsign, atis);
            if (atisCorrection != null)
            {
                RememberAtcTransmission(callsign, station, atisCorrection);
                return atisCorrection;
            }

            spokenCallsign = SelectReplyCallsign(
                callsign, normalized, spokenCallsign);

            AtcResponse repeat =
                TryHandleRepeatRequest(
                    callsign,
                    spokenCallsign,
                    normalized,
                    station);

            if (repeat != null)
            {
                return repeat;
            }

            AtcResponse readback =
                TryHandleReadback(
                    callsign,
                    spokenCallsign,
                    normalized,
                    atis,
                    telemetry,
                    station);

            if (readback != null)
            {
                RememberAtcTransmission(
                    callsign,
                    station,
                    readback);

                return readback;
            }

            AtcResponse courtesy =
                TryHandleCourtesyOrAcknowledgement(
                    spokenCallsign,
                    normalized);

            if (courtesy != null)
            {
                return courtesy;
            }

            AtcResponse response =
                station.Kind == RadioStationKind.Ground
                    ? HandleGround(
                        normalized,
                        callsign,
                        spokenCallsign,
                        atis)
                    : HandleTower(
                        normalized,
                        callsign,
                        spokenCallsign,
                        atis,
                        telemetry,
                        station.FrequencyMhz);

            RememberAtcTransmission(
                callsign,
                station,
                response);

            return response;
        }

        private static readonly HashSet<string> InformationCodes =
            new HashSet<string>(new[]
            {
                "alpha", "bravo", "charlie", "delta", "echo", "foxtrot",
                "golf", "hotel", "india", "juliett", "juliet", "kilo",
                "lima", "mike", "november", "oscar", "papa", "quebec",
                "romeo", "sierra", "tango", "uniform", "victor",
                "whiskey", "xray", "x ray", "yankee", "zulu"
            }, StringComparer.OrdinalIgnoreCase);

        private static AtcResponse CheckAtisInformation(
            string text, string spokenCallsign, AtisBroadcast currentAtis)
        {
            if (string.IsNullOrWhiteSpace(text) || currentAtis == null)
                return null;

            Match match = Regex.Match(text,
                @"\binformation\s+(?<letter>x\s+ray|[a-z]+)\b",
                RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            string reported = match.Groups["letter"].Value.Trim();
            if (!InformationCodes.Contains(reported)) return null;

            string actual = Normalize(currentAtis.Information);
            if (reported.Replace(" ", "") == actual.Replace(" ", "") ||
                (reported == "juliet" && actual == "juliett"))
                return null;

            return Speak(
                spokenCallsign + ", négatif, information " +
                currentAtis.Information + " en vigueur. Confirmez information " +
                currentAtis.Information + " avant de poursuivre.",
                "Information ATIS incorrecte : " + reported +
                " ; information en vigueur : " + currentAtis.Information + ".");
        }

        private string SelectReplyCallsign(
            string callsign, string pilotText, string fullSpeech)
        {
            string raw = new string((callsign ?? "").ToUpperInvariant()
                .Where(char.IsLetterOrDigit).ToArray());

            if (raw.Length < 4 ||
                (GetState(callsign) == TrainingState.Parked &&
                 !_pendingReadbacks.ContainsKey(NormalizeCallsignKey(callsign))))
                return fullSpeech;

            string shortCode = raw.Substring(0, 1) +
                raw.Substring(raw.Length - 2);
            if (_traffic.Any(traffic =>
            {
                string other = new string((traffic?.Callsign ?? "")
                    .ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
                return other.Length >= 4 &&
                    !string.Equals(other, raw, StringComparison.Ordinal) &&
                    other[0] == raw[0] &&
                    other.Substring(other.Length - 2) ==
                        raw.Substring(raw.Length - 2);
            }))
                return fullSpeech;

            string shortSpeech = AviationCallsign.ToSpeech(shortCode);
            string spokenPattern = @"(?<![a-z])" +
                Regex.Escape(Normalize(shortSpeech)).Replace(@"\ ", @"\s+") +
                @"(?![a-z])";
            string writtenShort = raw.Substring(0, 1).ToLowerInvariant() +
                raw.Substring(raw.Length - 2).ToLowerInvariant();

            if (Regex.IsMatch(pilotText, spokenPattern) ||
                Regex.IsMatch(pilotText,
                    @"\b" + Regex.Escape(writtenShort) + @"\b") ||
                Regex.IsMatch(pilotText,
                    @"\b" + raw.Substring(0, 1).ToLowerInvariant() +
                    @"\s+" + raw.Substring(raw.Length - 2).ToLowerInvariant() +
                    @"\b"))
                return shortSpeech;

            return fullSpeech;
        }

        private AtcResponse TryHandleRepeatRequest(
            string callsign,
            string spokenCallsign,
            string text,
            RadioStation station)
        {
            if (!IsRepeatRequest(text))
            {
                return null;
            }

            string key =
                LastTransmissionKey(
                    callsign,
                    station);

            if (!_lastAtcTransmissions.TryGetValue(
                    key,
                    out string previous) ||
                string.IsNullOrWhiteSpace(previous))
            {
                return Speak(
                    spokenCallsign +
                    ", aucune transmission précédente à répéter, transmettez vos intentions.",
                    "Demande de répétition reconnue, mais aucune transmission ATC précédente n'est disponible.");
            }

            string body =
                previous.Trim();

            string prefix =
                spokenCallsign + ",";

            if (body.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                body =
                    body.Substring(prefix.Length)
                        .Trim();
            }

            return Speak(
                spokenCallsign +
                ", je répète, " +
                body,
                "Dernière transmission ATC répétée à la demande du pilote.");
        }

        private void RememberAtcTransmission(
            string callsign,
            RadioStation station,
            AtcResponse response)
        {
            if (station == null ||
                response == null ||
                string.IsNullOrWhiteSpace(response.Text))
            {
                return;
            }

            _lastAtcTransmissions[
                LastTransmissionKey(
                    callsign,
                    station)] =
                response.Text.Trim();
        }

        private static string LastTransmissionKey(
            string callsign,
            RadioStation station)
        {
            return NormalizeCallsignKey(callsign) +
                   "|" +
                   (station?.Kind.ToString() ?? "Unknown");
        }

        private static bool IsRepeatRequest(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (ContainsAny(
                text,
                "repetez",
                "pouvez vous repeter",
                "pouvez repeter",
                "merci de repeter",
                "redites",
                "redire",
                "encore une fois",
                "repeter la derniere",
                "repetez la derniere"))
            {
                return true;
            }

            return text == "repete" ||
                   text == "repeter" ||
                   text.EndsWith(
                       " repete",
                       StringComparison.Ordinal);
        }

        private AtcResponse BuildFrequencyHandoff(
            string callsignKey,
            string spokenCallsign,
            string targetStation,
            double frequencyMhz,
            RadioStationKind sourceStation,
            string feedback)
        {
            SetPendingReadback(
                callsignKey,
                "frequency",
                null,
                null,
                null,
                frequencyMhz,
                sourceStation);

            return Speak(
                spokenCallsign +
                ", reçu, contactez " +
                targetStation +
                " " +
                AviationFrenchNumbers.Frequency(
                    frequencyMhz) +
                ", au revoir.",
                feedback);
        }

        private static bool IsReadyForDeparture(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool ready =
                ContainsAny(
                    text,
                    "pret",
                    "prêt",
                    "pret au depart",
                    "prêt au départ",
                    "sommes prets",
                    "sommes prêts",
                    "on est pret",
                    "on est prêt",
                    "ready");

            bool departureContext =
                ContainsAny(
                    text,
                    "depart",
                    "départ",
                    "point d attente",
                    "point d arret",
                    "point attente",
                    "attente");

            return ready &&
                   departureContext;
        }

        // A taxi clearance readback is NOT a report of reaching the hold.
        // Require a prior taxi clearance, an affirmative arrival/ready report
        // and no language indicating that the aircraft is still at parking
        // or travelling towards the holding point.
        private bool IsGroundHoldingPointReport(
            string text,
            string callsign)
        {
            TrainingState state = GetState(callsign);
            if (state != TrainingState.Taxiing &&
                state != TrainingState.HoldingPoint)
                return false;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Crucial: "on vous rappelle pret au depart" is an acknowledgement
            // of what the pilot WILL do, not a present position report.
            // Normalize() already strips accents and punctuation.
            bool futureCall = Regex.IsMatch(
                text,
                @"\b(?:rappel\w*|rappell\w*|rappelez|recontact\w*|previendr\w*)\b",
                RegexOptions.CultureInvariant);

            bool atParkingOrTaxiing = ContainsAny(
                text,
                "parking",
                "pas bouge",
                "pas encore",
                "encore au",
                "je roule",
                "on roule",
                "nous roulons",
                "roulons",
                "roulage",
                "je rejoins",
                "on rejoint",
                "nous rejoignons",
                "rejoindre",
                "en route vers",
                "vers le point",
                "jusqu au point",
                "en direction du point",
                "je vais",
                "on va",
                "quand je",
                "quand nous",
                "une fois au",
                "avant de",
                "je ne suis pas");

            if (futureCall || atParkingOrTaxiing)
                return false;

            // Transfer only when the pilot POSITIVELY reports being at
            // the holding point. Merely repeating "point d'attente" and
            // "pret au depart" from a taxi clearance is insufficient.
            bool confirmedPosition = Regex.IsMatch(
                text,
                @"\b(?:je suis|nous sommes|on est|sommes|je me trouve|nous voila|on se trouve|je suis arrive|nous sommes arrives|on est arrive|arrive|arrives|arrivons|en place|position)\s+(?:bien\s+)?au point d (?:attente|arret)\b",
                RegexOptions.CultureInvariant);

            // Short, standard VFR position report: "point d'attente
            // A1, pret au depart". Exclude QNH/runway readbacks to avoid
            // confusing a collation with arrival at the point.
            bool terseHoldingPointCall =
                !ContainsAny(text, "qnh", "q n h", "piste", "roulez") &&
                Regex.IsMatch(
                    text,
                    @"\bpoint d (?:attente|arret)\s+(?:[a-z]+\s+)?[a-z]?\d+\s+(?:je suis\s+|on est\s+|sommes\s+)?prets?(?:\s+au depart)?\b",
                    RegexOptions.CultureInvariant);

            return confirmedPosition || terseHoldingPointCall;
        }

        private AtcResponse HandleGround(
            string text,
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis)
        {
            if (IsGroundHoldingPointReport(text, callsignKey))
            {
                SetState(callsignKey, TrainingState.HoldingPoint);
                return BuildFrequencyHandoff(
                    callsignKey,
                    spokenCallsign,
                    "Bron Tour",
                    118.100,
                    RadioStationKind.Ground,
                    "Arrivée au point d'attente confirmée ; transfert vers Bron Tour.");
            }

            if (IsGroundDepartureRequest(text))
            {
                return BuildTaxiClearance(
                    callsignKey,
                    spokenCallsign,
                    atis);
            }

            if (IsInitialCall(text))
            {
                return Speak(
                    spokenCallsign + ", Bron Sol, bonjour, transmettez.",
                    "Premier contact reconnu.");
            }


            if (ContainsAny(
                text,
                "piste degagee",
                "piste dégagée",
                "degage",
                "dégagée"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Landed);

                return Speak(
                    spokenCallsign +
                    ", reçu, roulez au parking.",
                    "Piste dégagée reconnue.");
            }

            if (ContainsAny(
                text,
                "maintiens position",
                "maintiens position"))
            {
                return new AtcResponse
                {
                    Feedback = "Collationnement de maintien de position reconnu."
                };
            }

            return Speak(
                spokenCallsign + ", Bron Sol, transmettez vos intentions.",
                "Demande non classée sur la fréquence Sol.");
        }

        private AtcResponse BuildTaxiClearance(
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis)
        {
            var profile =
                LflyGroundProfile.ForRunway(
                    atis.Runway);

            SetState(
                callsignKey,
                TrainingState.Taxiing);

            SetPendingReadback(
                callsignKey,
                "taxi",
                atis.Runway,
                profile.FullLengthHoldingPoint,
                atis.Qnh);

            return Speak(
                spokenCallsign +
                ", roulez point d'attente " +
                AviationFrenchNumbers.Identifier(
                    profile.FullLengthHoldingPoint) +
                ", piste " +
                AviationFrenchNumbers.Runway(
                    atis.Runway) +
                ", Q N H " +
                ExtractQnhSpeech(
                    atis.Text) +
                ". Rappelez prêt au départ au point d'attente.",
                "Demande de départ reconnue. Roulage pleine longueur vers " +
                profile.FullLengthHoldingPoint +
                ".");
        }

        private static bool IsGroundDepartureRequest(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool taxiIntent =
                ContainsAny(
                    text,
                    "demande roulage",
                    "roulage",
                    "consignes de roulage",
                    "taxi",
                    "pret au roulage");

            if (taxiIntent)
            {
                return true;
            }

            bool circuitIntent =
                ContainsAny(
                    text,
                    "tour de piste",
                    "tours de piste",
                    "circuit",
                    "vol local",
                    "depart local");

            bool requestIntent =
                ContainsAny(
                    text,
                    "demande",
                    "voudrais",
                    "souhaite",
                    "souhaiterais",
                    "pour ",
                    "clairance",
                    "depart",
                    "faire",
                    "effectuer");

            return circuitIntent &&
                   requestIntent;
        }

        private AtcResponse HandleTower(
            string text,
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis,
            TelemetrySnapshot telemetry,
            double towerFrequency)
        {
            if (IsInitialCall(text))
            {
                return Speak(
                    spokenCallsign + ", Bron Tour, bonjour, transmettez.",
                    "Premier contact Tour reconnu.");
            }

            LflyReportingPoint reportingPoint =
                LflyReportingPoints.FindInTranscript(text);

            if (reportingPoint != null &&
                ContainsAny(
                    text,
                    "arrivee",
                    "arrivée",
                    "inbound",
                    "integration",
                    "intégration",
                    "pour bron",
                    "a destination"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Airborne);

                MultiplayerPlayerState traffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Downwind",
                        "Base",
                        "Final");

                string trafficText =
                    traffic == null
                        ? ""
                        : " Trafic connu " +
                          AviationCallsign.ToSpeech(
                              traffic.Callsign) +
                          " " +
                          PhaseForSpeech(
                              traffic.CircuitPhase) +
                          ".";

                return Speak(
                    spokenCallsign +
                    ", " +
                    reportingPoint.SpokenCode +
                    " reçu, intégrez vent arrière piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    "." +
                    trafficText +
                    " Rappelez vent arrière.",
                    BuildReportingPointFeedback(
                        reportingPoint,
                        telemetry));
            }

            if (ContainsAny(
                text,
                "integration",
                "intégration",
                "integrer",
                "intégrer"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Airborne);

                return BuildIntegrationResponse(
                    spokenCallsign,
                    atis,
                    towerFrequency);
            }

            if (ContainsAny(
                text,
                "pret",
                "prêt",
                "point d attente",
                "point attente") &&
                GetState(callsignKey) !=
                TrainingState.LineUpAndWait)
            {
                var ground =
                    LflyGroundProfile.ForRunway(
                        atis.Runway);

                MultiplayerPlayerState blockingTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Final",
                        "Runway");

                if (blockingTraffic != null)
                {
                    SetState(
                        callsignKey,
                        TrainingState.HoldingPoint);

                    return Speak(
                        spokenCallsign +
                        ", maintenez avant point d'attente " +
                        AviationFrenchNumbers.Identifier(
                            ground.FullLengthHoldingPoint) +
                        ", trafic " +
                        AviationCallsign.ToSpeech(
                            blockingTraffic.Callsign) +
                        " " +
                        PhaseForSpeech(
                            blockingTraffic.CircuitPhase) +
                        ".",
                        "Départ retenu à cause du trafic connecté.");
                }

                SetState(
                    callsignKey,
                    TrainingState.LineUpAndWait);

                SetPendingReadback(
                    callsignKey,
                    "lineup_wait",
                    atis.Runway,
                    ground.FullLengthHoldingPoint);

                return Speak(
                    spokenCallsign +
                    ", piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ", alignez-vous et attendez.",
                    "« Alignez-vous et attendez » est traité comme une instruction indivisible.");
            }

            if (ContainsAny(
                text,
                "vent arriere",
                "vent arrière"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Downwind);

                MultiplayerPlayerState finalTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Final");

                if (finalTraffic != null)
                {
                    return Speak(
                        spokenCallsign +
                        ", numéro deux derrière " +
                        AviationCallsign.ToSpeech(
                            finalTraffic.Callsign) +
                        " en finale, rappelez finale piste " +
                        AviationFrenchNumbers.Runway(
                            atis.Runway) +
                        ".",
                        "Séquence trafic multijoueur : numéro deux.");
                }

                MultiplayerPlayerState downwindTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Downwind",
                        "Base");

                if (downwindTraffic != null)
                {
                    return Speak(
                        spokenCallsign +
                        ", numéro deux derrière " +
                        AviationCallsign.ToSpeech(
                            downwindTraffic.Callsign) +
                        ", rappelez finale piste " +
                        AviationFrenchNumbers.Runway(
                            atis.Runway) +
                        ".",
                        "L'autre avion est déjà dans le circuit.");
                }

                return Speak(
                    spokenCallsign +
                    ", numéro un, poursuivez, rappelez finale piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ".",
                    "Report vent arrière reconnu.");
            }

            if (ContainsAny(
                text,
                "etape de base",
                "étape de base",
                "en base",
                "base"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Base);

                return Speak(
                    spokenCallsign +
                    ", poursuivez, rappelez finale piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ".",
                    "Report étape de base reconnu.");
            }

            if (ContainsAny(
                text,
                "remise de gaz",
                "remets les gaz",
                "remet les gaz",
                "go around"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Airborne);

                return Speak(
                    spokenCallsign +
                    ", remise de gaz reçue, rappelez vent arrière piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ".",
                    "Remise de gaz reconnue ; le vent n'est volontairement pas répété dans cette instruction.");
            }

            if (ContainsAny(
                text,
                "finale",
                "final"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Final);

                MultiplayerPlayerState runwayTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Runway");

                if (runwayTraffic != null)
                {
                    return Speak(
                        spokenCallsign +
                        ", poursuivez approche, trafic " +
                        AviationCallsign.ToSpeech(
                            runwayTraffic.Callsign) +
                        " sur la piste, rappelez courte finale.",
                        "Aucune autorisation d'atterrissage tant que la piste est détectée occupée.");
                }

                bool touchAndGo =
                    ContainsAny(
                        text,
                        "toucher",
                        "touch and go",
                        "touché décollé",
                        "touche decoller");

                if (touchAndGo)
                {
                    SetPendingReadback(
                        callsignKey,
                        "touch",
                        atis.Runway,
                        null);

                    return Speak(
                        spokenCallsign +
                        ", piste " +
                        AviationFrenchNumbers.Runway(
                            atis.Runway) +
                        ", autorisé toucher, vent " +
                        WindSpeech(telemetry) +
                        ".",
                        "Demande toucher reconnue.");
                }

                SetState(
                    callsignKey,
                    TrainingState.ClearedToLand);

                SetPendingReadback(
                    callsignKey,
                    "land",
                    atis.Runway,
                    null);

                return Speak(
                    spokenCallsign +
                    ", piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ", autorisé atterrissage, vent " +
                    WindSpeech(telemetry) +
                    ".",
                    "Clairance d'atterrissage délivrée.");
            }

            if (ContainsAny(
                text,
                "sortie de circuit",
                "quitte la frequence",
                "quitte la fréquence",
                "quittant la frequence",
                "quittant la fréquence"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Departed);

                return Speak(
                    spokenCallsign +
                    ", quittez la fréquence, au revoir.",
                    "Clôture des communications en sortie de circuit.");
            }

            if (ContainsAny(
                text,
                "piste degagee",
                "piste dégagée",
                "degage",
                "dégagée"))
            {
                SetState(
                    callsignKey,
                    TrainingState.Landed);

                return BuildFrequencyHandoff(
                    callsignKey,
                    spokenCallsign,
                    "Bron Sol",
                    121.705,
                    RadioStationKind.Tower,
                    "Piste dégagée reconnue ; transfert vers Bron Sol.");
            }

            return Speak(
                spokenCallsign +
                ", Bron Tour, transmettez vos intentions.",
                "Transmission comprise, mais scénario non classé.");
        }

        public AtcResponse BuildScheduledFollowUp(
            string callsign,
            RadioStation station,
            TelemetrySnapshot telemetry)
        {
            if (station == null ||
                station.Kind != RadioStationKind.Tower ||
                GetState(callsign) !=
                TrainingState.LineUpAndWait)
            {
                return null;
            }

            AtisBroadcast atis =
                _atisService.Build(telemetry);

            MultiplayerPlayerState blockingTraffic =
                FindTraffic(
                    atis.Runway,
                    station.FrequencyMhz,
                    "Final",
                    "Runway");

            string spokenCallsign =
                AviationCallsign.ToSpeech(callsign);

            if (blockingTraffic != null &&
                blockingTraffic.DistanceToThresholdMeters <
                3500)
            {
                AtcResponse hold =
                    Speak(
                        spokenCallsign +
                        ", maintenez position, trafic " +
                        AviationCallsign.ToSpeech(
                            blockingTraffic.Callsign) +
                        " en finale.",
                        "Départ toujours retenu pour trafic ; nouvel appel contrôleur programmé.");

                hold.ControllerFollowUpDelaySeconds = 12;

                RememberAtcTransmission(
                    callsign,
                    station,
                    hold);

                return hold;
            }

            SetState(
                callsign,
                TrainingState.ClearedForTakeoff);

            SetPendingReadback(
                callsign,
                "takeoff",
                atis.Runway,
                null);

            AtcResponse clearance =
                Speak(
                    spokenCallsign +
                    ", piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ", autorisé décollage, vent " +
                    WindSpeech(telemetry) +
                    ".",
                    "Clairance de décollage émise automatiquement après l'attente.");

            RememberAtcTransmission(
                callsign,
                station,
                clearance);

            return clearance;
        }

        private AtcResponse TryHandleReadback(
            string callsignKey,
            string spokenCallsign,
            string text,
            AtisBroadcast atis,
            TelemetrySnapshot telemetry,
            RadioStation station)
        {
            string pendingKey =
                NormalizeCallsignKey(
                    callsignKey);

            if (!_pendingReadbacks.TryGetValue(
                pendingKey,
                out PendingReadback pending))
            {
                return null;
            }

            if (pending.Kind == "frequency" &&
                pending.SourceStationKind.HasValue &&
                station != null &&
                station.Kind !=
                pending.SourceStationKind.Value)
            {
                _pendingReadbacks.Remove(
                    pendingKey);

                return null;
            }

            if (pending.Kind == "frequency")
            {
                bool frequencyOk =
                    pending.FrequencyMhz.HasValue &&
                    ContainsFrequency(text, pending.FrequencyMhz.Value);

                if (frequencyOk)
                {
                    _pendingReadbacks.Remove(pendingKey);
                    _completedFrequencyHandoffs[pendingKey] =
                        pending.SourceStationKind ?? station.Kind;

                    string feedback =
                        "Fréquence correctement collationnée : " +
                        pending.FrequencyMhz.Value.ToString(
                            "000.000", CultureInfo.InvariantCulture) +
                        " MHz. Contactez le nouveau contrôleur.";

                    // The original instruction already ended with goodbye.
                    // Do not repeat the whole frequency handoff.
                    if (IsGoodbye(text))
                    {
                        return Speak(spokenCallsign + ", au revoir.", feedback);
                    }

                    return new AtcResponse { Feedback = feedback };
                }

                // Stay in this specific dialogue state. A transcription
                // like "cent dix-huit virgule un" or "passe sur la tour"
                // must NEVER fall through to generic "transmettez".
                if (!pending.ReminderIssued &&
                    (IsAcknowledgementOnly(text) ||
                     LooksLikeFrequencyReadback(text) ||
                     IsGoodbye(text) ||
                     LooksLikeTransferAcknowledgement(text)))
                {
                    pending.ReminderIssued = true;
                    return Speak(
                        spokenCallsign +
                        ", collationnez la fréquence " +
                        AviationFrenchNumbers.Frequency(
                            pending.FrequencyMhz ?? 0) +
                        ".",
                        "Collationnement de changement de fréquence incomplet.");
                }

                return new AtcResponse
                {
                    Feedback = "Transfert de fréquence en attente : " +
                        "collationnez la fréquence annoncée, puis changez de station."
                };
            }

            if (pending.Kind == "taxi")
            {
                bool holdingPointOk =
                    ContainsPoint(
                        text,
                        pending.HoldingPoint);

                bool runwayOk =
                    ContainsRunway(
                        text,
                        pending.Runway);

                bool qnhOk =
                    !pending.Qnh.HasValue ||
                    ContainsQnh(
                        text,
                        pending.Qnh.Value);

                bool taxiActionOk =
                    ContainsAny(
                        text, "roule", "roulons",
                        "point d attente", "point d arret", "point attente");

                string reportedPoint = FindMentionedHoldingPoint(text);
                if (reportedPoint != null &&
                    reportedPoint != pending.HoldingPoint)
                    return Speak(spokenCallsign +
                        ", négatif, point d'attente " +
                        AviationFrenchNumbers.Identifier(pending.HoldingPoint) +
                        ". Collationnez le point d'attente correct.",
                        "Point d'attente incorrect : " + reportedPoint +
                        ". Attendu : " + pending.HoldingPoint + ".");

                string reportedRunway = FindMentionedRunway(text);
                if (reportedRunway != null && reportedRunway != pending.Runway)
                    return Speak(spokenCallsign + ", négatif, piste " +
                        AviationFrenchNumbers.Runway(pending.Runway) +
                        ". Collationnez la piste correcte.",
                        "Piste incorrecte : " + reportedRunway +
                        ". Attendue : " + pending.Runway + ".");

                int? reportedQnh = FindMentionedQnh(text);
                if (pending.Qnh.HasValue && reportedQnh.HasValue &&
                    reportedQnh.Value != pending.Qnh.Value)
                    return Speak(spokenCallsign + ", négatif, Q N H " +
                        AviationFrenchNumbers.DigitsOnly(pending.Qnh.Value) +
                        ". Collationnez le Q N H correct.",
                        "QNH incorrect : " + reportedQnh.Value +
                        ". Attendu : " + pending.Qnh.Value + ".");

                if (holdingPointOk &&
                    runwayOk &&
                    qnhOk &&
                    taxiActionOk)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    string feedback =
                        "Collationnement roulage correct : " +
                        pending.HoldingPoint +
                        " / piste " +
                        pending.Runway +
                        (pending.Qnh.HasValue
                            ? " / QNH " +
                              pending.Qnh.Value
                            : "") +
                        ".";

                    // A mandatory taxi clearance readback completes the
                    // readback, never a Tower handoff. The pilot must later
                    // report being physically at the holding point.
                    return new AtcResponse
                    {
                        Feedback = feedback
                    };
                }

                if (IsAcknowledgementOnly(text))
                {
                    return BuildMandatoryReadbackReminder(
                        spokenCallsign,
                        pending);
                }

                if (LooksLikeReadback(text))
                {
                    return RepeatTaxiClearance(
                        spokenCallsign,
                        pending);
                }

                return null;
            }

            // Mandatory runway clearances must reject a wrong runway,
            // rather than falling back to an unspecific repeat request.
            if (pending.Kind == "lineup_wait" ||
                pending.Kind == "takeoff" ||
                pending.Kind == "land" ||
                pending.Kind == "touch")
            {
                string reportedRunway = FindMentionedRunway(text);
                if (reportedRunway != null && pending.Runway != null &&
                    reportedRunway != pending.Runway)
                    return Speak(spokenCallsign + ", négatif, piste " +
                        AviationFrenchNumbers.Runway(pending.Runway) +
                        ". Collationnez la piste correcte.",
                        "Piste incorrecte : " + reportedRunway +
                        ". Attendue : " + pending.Runway + ".");
            }

            if (pending.Kind == "lineup_wait")
            {
                bool runwayMentioned =
                    ContainsAny(
                        text,
                        "piste",
                        "runway");

                bool runwayOk =
                    !runwayMentioned ||
                    ContainsRunway(
                        text,
                        pending.Runway);

                bool alignmentOk =
                    ContainsAny(
                        text,
                        "m aligne",
                        "on s aligne",
                        "nous nous alignons",
                        "alignons",
                        "aligne");

                bool waitOk =
                    ContainsAny(
                        text,
                        "on attend",
                        "attend",
                        "attends",
                        "attendons",
                        "j attends");

                if (!runwayOk ||
                    !alignmentOk ||
                    !waitOk)
                {
                    if (IsAcknowledgementOnly(text) ||
                        LooksLikeReadback(text))
                    {
                        return BuildMandatoryReadbackReminder(
                            spokenCallsign,
                            pending);
                    }

                    return null;
                }

                _pendingReadbacks.Remove(
                    pendingKey);

                SetState(
                    callsignKey,
                    TrainingState.LineUpAndWait);

                return new AtcResponse
                {
                    Feedback =
                        "Alignement et attente correctement collationnés. Le contrôleur vous rappellera.",
                    ControllerFollowUpDelaySeconds = 18
                };
            }

            if (pending.Kind == "takeoff")
            {
                bool correct =
                    ContainsRunway(
                        text,
                        pending.Runway) &&
                    ContainsAny(
                        text,
                        "je decolle",
                        "decolle",
                        "nous decollons");

                if (correct)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    SetState(
                        callsignKey,
                        TrainingState.Airborne);

                    return new AtcResponse
                    {
                        Feedback =
                            "Collationnement décollage correct : piste " +
                            pending.Runway +
                            ", je décolle."
                    };
                }

                if (IsAcknowledgementOnly(text) ||
                    LooksLikeReadback(text))
                {
                    return BuildMandatoryReadbackReminder(
                        spokenCallsign,
                        pending);
                }

                return null;
            }

            if (pending.Kind == "land")
            {
                bool correct =
                    ContainsRunway(
                        text,
                        pending.Runway) &&
                    ContainsAny(
                        text,
                        "j atterris",
                        "atterris",
                        "atterrissons");

                if (correct)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    return new AtcResponse
                    {
                        Feedback =
                            "Collationnement atterrissage correct : piste " +
                            pending.Runway +
                            ", j'atterris."
                    };
                }

                if (IsAcknowledgementOnly(text) ||
                    LooksLikeReadback(text))
                {
                    return BuildMandatoryReadbackReminder(
                        spokenCallsign,
                        pending);
                }

                return null;
            }

            if (pending.Kind == "touch")
            {
                bool correct =
                    ContainsRunway(
                        text,
                        pending.Runway) &&
                    ContainsAny(
                        text,
                        "touche",
                        "toucher");

                if (correct)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    return new AtcResponse
                    {
                        Feedback =
                            "Collationnement toucher correct."
                    };
                }

                if (IsAcknowledgementOnly(text) ||
                    LooksLikeReadback(text))
                {
                    return BuildMandatoryReadbackReminder(
                        spokenCallsign,
                        pending);
                }

                return null;
            }

            return null;
        }

        private AtcResponse TryHandleCourtesyOrAcknowledgement(
            string spokenCallsign,
            string text)
        {
            if (IsGoodbye(text))
            {
                return Speak(
                    spokenCallsign +
                    ", au revoir.",
                    "Fin d'échange reconnue.");
            }

            if (IsReportAcknowledgement(text))
            {
                return new AtcResponse
                {
                    Feedback =
                        "Instruction de rappel correctement accusée."
                };
            }

            if (IsAcknowledgementOnly(text))
            {
                return new AtcResponse
                {
                    Feedback =
                        "Accusé de réception reconnu."
                };
            }

            return null;
        }

        private static bool IsGoodbye(string text)
        {
            return ContainsAny(
                text,
                "au revoir",
                "bonne journee",
                "bonne soiree",
                "a bientot");
        }

        private static bool IsReportAcknowledgement(
            string text)
        {
            return ContainsAny(
                text,
                "je rappelle finale",
                "on rappelle finale",
                "nous rappelons finale",
                "rappellerai finale",
                "je rappelle vent arriere",
                "on rappelle vent arriere",
                "nous rappelons vent arriere",
                "rappellerai vent arriere",
                "je rappelle courte finale",
                "on rappelle courte finale");
        }

        private static bool IsAcknowledgementOnly(
            string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            bool acknowledgement =
                ContainsAny(
                    text,
                    "recu",
                    "bien recu",
                    "compris",
                    "bien compris",
                    "roger",
                    "wilco",
                    "d accord",
                    "merci");

            bool operationalContent =
                ContainsAny(
                    text,
                    "roule",
                    "point d attente",
                    "piste",
                    "aligne",
                    "attends",
                    "decolle",
                    "atterris",
                    "touche",
                    "frequence",
                    "decimale") ||
                text.Any(char.IsDigit);

            return acknowledgement &&
                   !operationalContent;
        }

        private static bool IsNewOperationalRequestAfterHandoff(
            string text,
            RadioStationKind station)
        {
            return station == RadioStationKind.Ground &&
                   (IsGroundDepartureRequest(text) ||
                    ContainsAny(text, "piste degagee", "retour parking"));
        }

        private static bool LooksLikeTransferAcknowledgement(string text)
        {
            return ContainsAny(text,
                "tour",
                "sol",
                "contact",
                "je passe",
                "on passe",
                "nous passons",
                "je bascule",
                "on bascule",
                "bien recu",
                "recu",
                "merci",
                "au revoir");
        }

        private static bool LooksLikeFrequencyReadback(
            string text)
        {
            return ContainsAny(text,
                       "frequence",
                       "decimale",
                       "virgule",
                       "contacte",
                       "contactons",
                       "cent dix huit",
                       "cent vingt et un",
                       "unite unite huit") ||
                   (text ?? "").Any(char.IsDigit);
        }

        private static bool ContainsFrequency(
            string text,
            double frequencyMhz)
        {
            string normalized = Normalize(text);
            string compact = normalized.Replace(" ", "");

            string formatted = frequencyMhz.ToString(
                "000.000", CultureInfo.InvariantCulture);
            string[] parts = formatted.Split('.');
            string rawDigits = parts[0] + parts[1];
            string shortDigits =
                parts[1].EndsWith("00", StringComparison.Ordinal)
                    ? parts[0] + parts[1].Substring(0, 1)
                    : rawDigits;

            string officialSpeech = Normalize(
                AviationFrenchNumbers.Frequency(frequencyMhz));
            string spokenUnits = officialSpeech.Replace("unite", "un");

            // Numeric speech-to-text, e.g. "118.100", "118,1",
            // "118 100", "118 1".
            if (Regex.IsMatch(normalized,
                @"(?<!\d)" + parts[0] +
                @"\s+(?:" + parts[1] +
                (shortDigits != rawDigits
                    ? "|" + parts[1].Substring(0, 1)
                    : "") +
                @")(?!\d)", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(normalized,
                    @"(?<!\d)(?:" + rawDigits + "|" +
                    shortDigits + @")(?!\d)",
                    RegexOptions.CultureInvariant))
            {
                return true;
            }

            if (compact.Contains(officialSpeech.Replace(" ", "")) ||
                compact.Contains(spokenUnits.Replace(" ", "")))
            {
                return true;
            }

            // ASR usually renders digits as common French speech rather
            // than ICAO "unité" : "un un huit décimale un",
            // "cent dix-huit virgule un", etc.
            if (Math.Abs(frequencyMhz - 118.100) < 0.0005)
            {
                return Regex.IsMatch(
                    normalized,
                    @"\b(?:un(?:ite)?\s+un(?:ite)?\s+huit|" +
                    @"cent\s+dix\s+huit|un\s+dix\s+huit)" +
                    @"\s+(?:decimale\s+|virgule\s+|point\s+)?" +
                    @"(?:un|unite)\b",
                    RegexOptions.CultureInvariant);
            }

            if (Math.Abs(frequencyMhz - 121.705) < 0.0005)
            {
                return Regex.IsMatch(
                    normalized,
                    @"\b(?:un(?:ite)?\s+deux\s+un(?:ite)?|" +
                    @"cent\s+vingt(?:\s+et)?\s+un)" +
                    @"\s+(?:decimale\s+|virgule\s+|point\s+)?" +
                    @"sept\s+zero\s+cinq\b",
                    RegexOptions.CultureInvariant);
            }

            return false;
        }

        private static bool ContainsQnh(
            string text,
            int qnh)
        {
            string compact =
                Normalize(text)
                    .Replace(
                        " ",
                        "");

            string digits =
                qnh.ToString(
                    CultureInfo.InvariantCulture);

            string spoken =
                Normalize(
                    AviationFrenchNumbers.DigitsOnly(
                        qnh))
                    .Replace(
                        " ",
                        "");

            string unitSpeech =
                string.Concat(
                    digits.Select(
                        c =>
                            c == '1'
                                ? "unite"
                                : AviationFrenchNumbers
                                    .DigitsOnly(
                                        c - '0')
                                    .Replace(
                                        " ",
                                        "")));

            return compact.Contains(digits) ||
                   compact.Contains(spoken) ||
                   compact.Contains(unitSpeech);
        }

        private static AtcResponse BuildMandatoryReadbackReminder(
            string spokenCallsign,
            PendingReadback pending)
        {
            if (pending.Kind == "taxi")
            {
                string qnh =
                    pending.Qnh.HasValue
                        ? ", Q N H " +
                          AviationFrenchNumbers.DigitsOnly(
                              pending.Qnh.Value)
                        : "";

                return Speak(
                    spokenCallsign +
                    ", collationnez point d'attente " +
                    AviationFrenchNumbers.Identifier(
                        pending.HoldingPoint) +
                    ", piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    qnh +
                    ".",
                    "Collationnement obligatoire incomplet.");
            }

            if (pending.Kind == "lineup_wait")
            {
                return Speak(
                    spokenCallsign +
                    ", collationnez, piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    ", alignez-vous et attendez.",
                    "Collationnement alignement/attente obligatoire.");
            }

            if (pending.Kind == "takeoff")
            {
                return Speak(
                    spokenCallsign +
                    ", collationnez, piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    ", je décolle.",
                    "La clairance de décollage doit être collationnée.");
            }

            if (pending.Kind == "land")
            {
                return Speak(
                    spokenCallsign +
                    ", collationnez, piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    ", j'atterris.",
                    "La clairance d'atterrissage doit être collationnée.");
            }

            if (pending.Kind == "touch")
            {
                return Speak(
                    spokenCallsign +
                    ", collationnez, piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    ", toucher.",
                    "La clairance de toucher doit être collationnée.");
            }

            if (pending.Kind == "frequency" &&
                pending.FrequencyMhz.HasValue)
            {
                return Speak(
                    spokenCallsign +
                    ", je répète, contactez " +
                    StationNameForFrequency(
                        pending.FrequencyMhz) +
                    " " +
                    AviationFrenchNumbers.Frequency(
                        pending.FrequencyMhz.Value) +
                    ", au revoir.",
                    "Le changement de fréquence doit être collationné.");
            }

            return Speak(
                spokenCallsign +
                ", collationnez.",
                "Collationnement obligatoire.");
        }

        private static string StationNameForFrequency(
            double? frequencyMhz)
        {
            if (!frequencyMhz.HasValue)
            {
                return "la fréquence";
            }

            if (Math.Abs(
                    frequencyMhz.Value -
                    118.100) <= 0.006)
            {
                return "Bron Tour";
            }

            if (Math.Abs(
                    frequencyMhz.Value -
                    121.705) <= 0.006)
            {
                return "Bron Sol";
            }

            return "la fréquence";
        }

        private static string BuildReportingPointFeedback(
            LflyReportingPoint reported,
            TelemetrySnapshot telemetry)
        {
            if (reported == null ||
                telemetry == null)
            {
                return "Point de compte rendu reconnu.";
            }

            LflyReportingPointMatch nearest =
                LflyReportingPoints.FindNearest(
                    telemetry.LatitudeDeg,
                    telemetry.LongitudeDeg,
                    1.2);

            if (nearest == null)
            {
                return "Point " +
                       reported.Code +
                       " annoncé, mais la télémétrie n'est pas à moins de 1,2 NM d'un point publié.";
            }

            if (!string.Equals(
                nearest.Point.Code,
                reported.Code,
                StringComparison.OrdinalIgnoreCase))
            {
                return "Attention : point " +
                       reported.Code +
                       " annoncé, position détectée près de " +
                       nearest.Point.Code +
                       " (" +
                       nearest.DistanceNm.ToString("F1") +
                       " NM).";
            }

            return "Point " +
                   reported.Code +
                   " cohérent avec la position (" +
                   nearest.DistanceNm.ToString("F1") +
                   " NM).";
        }

        private AtcResponse BuildIntegrationResponse(
            string spokenCallsign,
            AtisBroadcast atis,
            double towerFrequency)
        {
            MultiplayerPlayerState traffic =
                FindTraffic(
                    atis.Runway,
                    towerFrequency,
                    "Downwind",
                    "Base",
                    "Final");

            string trafficText =
                traffic == null
                    ? ""
                    : " Trafic connu " +
                      AviationCallsign.ToSpeech(
                          traffic.Callsign) +
                      " " +
                      PhaseForSpeech(
                          traffic.CircuitPhase) +
                      ".";

            return Speak(
                spokenCallsign +
                ", intégrez vent arrière piste " +
                AviationFrenchNumbers.Runway(
                    atis.Runway) +
                "." +
                trafficText +
                " Rappelez vent arrière.",
                traffic == null
                    ? "Instruction d'intégration reconnue."
                    : "Intégration adaptée au trafic multijoueur.");
        }

        private AtcResponse RepeatTaxiClearance(
            string spokenCallsign,
            PendingReadback pending)
        {
            string qnhText =
                pending.Qnh.HasValue
                    ? ", Q N H " +
                      AviationFrenchNumbers.DigitsOnly(
                          pending.Qnh.Value)
                    : "";

            return Speak(
                spokenCallsign +
                ", je répète, roulez point d'attente " +
                AviationFrenchNumbers.Identifier(
                    pending.HoldingPoint) +
                ", piste " +
                AviationFrenchNumbers.Runway(
                    pending.Runway) +
                qnhText +
                ".",
                "Collationnement roulage incomplet : reprends le point d'attente, la piste et le QNH.");
        }

        private MultiplayerPlayerState FindTraffic(
            string runway,
            double frequencyMhz,
            params string[] phases)
        {
            return _traffic
                .Where(
                    p =>
                        p != null &&
                        !string.IsNullOrWhiteSpace(
                            p.Callsign) &&
                        Math.Abs(
                            p.Com1ActiveMhz -
                            frequencyMhz) <=
                        0.006 &&
                        (string.IsNullOrWhiteSpace(
                             p.ActiveRunway) ||
                         p.ActiveRunway ==
                         runway) &&
                        phases.Any(
                            phase =>
                                string.Equals(
                                    phase,
                                    p.CircuitPhase,
                                    StringComparison.OrdinalIgnoreCase)))
                .OrderBy(
                    p =>
                        p.DistanceToThresholdMeters)
                .FirstOrDefault();
        }

        private TrainingState GetState(
            string callsign)
        {
            string key =
                NormalizeCallsignKey(callsign);

            return _states.TryGetValue(
                key,
                out TrainingState state)
                ? state
                : TrainingState.Parked;
        }

        private void SetState(
            string callsign,
            TrainingState state)
        {
            _states[
                NormalizeCallsignKey(
                    callsign)] = state;
        }

        private void SetPendingReadback(
            string callsign,
            string kind,
            string runway,
            string holdingPoint,
            int? qnh = null,
            double? frequencyMhz = null,
            RadioStationKind? sourceStationKind = null)
        {
            _pendingReadbacks[
                NormalizeCallsignKey(
                    callsign)] =
                new PendingReadback
                {
                    Kind = kind,
                    Runway = runway,
                    HoldingPoint = holdingPoint,
                    Qnh = qnh,
                    FrequencyMhz = frequencyMhz,
                    SourceStationKind = sourceStationKind
                };
        }

        private static string NormalizeCallsignKey(
            string callsign)
        {
            return string.IsNullOrWhiteSpace(
                callsign)
                ? "UNKNOWN"
                : callsign.Trim();
        }

        private static string FindMentionedHoldingPoint(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match match = Regex.Match(text,
                @"\b(?:point\s+d\s+(?:attente|arret)\s+)?(?:a|alpha)\s*" +
                @"(?<number>[1-4]|un|une|deux|trois|quatre)\b",
                RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            switch (match.Groups["number"].Value)
            {
                case "1": case "un": case "une": return "A1";
                case "2": case "deux": return "A2";
                case "3": case "trois": return "A3";
                case "4": case "quatre": return "A4";
                default: return null;
            }
        }

        private static string FindMentionedRunway(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match match = Regex.Match(text,
                @"\bpiste\s+(?<runway>1\s*6|3\s*4|un\s+six|trois\s+quatre|seize|trente\s+quatre)\b",
                RegexOptions.CultureInvariant);
            if (!match.Success) return null;
            string value = match.Groups["runway"].Value.Replace(" ", "");
            return value == "16" || value == "unsix" || value == "seize"
                ? "16" : "34";
        }

        private static int? FindMentionedQnh(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            Match match = Regex.Match(text,
                @"\bq\s*n\s*h\s+(?<value>\d{3,4})\b",
                RegexOptions.CultureInvariant);
            if (match.Success &&
                int.TryParse(match.Groups["value"].Value, out int qnh))
                return qnh;

            Match start = Regex.Match(text, @"\bq\s*n\s*h\s+");
            if (!start.Success) return null;
            string rest = text.Substring(start.Index + start.Length);
            for (int pressure = 950; pressure <= 1060; pressure++)
            {
                string spoken = Normalize(
                    AviationFrenchNumbers.DigitsOnly(pressure));
                if (Regex.IsMatch(rest,
                    @"^" + Regex.Escape(spoken).Replace(@"\ ", @"\s+") +
                    @"\b", RegexOptions.CultureInvariant))
                    return pressure;
            }
            return null;
        }

        private static bool ContainsPoint(
            string text,
            string point)
        {
            if (string.IsNullOrWhiteSpace(
                point))
            {
                return true;
            }

            string compact =
                (text ?? "")
                    .ToLowerInvariant()
                    .Replace(" ", "");

            string rawPoint =
                point
                    .ToLowerInvariant()
                    .Replace(" ", "");

            string spokenPoint =
                Normalize(
                    AviationFrenchNumbers.Identifier(
                        point))
                    .Replace(" ", "");

            return compact.Contains(rawPoint) ||
                   compact.Contains(spokenPoint);
        }

        private static bool ContainsRunway(
            string text,
            string runway)
        {
            if (string.IsNullOrWhiteSpace(
                runway))
            {
                return true;
            }

            string compact =
                Normalize(text)
                    .Replace(" ", "");

            string spoken =
                Normalize(
                    AviationFrenchNumbers.Runway(
                        runway))
                    .Replace(" ", "");

            return compact.Contains(
                       "piste" + runway) ||
                   compact.Contains(runway) ||
                   compact.Contains(
                       "piste" + spoken) ||
                   compact.Contains(spoken);
        }

        private static bool LooksLikeReadback(
            string text)
        {
            return ContainsAny(
                text,
                "je roule",
                "roulons",
                "je maintiens",
                "m aligne",
                "j attends",
                "je decolle",
                "je décolle",
                "j atterris",
                "piste",
                "point d attente");
        }

        private static bool IsInitialCall(
            string text)
        {
            return ContainsAny(
                text,
                "bron sol bonjour",
                "bron tour bonjour",
                "lyon bron sol",
                "lyon bron tour") &&
                !ContainsAny(
                    text,
                    "demande",
                    "roulage",
                    "pret",
                    "prêt",
                    "finale",
                    "vent arriere",
                    "vent arrière",
                    "arrivee",
                    "arrivée");
        }

        private static string PhaseForSpeech(
            string phase)
        {
            switch (
                (phase ?? "")
                    .ToLowerInvariant())
            {
                case "final":
                    return "en finale";

                case "runway":
                    return "sur la piste";

                case "downwind":
                    return "en vent arrière";

                case "base":
                    return "en étape de base";

                case "initialclimb":
                    return "en montée initiale";

                case "crosswind":
                    return "en traversier";

                default:
                    return "dans le circuit";
            }
        }

        private static AtcResponse Speak(
            string text,
            string feedback)
        {
            return new AtcResponse
            {
                Text = text,
                Feedback = feedback
            };
        }

        private static string WindSpeech(
            TelemetrySnapshot telemetry)
        {
            int direction =
                telemetry?.WindDirectionTrueDeg >= 0
                    ? (int)Math.Round(
                          telemetry
                              .WindDirectionTrueDeg /
                          10.0) *
                      10
                    : 0;

            if (direction == 360)
            {
                direction = 0;
            }

            int speed =
                telemetry != null
                    ? Math.Max(
                        0,
                        (int)Math.Round(
                            telemetry.WindSpeedKt))
                    : 0;

            return AviationFrenchNumbers
                       .DigitsOnly(
                           direction,
                           3) +
                   " degrés " +
                   AviationFrenchNumbers
                       .DigitsOnly(speed) +
                   " noeuds";
        }

        private static string ExtractQnhSpeech(
            string atisText)
        {
            int marker =
                atisText.IndexOf(
                    "Q N H ",
                    StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                return AviationFrenchNumbers
                    .DigitsOnly(1013);
            }

            string tail =
                atisText.Substring(
                    marker + 6);

            int period =
                tail.IndexOf('.');

            return period >= 0
                ? tail.Substring(
                    0,
                    period)
                : tail;
        }

        private static bool ContainsAny(
            string text,
            params string[] terms)
        {
            return terms.Any(
                term =>
                    text.Contains(term));
        }

        private static string Normalize(
            string value)
        {
            if (string.IsNullOrWhiteSpace(
                value))
            {
                return "";
            }

            string decomposed =
                value
                    .ToLowerInvariant()
                    .Normalize(
                        NormalizationForm.FormD);

            var builder =
                new StringBuilder();

            foreach (char c in decomposed)
            {
                UnicodeCategory category =
                    CharUnicodeInfo
                        .GetUnicodeCategory(c);

                if (category !=
                    UnicodeCategory
                        .NonSpacingMark)
                {
                    builder.Append(
                        char.IsPunctuation(c)
                            ? ' '
                            : c);
                }
            }

            return string.Join(
                " ",
                builder
                    .ToString()
                    .Normalize(
                        NormalizationForm.FormC)
                    .Split(
                        new[] { ' ' },
                        StringSplitOptions
                            .RemoveEmptyEntries));
        }
    }
}
