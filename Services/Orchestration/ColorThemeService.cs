using System.Text.Json;

namespace Autonomuse.Services.Orchestration
{
    public class ColorThemeService
    {
        public class AccentTheme
        {
            public string Base { get; set; } = "#e8a30e";
            public string Bright { get; set; } = "#d4af37";
            public string Dark { get; set; } = "#b8860b";
            public string Hover { get; set; } = "#ffb82e";
            public string Glow { get; set; } = "rgba(232, 163, 14, 0.15)";
            public string Subtle { get; set; } = "rgba(232, 163, 14, 0.05)";
            public string ContrastText { get; set; } = "#000000";
        }

        public AccentTheme GenerateTheme(string hex, bool isTextWhite)
        {
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#") || (hex.Length != 7 && hex.Length != 4))
                return new AccentTheme { ContrastText = isTextWhite ? "#FFFFFF" : "#000000" };

            var (r, g, b) = ParseHex(hex);
            if (r < 0) return new AccentTheme { ContrastText = isTextWhite ? "#FFFFFF" : "#000000" };

            return new AccentTheme
            {
                Base = hex,
                Bright = ColorToHex(Adjust(r, g, b, 1.2f)),
                Dark = ColorToHex(Adjust(r, g, b, 0.7f)),
                Hover = ColorToHex(Adjust(r, g, b, 1.1f)),
                Glow = $"rgba({r}, {g}, {b}, 0.15)",
                Subtle = $"rgba({r}, {g}, {b}, 0.05)",
                ContrastText = isTextWhite ? "#FFFFFF" : "#000000"
            };
        }

        private static (int r, int g, int b) ParseHex(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 3)
                    hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                return (
                    Convert.ToInt32(hex[..2], 16),
                    Convert.ToInt32(hex[2..4], 16),
                    Convert.ToInt32(hex[4..], 16)
                );
            }
            catch { return (-1, -1, -1); }
        }

        private static (int r, int g, int b) Adjust(int r, int g, int b, float factor)
        {
            return (
                Math.Clamp((int)(r * factor), 0, 255),
                Math.Clamp((int)(g * factor), 0, 255),
                Math.Clamp((int)(b * factor), 0, 255)
            );
        }

        private static string ColorToHex((int r, int g, int b) c) => $"#{c.r:X2}{c.g:X2}{c.b:X2}";

        public string SerializeTheme(AccentTheme theme) => JsonSerializer.Serialize(theme);
        public AccentTheme? DeserializeTheme(string json) => JsonSerializer.Deserialize<AccentTheme>(json);

        public static string HexToRgb(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#")) return "18, 18, 18";
            var (r, g, b) = ParseHex(hex);
            return r >= 0 ? $"{r}, {g}, {b}" : "18, 18, 18";
        }
    }
}
