namespace Autonomuse.Shared.DTOs
{
    public class HomeUISettings
    {
        // Container settings
        public float LeftBlur { get; set; } = 0f;
        public float LeftOpacity { get; set; } = 1.0f;
        public float MenuBlur { get; set; } = 0f;
        public float MenuOpacity { get; set; } = 1.0f;
        public float RightBlur { get; set; } = 0f;
        public float RightOpacity { get; set; } = 1.0f;
        
        // Card settings
        public float CardBlur { get; set; } = 0f;
        public float CardOpacity { get; set; } = 1.0f;
        public string CardColor { get; set; } = "#121212";
        
        // General Text Settings
        public string GeneralTextColor { get; set; } = "#888888"; 
        public bool EnableTextShadow { get; set; } = false;
        public float TextShadowOpacity { get; set; } = 0.25f; 
        
        // Text Outline Settings
        public bool EnableTextOutline { get; set; } = false;
        public string TextOutlineColor { get; set; } = "#000000";
        public float TextOutlineOpacity { get; set; } = 0.25f;

        // Header Text Background Settings
        public bool EnableHeaderBackground { get; set; } = false;
        public float HeaderPaddingTop { get; set; } = 0f;
        public float HeaderPaddingBottom { get; set; } = 0f;
        public float HeaderPaddingLeft { get; set; } = 0f;
        public float HeaderPaddingRight { get; set; } = 0f;
        public string HeaderBackgroundColor { get; set; } = "#000000";
        public float HeaderBackgroundOpacity { get; set; } = 0f;
        public float HeaderBackgroundRadius { get; set; } = 0f;
        public float HeaderFontSize { get; set; } = 25f;
        public float SubheadingFontSize { get; set; } = 18f;
        public string SubheadingTextColor { get; set; } = "#888888";
    }
}
