namespace astratech_apps_backend.DTOs.Iot
{
    public record GraphPoint
    {
        public decimal AverageValue { get; init; }
        public decimal MaximumValue { get; init; }
        public decimal MinimumValue { get; init; }
        public string LabelText { get; init; } = string.Empty;
    }
}
