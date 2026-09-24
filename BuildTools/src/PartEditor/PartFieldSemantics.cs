namespace PartEditor
{
    // Only assign special controls where the saved key has a verified meaning.
    // Unknown mod fields retain their saved number / boolean / string type.
    internal static class PartFieldSemantics
    {
        internal static bool IsPercent(string key) =>
            key == "fuel_percent" || key == "force_percent";

        internal static string PercentLabel(string key) =>
            key == "fuel_percent" ? "Fuel (%)" : "Force (%)";

        internal static bool IsMoreOption(string key) =>
            key == "color_tex" || key == "shape_tex" || key == "shade_tex" ||
            key == "fragment";
    }
}
