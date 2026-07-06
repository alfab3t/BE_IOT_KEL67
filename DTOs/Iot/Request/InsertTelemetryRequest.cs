using System.ComponentModel.DataAnnotations;

namespace astratech_apps_backend.DTOs.Iot.Request
{
    public record InsertTelemetryRequest
    {
        [Required(ErrorMessage = "ID Device wajib diisi.")]
        public string DeviceId { get; init; } = string.Empty;

        [Required(ErrorMessage = "Nilai sensor wajib diisi.")]
        public decimal Value { get; init; }

        [Required(ErrorMessage = "Tipe data sensor wajib diisi.")]
        public string Type { get; init; } = string.Empty;
    }
}
