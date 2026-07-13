using System.Collections.Generic;

namespace astratech_apps_backend.DTOs.Iot
{
    public class UserListResponse
    {
        public IEnumerable<AppUser> Users { get; set; } = new List<AppUser>();
        public IEnumerable<RoleCount> Roles { get; set; } = new List<RoleCount>();
    }

    public class RoleCount
    {
        public string RoleId { get; set; } = "";
        public string RoleName { get; set; } = "";
        public int Count { get; set; }
    }
}
