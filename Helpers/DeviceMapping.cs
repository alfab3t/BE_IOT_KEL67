using System;
using System.Collections.Generic;

namespace astratech_apps_backend.Helpers
{
    public static class DeviceMapping
    {
        // Maps old string ID -> physical MAC address
        private static readonly Dictionary<string, string> StringIdToMac = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DEV_AC01", "24:2C:70:39:65:14" },
            { "DEV_AC02", "48:2F:50:5C:ED:2D" },
            { "DEV_LAMP01", "97:89:1D:AD:FE:4A" },
            { "DEV_LAMP02", "E5:B5:54:7C:5C:18" },
            { "DEV_PWR_A", "00:1A:2B:3C:4D:71" },
            { "DEV_PWR_B", "00:1A:2B:3C:4D:72" },
            { "DEV_PWR_C", "00:1A:2B:3C:4D:73" },
            { "DEV_PWR_D", "00:1A:2B:3C:4D:74" },
            { "DEV_PWR01", "00:1A:2B:3C:4D:5F" },
            { "DEV_TEMP01", "84:F3:EB:B4:51:4F" },
            { "DEV_TEMP02", "B8:F8:62:62:1E:84" }
        };

        // Maps physical MAC address -> old string ID
        private static readonly Dictionary<string, string> MacToStringId = new(StringComparer.OrdinalIgnoreCase)
        {
            { "24:2C:70:39:65:14", "DEV_AC01" },
            { "48:2F:50:5C:ED:2D", "DEV_AC02" },
            { "97:89:1D:AD:FE:4A", "DEV_LAMP01" },
            { "E5:B5:54:7C:5C:18", "DEV_LAMP02" },
            { "00:1A:2B:3C:4D:71", "DEV_PWR_A" },
            { "00:1A:2B:3C:4D:72", "DEV_PWR_B" },
            { "00:1A:2B:3C:4D:73", "DEV_PWR_C" },
            { "00:1A:2B:3C:4D:74", "DEV_PWR_D" },
            { "00:1A:2B:3C:4D:5F", "DEV_PWR01" },
            { "84:F3:EB:B4:51:4F", "DEV_TEMP01" },
            { "B8:F8:62:62:1E:84", "DEV_TEMP02" }
        };

        public static string GetMac(string stringId)
        {
            if (string.IsNullOrEmpty(stringId)) return stringId;
            return StringIdToMac.TryGetValue(stringId.Trim(), out var mac) ? mac : stringId;
        }

        public static string GetStringId(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return mac;
            return MacToStringId.TryGetValue(mac.Trim(), out var stringId) ? stringId : mac;
        }
    }
}
