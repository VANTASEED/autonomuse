using System.Drawing;
using System.Text.Json;
using Color = System.Drawing.Color;

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
            public string ContrastText { get; set; } = "#000000"; // Default black text for gold
        }

        public AccentTheme GenerateTheme(string hex, bool isTextWhite)
        {
            // If hex is empty or invalid, return the default Gold theme
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#") || (hex.Length != 7 && hex.Length != 4))
                return new AccentTheme { ContrastText = isTextWhite ? "#FFFFFF" : "#000000" };

            Color baseColor;
            try 
            {
                baseColor = ColorTranslator.FromHtml(hex);
            }
            catch 
            {
                return new AccentTheme { ContrastText = isTextWhite ? "#FFFFFF" : "#000000" };
            }
            
            return new AccentTheme
            {
                Base = hex,
                Bright = ColorToHex(AdjustBrightness(baseColor, 1.2f)),
                Dark = ColorToHex(AdjustBrightness(baseColor, 0.7f)),
                Hover = ColorToHex(AdjustBrightness(baseColor, 1.1f)),
                Glow = $"rgba({baseColor.R}, {baseColor.G}, {baseColor.B}, 0.15)",
                Subtle = $"rgba({baseColor.R}, {baseColor.G}, {baseColor.B}, 0.05)",
                ContrastText = isTextWhite ? "#FFFFFF" : "#000000"
            };
        }

        private Color AdjustBrightness(Color color, float factor)
        {
            float r = Math.Clamp(color.R * factor, 0, 255);
            float g = Math.Clamp(color.G * factor, 0, 255);
            float b = Math.Clamp(color.B * factor, 0, 255);
            return Color.FromArgb(color.A, (int)r, (int)g, (int)b);
        }

        private string ColorToHex(Color color)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public string SerializeTheme(AccentTheme theme) => JsonSerializer.Serialize(theme);
        public AccentTheme? DeserializeTheme(string json) => JsonSerializer.Deserialize<AccentTheme>(json);

        public static string HexToRgb(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || !hex.StartsWith("#")) return "18, 18, 18";
            try
            {
                var color = ColorTranslator.FromHtml(hex);
                return $"{color.R}, {color.G}, {color.B}";
            }
            catch { return "18, 18, 18"; }
        }
    }
}
