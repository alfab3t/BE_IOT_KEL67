namespace astratech_apps_backend.DTOs.Iot
{
    public class AppUser
    {
        public string Username { get; set; } = string.Empty;
        public string FullName  { get; set; } = string.Empty;
        public string Email     { get; set; } = string.Empty;
        public string Status    { get; set; } = string.Empty;
        public string RoleName  { get; set; } = string.Empty;
        public string RoleId    { get; set; } = string.Empty;
        public string? Photo    { get; set; }
        public bool CanManage   { get; set; } = false;
        public List<DeviceAccess> AccessibleDevices { get; set; } = [];
    }

    public class DeviceAccess
    {
        public string DeviceId   { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Category   { get; set; } = string.Empty;
        public string Status     { get; set; } = string.Empty;
    }
}
