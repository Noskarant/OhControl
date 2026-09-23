using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            Airborne,
            Downwind,
            Final,
            Landed
        }

        private readonly AtisService _atisService;
        private readonly Dictionary<string, TrainingState> _states =
            new Dictionary<string, TrainingState>(
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
            if (ContainsAny(text, "roulage", "rouler", "taxi"))
            {
                SetState(callsignKey, TrainingState.Taxiing);

                return Speak(
                    spokenCallsign + ", roulez point d'attente piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ". Q N H " +
                    ExtractQnhSpeech(atis.Text) +
                    ". Rappelez prêt au point d'attente.",
                    "Roulage autorisé vers le point d'attente. " +
                    "La route de taxi détaillée sera ajoutée avec la carte sol LFLY.");
            }

            if (ContainsAny(text, "pret", "point d attente", "attente"))
            {
                SetState(callsignKey, TrainingState.HoldingPoint);

                return Speak(
                    spokenCallsign +
                    ", contactez Bron Tour un un huit décimale un zéro zéro.",
                    "Passe sur 118.100 MHz dans COM1.");
            }

            if (ContainsAny(text, "piste degagee", "degage", "parking"))
            {
                SetState(callsignKey, TrainingState.Landed);

                return Speak(
                    spokenCallsign + ", roulez au parking. Au revoir.",
                    "Fin du scénario tour de piste.");
            }

            return Speak(
                spokenCallsign + ", Bron Sol, transmettez.",
                "Le moteur attend surtout une demande de roulage ou un report prêt au point d'attente.");
        }

        private AtcResponse HandleTower(
            string text,
            string callsignKey,
            string spokenCallsign,
            AtisBroadcast atis,
            TelemetrySnapshot telemetry,
            double towerFrequency)
        {
            if (ContainsAny(text, "integration", "integrer"))
            {
                SetState(callsignKey, TrainingState.Airborne);

                MultiplayerPlayerState traffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Downwind",
                        "Final");

                string trafficText = traffic == null
                    ? ""
                    : " Trafic connu " +
                      AviationCallsign.ToSpeech(traffic.Callsign) +
                      " " +
                      PhaseForSpeech(traffic.CircuitPhase) +
                      ".";

                return Speak(
                    spokenCallsign + ", intégrez vent arrière piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    "." + trafficText +
                    " Rappelez vent arrière.",
                    traffic == null
                        ? "Instruction d'intégration reconnue."
                        : "L'ATC tient compte de l'autre avion connecté.");
            }

            if (ContainsAny(text, "pret", "point d attente", "attente") &&
                GetState(callsignKey) != TrainingState.LineUpAndWait)
            {
                MultiplayerPlayerState blockingTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Final",
                        "Runway");

                if (blockingTraffic != null)
                {
                    SetState(callsignKey, TrainingState.HoldingPoint);

                    return Speak(
                        spokenCallsign +
                        ", maintenez avant piste " +
                        AviationFrenchNumbers.Runway(atis.Runway) +
                        ", trafic " +
                        AviationCallsign.ToSpeech(blockingTraffic.Callsign) +
                        " " +
                        PhaseForSpeech(blockingTraffic.CircuitPhase) +
                        ".",
                        "Départ retenu car l'autre avion est détecté sur la piste ou en finale.");
                }

                SetState(callsignKey, TrainingState.LineUpAndWait);

                return Speak(
                    spokenCallsign + ", alignez-vous piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    " et attendez.",
                    "Collationne la piste, l'alignement et l'attente.");
            }

            if (GetState(callsignKey) == TrainingState.LineUpAndWait &&
                ContainsAny(text, "aligne", "attends", "attend"))
            {
                MultiplayerPlayerState finalTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Final");

                if (finalTraffic != null &&
                    finalTraffic.DistanceToThresholdMeters < 3500)
                {
                    return Speak(
                        spokenCallsign +
                        ", maintenez position, trafic " +
                        AviationCallsign.ToSpeech(finalTraffic.Callsign) +
                        " en finale.",
                        "Décollage retenu : trafic connecté détecté en finale.");
                }

                SetState(callsignKey, TrainingState.Airborne);

                return Speak(
                    spokenCallsign + ", autorisé décollage piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ", vent " + WindSpeech(telemetry) + ".",
                    "Autorisation de décollage : piste et clairance doivent être collationnées.");
            }

            if (ContainsAny(text, "vent arriere"))
            {
                SetState(callsignKey, TrainingState.Downwind);

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
                        AviationCallsign.ToSpeech(finalTraffic.Callsign) +
                        " en finale, rappelez finale piste " +
                        AviationFrenchNumbers.Runway(atis.Runway) + ".",
                        "Séquence générée à partir du trafic multijoueur connecté.");
                }

                MultiplayerPlayerState downwindTraffic =
                    FindTraffic(
                        atis.Runway,
                        towerFrequency,
                        "Downwind");

                if (downwindTraffic != null)
                {
                    return Speak(
                        spokenCallsign +
                        ", numéro deux derrière " +
                        AviationCallsign.ToSpeech(downwindTraffic.Callsign) +
                        " en vent arrière, rappelez finale piste " +
                        AviationFrenchNumbers.Runway(atis.Runway) + ".",
                        "L'autre avion est détecté dans le même tour de piste.");
                }

                return Speak(
                    spokenCallsign + ", numéro un, rappelez finale piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) + ".",
                    "Report vent arrière reconnu.");
            }

            if (ContainsAny(text, "etape de base", "base"))
            {
                return Speak(
                    spokenCallsign + ", rappelez finale piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) + ".",
                    "Report base reconnu.");
            }

            if (ContainsAny(text, "finale", "final"))
            {
                SetState(callsignKey, TrainingState.Final);

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
                        AviationCallsign.ToSpeech(runwayTraffic.Callsign) +
                        " sur la piste, rappelez courte finale.",
                        "La piste est détectée occupée par l'autre joueur ; aucune autorisation d'atterrissage n'est délivrée.");
                }

                return Speak(
                    spokenCallsign + ", autorisé atterrissage piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ", vent " + WindSpeech(telemetry) + ".",
                    "Autorisation d'atterrissage : collationne la piste et l'autorisation.");
            }

            if (ContainsAny(text, "piste degagee", "degage"))
            {
                SetState(callsignKey, TrainingState.Landed);

                return Speak(
                    spokenCallsign +
                    ", contactez Bron Sol un deux un décimale sept zéro cinq.",
                    "Après dégagement, passe sur 121.705 MHz.");
            }

            return Speak(
                spokenCallsign + ", Bron Tour, transmettez.",
                "Transmission comprise, mais aucun scénario V1 précis n'a été reconnu.");
        }

        private MultiplayerPlayerState FindTraffic(
            string runway,
            double frequencyMhz,
            params string[] phases)
        {
            return _traffic
                .Where(p =>
                    p != null &&
                    !string.IsNullOrWhiteSpace(p.Callsign) &&
                    Math.Abs(p.Com1ActiveMhz - frequencyMhz) <= 0.006 &&
                    (string.IsNullOrWhiteSpace(p.ActiveRunway) ||
                     p.ActiveRunway == runway) &&
                    phases.Any(phase =>
                        string.Equals(
                            phase,
                            p.CircuitPhase,
                            StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.DistanceToThresholdMeters)
                .FirstOrDefault();
        }

        private TrainingState GetState(string callsign)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                callsign = "UNKNOWN";
            }

            return _states.TryGetValue(callsign, out TrainingState state)
                ? state
                : TrainingState.Parked;
        }

        private void SetState(string callsign, TrainingState state)
        {
            if (string.IsNullOrWhiteSpace(callsign))
            {
                callsign = "UNKNOWN";
            }

            _states[callsign] = state;
        }

        private static string PhaseForSpeech(string phase)
        {
            switch ((phase ?? "").ToLowerInvariant())
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

        private static AtcResponse Speak(string text, string feedback)
        {
            return new AtcResponse
            {
                Text = text,
                Feedback = feedback
            };
        }

        private static string WindSpeech(TelemetrySnapshot telemetry)
        {
            int direction = telemetry?.WindDirectionTrueDeg >= 0
                ? (int)Math.Round(telemetry.WindDirectionTrueDeg / 10.0) * 10
                : 0;

            if (direction == 360)
            {
                direction = 0;
            }

            int speed = telemetry != null
                ? Math.Max(0, (int)Math.Round(telemetry.WindSpeedKt))
                : 0;

            return AviationFrenchNumbers.DigitsOnly(direction, 3) +
                   " degrés " +
                   AviationFrenchNumbers.DigitsOnly(speed) +
                   " noeuds";
        }

        private static string ExtractQnhSpeech(string atisText)
        {
            int marker = atisText.IndexOf(
                "Q N H ",
                StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                return AviationFrenchNumbers.DigitsOnly(1013);
            }

            string tail = atisText.Substring(marker + 6);
            int period = tail.IndexOf('.');

            return period >= 0 ? tail.Substring(0, period) : tail;
        }

        private static bool ContainsAny(
            string text,
            params string[] terms)
        {
            return terms.Any(term => text.Contains(term));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string decomposed = value
                .ToLowerInvariant()
                .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder();

            foreach (char c in decomposed)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(c);

                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.IsPunctuation(c) ? ' ' : c);
                }
            }

            return string.Join(
                " ",
                builder.ToString()
                    .Normalize(NormalizationForm.FormC)
                    .Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
