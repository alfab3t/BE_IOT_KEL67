using System.ComponentModel.DataAnnotations;

namespace astratech_apps_backend.DTOs.Iot.Request
{
    public record CreateDeviceRequest
    {
        [Required(ErrorMessage = "ID Device wajib diisi.")]
        public string Id { get; init; } = string.Empty;

        [Required(ErrorMessage = "MAC Address wajib diisi.")]
        public string MacAddress { get; init; } = string.Empty;

        [Required(ErrorMessage = "Nama Device wajib diisi.")]
        public string Name { get; init; } = string.Empty;

        [Required(ErrorMessage = "Kategori wajib diisi.")]
        public string Category { get; init; } = string.Empty;

        public decimal? LimitMin { get; init; }
        public decimal? LimitMax { get; init; }
        public string? Photo { get; init; }
        public int? LocationId { get; init; }
    }
}
