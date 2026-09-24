namespace OhControl.Atc
{
    public sealed class AtcResponse
    {
        public string Text { get; set; }
        public string Feedback { get; set; }
        public int? ControllerFollowUpDelaySeconds { get; set; }
    }
}
