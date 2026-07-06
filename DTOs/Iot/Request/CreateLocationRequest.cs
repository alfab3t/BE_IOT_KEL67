using System.ComponentModel.DataAnnotations;

namespace astratech_apps_backend.DTOs.Iot.Request
{
    public record CreateLocationRequest
    {
        [Required(ErrorMessage = "Nama Gedung wajib diisi.")]
        public string Gedung { get; init; } = string.Empty;

        [Required(ErrorMessage = "Lantai wajib diisi.")]
        public string Lantai { get; init; } = string.Empty;

        [Required(ErrorMessage = "Nama Ruangan wajib diisi.")]
        public string Ruangan { get; init; } = string.Empty;
    }
}
