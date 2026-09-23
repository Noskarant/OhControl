using System;
using System.Globalization;
using System.Linq;
using System.Text;
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
        private TrainingState _state = TrainingState.Parked;

        public AtcEngine(AtisService atisService)
        {
            _atisService = atisService;
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
                    Feedback = "L'ATIS est une fréquence d'écoute : aucune émission pilote attendue."
                };
            }

            string normalized = Normalize(transcript);
            string spokenCallsign = AviationCallsign.ToSpeech(callsign);
            AtisBroadcast atis = _atisService.Build(telemetry);

            if (station.Kind == RadioStationKind.Ground)
            {
                return HandleGround(normalized, spokenCallsign, atis);
            }

            return HandleTower(normalized, spokenCallsign, atis, telemetry);
        }

        private AtcResponse HandleGround(
            string text,
            string callsign,
            AtisBroadcast atis)
        {
            if (ContainsAny(text, "roulage", "rouler", "taxi"))
            {
                _state = TrainingState.Taxiing;

                return Speak(
                    callsign + ", roulez point d'attente piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ". Q N H " +
                    ExtractQnhSpeech(atis.Text) +
                    ". Rappelez prêt au point d'attente.",
                    "Roulage autorisé vers le point d'attente. Route de taxi détaillée volontairement non simulée à ce stade.");
            }

            if (ContainsAny(text, "pret", "point d attente", "attente"))
            {
                _state = TrainingState.HoldingPoint;

                return Speak(
                    callsign +
                    ", contactez Bron Tour un un huit décimale un zéro zéro.",
                    "Passe sur 118.100 MHz dans COM1.");
            }

            if (ContainsAny(text, "piste degagee", "degage", "parking"))
            {
                _state = TrainingState.Landed;

                return Speak(
                    callsign + ", roulez au parking. Au revoir.",
                    "Fin du scénario tour de piste.");
            }

            return Speak(
                callsign + ", Bron Sol, transmettez.",
                "Le moteur attend surtout une demande de roulage ou un report prêt au point d'attente.");
        }

        private AtcResponse HandleTower(
            string text,
            string callsign,
            AtisBroadcast atis,
            TelemetrySnapshot telemetry)
        {
            if (ContainsAny(text, "integration", "integrer"))
            {
                _state = TrainingState.Airborne;

                return Speak(
                    callsign + ", intégrez vent arrière piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ", rappelez vent arrière.",
                    "Instruction d'intégration reconnue.");
            }

            if (ContainsAny(text, "pret", "point d attente", "attente") &&
                _state != TrainingState.LineUpAndWait)
            {
                _state = TrainingState.LineUpAndWait;

                return Speak(
                    callsign + ", alignez-vous piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    " et attendez.",
                    "Collationne la piste, l'alignement et l'attente.");
            }

            if (_state == TrainingState.LineUpAndWait &&
                ContainsAny(text, "aligne", "attends", "attend"))
            {
                _state = TrainingState.Airborne;

                return Speak(
                    callsign + ", autorisé décollage piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ", vent " + WindSpeech(telemetry) + ".",
                    "Autorisation de décollage : piste et clairance doivent être collationnées.");
            }

            if (ContainsAny(text, "vent arriere"))
            {
                _state = TrainingState.Downwind;

                return Speak(
                    callsign + ", numéro un, rappelez finale piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) + ".",
                    "Report vent arrière reconnu.");
            }

            if (ContainsAny(text, "etape de base", "base"))
            {
                return Speak(
                    callsign + ", rappelez finale piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) + ".",
                    "Report base reconnu.");
            }

            if (ContainsAny(text, "finale", "final"))
            {
                _state = TrainingState.Final;

                return Speak(
                    callsign + ", autorisé atterrissage piste " +
                    AviationFrenchNumbers.Runway(atis.Runway) +
                    ", vent " + WindSpeech(telemetry) + ".",
                    "Autorisation d'atterrissage : collationne la piste et l'autorisation.");
            }

            if (ContainsAny(text, "piste degagee", "degage"))
            {
                _state = TrainingState.Landed;

                return Speak(
                    callsign +
                    ", contactez Bron Sol un deux un décimale sept zéro cinq.",
                    "Après dégagement, passe sur 121.705 MHz.");
            }

            return Speak(
                callsign + ", Bron Tour, transmettez.",
                "Transmission comprise, mais aucun scénario V1 précis n'a été reconnu.");
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
            int marker = atisText.IndexOf("Q N H ", StringComparison.OrdinalIgnoreCase);

            if (marker < 0)
            {
                return AviationFrenchNumbers.DigitsOnly(1013);
            }

            string tail = atisText.Substring(marker + 6);
            int period = tail.IndexOf('.');

            return period >= 0 ? tail.Substring(0, period) : tail;
        }

        private static bool ContainsAny(string text, params string[] terms)
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
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
