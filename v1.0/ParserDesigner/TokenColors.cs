namespace ParserDesigner;

internal static class TokenColors
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(191, 219, 254), Color.FromArgb(254, 215, 170),
        Color.FromArgb(187, 247, 208), Color.FromArgb(233, 213, 255),
        Color.FromArgb(254, 205, 211), Color.FromArgb(165, 243, 252),
        Color.FromArgb(254, 240, 138), Color.FromArgb(217, 249, 157),
        Color.FromArgb(199, 210, 254), Color.FromArgb(245, 208, 254),
        Color.FromArgb(153, 246, 228), Color.FromArgb(253, 186, 116)
    ];
    public static Color For(int index)
    {
        index = Math.Max(index, 0);
        if (index < Palette.Length)
            return Palette[index];
        double phase = index * 2.399963229728653;
        return Color.FromArgb(210 + (int)(35 * Math.Sin(phase)), 210 + (int)(35 * Math.Sin(phase + 2.094)), 210 + (int)(35 * Math.Sin(phase + 4.189)));
    }
}
