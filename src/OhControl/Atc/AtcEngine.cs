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
        }

        private readonly AtisService _atisService;

        private readonly Dictionary<string, TrainingState> _states =
            new Dictionary<string, TrainingState>(
                StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, PendingReadback> _pendingReadbacks =
            new Dictionary<string, PendingReadback>(
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
                return readback;
            }

            if (station.Kind == RadioStationKind.Ground)
            {
                return HandleGround(
                    normalized,
                    callsign,
                    spokenCallsign,
                    atis);
            }

            return HandleTower(
                normalized,
                callsign,
                spokenCallsign,
                atis,
                telemetry,
                station.FrequencyMhz);
        }

        private AtcResponse HandleGround(
            string text,
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis)
        {
            if (IsInitialCall(text))
            {
                return Speak(
                    spokenCallsign + ", Bron Sol, bonjour, transmettez.",
                    "Premier contact reconnu.");
            }

            if (ContainsAny(
                text,
                "demande roulage",
                "roulage",
                "consignes de roulage",
                "taxi"))
            {
                var profile =
                    LflyGroundProfile.ForRunway(atis.Runway);

                SetState(
                    callsignKey,
                    TrainingState.Taxiing);

                SetPendingReadback(
                    callsignKey,
                    "taxi",
                    atis.Runway,
                    profile.FullLengthHoldingPoint);

                return Speak(
                    spokenCallsign +
                    ", roulez point d'attente " +
                    profile.FullLengthHoldingPoint +
                    " piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ". Q N H " +
                    ExtractQnhSpeech(atis.Text) +
                    ". Rappelez prêt.",
                    "Roulage pleine longueur par défaut : " +
                    profile.FullLengthHoldingPoint +
                    ". A2/A3 restent disponibles pour de futurs scénarios de départ intermédiaire ; A5 nécessite une autorisation ATC.");
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

                return Speak(
                    spokenCallsign +
                    ", contactez Bron Tour " +
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
                    ", roulez au parking.",
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
                spokenCallsign + ", Bron Sol, transmettez.",
                "Demande non classée sur la fréquence Sol.");
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
                        ground.FullLengthHoldingPoint +
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
                    ", alignez-vous et attendez piste " +
                    AviationFrenchNumbers.Runway(
                        atis.Runway) +
                    ", point d'attente " +
                    ground.FullLengthHoldingPoint +
                    ".",
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
                    ", numéro un, rappelez finale piste " +
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
                    ", rappelez finale piste " +
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
                    ", rappelez vent arrière piste " +
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

                return Speak(
                    spokenCallsign +
                    ", contactez Bron Sol " +
                    AviationFrenchNumbers.Frequency(121.705) +
                    ".",
                    "Après dégagement, passage sur 121.705 MHz.");
            }

            return Speak(
                spokenCallsign +
                ", Bron Tour, transmettez.",
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

                if (holdingPointOk &&
                    runwayOk &&
                    ContainsAny(
                        text,
                        "roule",
                        "roulons",
                        "point d attente",
                        "point attente"))
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
                            "."
                    };
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
                    if (LooksLikeReadback(text))
                    {
                        return Speak(
                            spokenCallsign +
                            ", alignez-vous et attendez piste " +
                            AviationFrenchNumbers.Runway(
                                pending.Runway) +
                            ".",
                            "Collationnement incomplet : la piste, l'alignement et l'attente doivent être repris.");
                    }

                    return null;
                }

                _pendingReadbacks.Remove(
                    callsignKey);

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
                        "je décolle",
                        "decolle",
                        "décolle");

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
                            "Collationnement décollage correct : « piste " +
                            pending.Runway +
                            ", je décolle »."
                    };
                }

                if (LooksLikeReadback(text))
                {
                    return Speak(
                        spokenCallsign +
                        ", piste " +
                        AviationFrenchNumbers.Runway(
                            pending.Runway) +
                        ", autorisé décollage.",
                        "Collationnement décollage incomplet : reprends la piste et l'action de décoller.");
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
                            "Collationnement atterrissage correct : « piste " +
                            pending.Runway +
                            ", j'atterris »."
                    };
                }

                if (LooksLikeReadback(text))
                {
                    return Speak(
                        spokenCallsign +
                        ", piste " +
                        AviationFrenchNumbers.Runway(
                            pending.Runway) +
                        ", autorisé atterrissage.",
                        "Collationnement atterrissage incomplet : reprends la piste et l'action d'atterrir.");
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

                return null;
            }

            return null;
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
            return Speak(
                spokenCallsign +
                ", roulez point d'attente " +
                pending.HoldingPoint +
                " piste " +
                AviationFrenchNumbers.Runway(
                    pending.Runway) +
                ".",
                "Collationnement roulage incomplet : reprends le point d'attente et la piste.");
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
            string holdingPoint)
        {
            _pendingReadbacks[
                NormalizeCallsignKey(
                    callsign)] =
                new PendingReadback
                {
                    Kind = kind,
                    Runway = runway,
                    HoldingPoint =
                        holdingPoint
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

            string normalizedPoint =
                point
                    .ToLowerInvariant()
                    .Replace(" ", "");

            string compact =
                (text ?? "")
                    .Replace(" ", "");

            return compact.Contains(
                normalizedPoint);
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
                (text ?? "")
                    .Replace(" ", "");

            return compact.Contains(
                       "piste" + runway) ||
                   compact.Contains(runway);
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
