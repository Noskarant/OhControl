using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
        }

        private readonly AtisService _atisService;

        private readonly Dictionary<string, TrainingState> _states =
            new Dictionary<string, TrainingState>(
                StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PendingReadback> _pendingReadbacks =
            new Dictionary<string, PendingReadback>(
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
            AtisBroadcast atis = _atisService.Build(telemetry);

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

        private AtcResponse HandleGround(
            string text,
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis)
        {
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
                "pret",
                "prêt",
                "point d attente",
                "point attente"))
            {
                SetState(
                    callsignKey,
                    TrainingState.HoldingPoint);

                SetPendingReadback(
                    callsignKey,
                    "frequency",
                    null,
                    null,
                    null,
                    118.100,
                    RadioStationKind.Ground);

                return Speak(
                    spokenCallsign +
                    ", reçu, contactez Bron Tour " +
                    AviationFrenchNumbers.Frequency(118.100) +
                    ".",
                    "Transfert vers 118.100 MHz.");
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

                return new AtcResponse
                {
                    Text =
                        spokenCallsign +
                        ", quittez la fréquence.",
                    Feedback =
                        "Clôture des communications en sortie de circuit."
                };
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

                SetPendingReadback(
                    callsignKey,
                    "frequency",
                    null,
                    null,
                    null,
                    121.705,
                    RadioStationKind.Tower);

                return Speak(
                    spokenCallsign +
                    ", reçu, contactez Bron Sol " +
                    AviationFrenchNumbers.Frequency(121.705) +
                    ".",
                    "Après dégagement, passage sur 121.705 MHz.");
            }

            return Speak(
                spokenCallsign +
                ", Bron Tour, transmettez vos intentions.",
                "Transmission comprise, mais scénario non classé.");
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
                    ContainsFrequency(
                        text,
                        pending.FrequencyMhz.Value);

                if (frequencyOk)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    if (IsGoodbye(text))
                    {
                        return Speak(
                            spokenCallsign +
                            ", au revoir.",
                            "Fréquence correctement collationnée et fin d'échange reconnue.");
                    }

                    return new AtcResponse
                    {
                        Feedback =
                            "Fréquence correctement collationnée : " +
                            pending.FrequencyMhz.Value
                                .ToString(
                                    "000.000",
                                    CultureInfo.InvariantCulture) +
                            " MHz."
                    };
                }

                if (IsAcknowledgementOnly(text) ||
                    LooksLikeFrequencyReadback(text))
                {
                    string stationName =
                        StationNameForFrequency(
                            pending.FrequencyMhz);

                    return Speak(
                        spokenCallsign +
                        ", collationnez, contactez " +
                        stationName +
                        " " +
                        AviationFrenchNumbers.Frequency(
                            pending.FrequencyMhz ?? 0) +
                        ".",
                        "Le changement de fréquence doit être collationné.");
                }

                return null;
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
                        text,
                        "roule",
                        "roulons",
                        "point d attente",
                        "point attente");

                if (holdingPointOk &&
                    runwayOk &&
                    qnhOk &&
                    taxiActionOk)
                {
                    _pendingReadbacks.Remove(
                        pendingKey);

                    return new AtcResponse
                    {
                        Feedback =
                            "Collationnement roulage correct : " +
                            pending.HoldingPoint +
                            " / piste " +
                            pending.Runway +
                            (pending.Qnh.HasValue
                                ? " / QNH " +
                                  pending.Qnh.Value
                                : "") +
                            "."
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

            if (pending.Kind == "lineup_wait")
            {
                bool runwayOk =
                    ContainsRunway(
                        text,
                        pending.Runway);

                bool alignmentOk =
                    ContainsAny(
                        text,
                        "m aligne",
                        "alignons",
                        "aligne");

                bool waitOk =
                    ContainsAny(
                        text,
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

                MultiplayerPlayerState finalTraffic =
                    FindTraffic(
                        pending.Runway,
                        station.FrequencyMhz,
                        "Final");

                if (finalTraffic != null &&
                    finalTraffic.DistanceToThresholdMeters <
                    3500)
                {
                    return Speak(
                        spokenCallsign +
                        ", maintenez position, trafic " +
                        AviationCallsign.ToSpeech(
                            finalTraffic.Callsign) +
                        " en finale.",
                        "Alignement collationné ; départ retenu pour trafic.");
                }

                SetState(
                    callsignKey,
                    TrainingState.ClearedForTakeoff);

                SetPendingReadback(
                    callsignKey,
                    "takeoff",
                    pending.Runway,
                    null);

                return Speak(
                    spokenCallsign +
                    ", piste " +
                    AviationFrenchNumbers.Runway(
                        pending.Runway) +
                    ", autorisé décollage, vent " +
                    WindSpeech(telemetry) +
                    ".",
                    "Alignement/attente correctement collationné ; clairance de décollage délivrée.");
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

        private static bool LooksLikeFrequencyReadback(
            string text)
        {
            return ContainsAny(
                       text,
                       "frequence",
                       "decimale",
                       "contacte",
                       "contactons") ||
                   (text ?? "").Any(
                       char.IsDigit);
        }

        private static bool ContainsFrequency(
            string text,
            double frequencyMhz)
        {
            string normalized =
                Normalize(text);

            string compact =
                normalized.Replace(
                    " ",
                    "");

            string formatted =
                frequencyMhz.ToString(
                    "000.000",
                    CultureInfo.InvariantCulture);

            string sixDigits =
                formatted.Replace(
                    ".",
                    "");

            string[] parts =
                formatted.Split('.');

            string shortDigits =
                parts[1].EndsWith(
                    "00",
                    StringComparison.Ordinal)
                    ? parts[0] +
                      parts[1].Substring(0, 1)
                    : sixDigits;

            string officialSpeech =
                Normalize(
                    AviationFrenchNumbers.Frequency(
                        frequencyMhz))
                    .Replace(
                        " ",
                        "");

            string legacySpeech =
                officialSpeech.Replace(
                    "unite",
                    "un");

            return compact.Contains(
                       sixDigits) ||
                   compact.Contains(
                       shortDigits) ||
                   compact.Contains(
                       officialSpeech) ||
                   compact.Contains(
                       legacySpeech);
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
                    ", collationnez fréquence " +
                    AviationFrenchNumbers.Frequency(
                        pending.FrequencyMhz.Value) +
                    ".",
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
