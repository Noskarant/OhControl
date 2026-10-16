namespace OhControl.Radio
{
    public enum RadioStationKind
    {
        None,
        Atis,
        Ground,
        Tower
    }

    public sealed class RadioStation
    {
        public RadioStation(
            RadioStationKind kind,
            string callsign,
            double frequencyMhz)
        {
            Kind = kind;
            Callsign = callsign;
            FrequencyMhz = frequencyMhz;
        }

        public RadioStationKind Kind { get; }
        public string Callsign { get; }
        public double FrequencyMhz { get; }

        public override string ToString()
        {
            return Callsign + " " + FrequencyMhz.ToString("F3") + " MHz";
        }
    }
}
