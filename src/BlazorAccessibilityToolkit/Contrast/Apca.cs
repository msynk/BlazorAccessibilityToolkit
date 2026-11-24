using System.Globalization;

namespace BlazorAccessibilityToolkit;

/// <summary>
/// The Accessible Perceptual Contrast Algorithm (APCA), a candidate contrast method for WCAG 3, as implemented by
/// apca-w3 0.1.9 (algorithm version 0.0.98G-4g, <see href="https://github.com/Myndex/apca-w3"/>). It is a port of the
/// contrast function only, with the published constants unchanged, verified against the test values of apca-w3.
/// </summary>
/// <remarks>
/// <para>APCA is not part of WCAG 2: use its results as information next to the WCAG 2 contrast ratio, which remains
/// the conformance requirement.</para>
/// <para>apca-w3 is not open source. Its license (<see href="https://github.com/Myndex/apca-w3/blob/master/LICENSE.md"/>)
/// permits use for predicting the contrast of web content and requires a commercial license from Myndex Research for
/// commercial use. Check that your use is covered.</para>
/// </remarks>
public static class Apca
{
    /// <summary>The APCA algorithm version implemented.</summary>
    public const string AlgorithmVersion = "0.0.98G-4g";

    // apca-w3 0.1.9, SA98G constants.
    private const double MainTrc = 2.4;
    private const double SRco = 0.2126729;
    private const double SGco = 0.7151522;
    private const double SBco = 0.0721750;
    private const double NormBg = 0.56;
    private const double NormTxt = 0.57;
    private const double RevTxt = 0.62;
    private const double RevBg = 0.65;
    private const double BlkThrs = 0.022;
    private const double BlkClmp = 1.414;
    private const double ScaleBoW = 1.14;
    private const double ScaleWoB = 1.14;
    private const double LoBoWoffset = 0.027;
    private const double LoWoBoffset = 0.027;
    private const double DeltaYmin = 0.0005;
    private const double LoClip = 0.1;

    /// <summary>
    /// The lightness contrast (Lc) of text on a background, from about -108 to 106. Positive values are dark text on a
    /// light background, negative values light text on a dark background; compare the absolute value with the guidance.
    /// Colors are CSS hex colors (<c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c> or <c>#rrggbbaa</c>); a translucent text color
    /// is blended over the background first, as apca-w3 does.
    /// </summary>
    public static double Contrast(string textColor, string backgroundColor)
    {
        var (tr, tg, tb, ta) = ParseHex(textColor);
        var (br, bg, bb, _) = ParseHex(backgroundColor);
        if (ta < 1)
        {
            // apca-w3 alphaBlend, with rounding.
            var alpha = Math.Clamp(ta, 0, 1);
            var complement = 1 - alpha;
            tr = Math.Min(Math.Round((br * complement) + (tr * alpha), MidpointRounding.AwayFromZero), 255);
            tg = Math.Min(Math.Round((bg * complement) + (tg * alpha), MidpointRounding.AwayFromZero), 255);
            tb = Math.Min(Math.Round((bb * complement) + (tb * alpha), MidpointRounding.AwayFromZero), 255);
        }

        return Contrast(SrgbToY(tr, tg, tb), SrgbToY(br, bg, bb));
    }

    /// <summary>The APCA luminance (Y) of an sRGB color with channels from 0 to 255 (a simple 2.4 power curve).</summary>
    public static double SrgbToY(double red, double green, double blue) =>
        (SRco * Math.Pow(red / 255.0, MainTrc)) + (SGco * Math.Pow(green / 255.0, MainTrc)) + (SBco * Math.Pow(blue / 255.0, MainTrc));

    /// <summary>The lightness contrast (Lc) of a text luminance on a background luminance, from <see cref="SrgbToY"/>.</summary>
    public static double Contrast(double textY, double backgroundY)
    {
        if (double.IsNaN(textY) || double.IsNaN(backgroundY) || Math.Min(textY, backgroundY) < 0 || Math.Max(textY, backgroundY) > 1.1)
        {
            return 0;
        }

        textY = textY > BlkThrs ? textY : textY + Math.Pow(BlkThrs - textY, BlkClmp);
        backgroundY = backgroundY > BlkThrs ? backgroundY : backgroundY + Math.Pow(BlkThrs - backgroundY, BlkClmp);
        if (Math.Abs(backgroundY - textY) < DeltaYmin)
        {
            return 0;
        }

        double output;
        if (backgroundY > textY)
        {
            var sapc = (Math.Pow(backgroundY, NormBg) - Math.Pow(textY, NormTxt)) * ScaleBoW;
            output = sapc < LoClip ? 0 : sapc - LoBoWoffset;
        }
        else
        {
            var sapc = (Math.Pow(backgroundY, RevBg) - Math.Pow(textY, RevTxt)) * ScaleWoB;
            output = sapc > -LoClip ? 0 : sapc + LoWoBoffset;
        }

        return output * 100;
    }

    /// <summary>
    /// The minimum |Lc| that APCA's guidance ("APCA in a Nutshell") gives for text of this size and weight: the lowest of
    /// the levels 45, 60, 75 and 90 whose minimum font size the text reaches. For example 16 px at weight 400 needs
    /// Lc 90, 18 px at 400 needs Lc 75, 24 px at 400 needs Lc 60 and 36 px at 400 needs Lc 45. Weights between the
    /// listed ones use the next lighter listed weight. Returns 90 with <paramref name="tooSmall"/> set when the text is
    /// smaller than the guidance's smallest size for body text (14 px at 400, 18 px at 300).
    /// </summary>
    public static int RequiredLc(double fontSizePx, int fontWeight, out bool tooSmall)
    {
        foreach (var (level, sizes) in FontTable)
        {
            if (MinimumSize(sizes, fontWeight) is { } minimum && fontSizePx >= minimum - 0.01)
            {
                tooSmall = false;
                return level;
            }
        }

        tooSmall = true;
        return 90;
    }

    // From "APCA in a Nutshell" (https://github.com/Myndex/SAPC-APCA/blob/master/documentation/APCA_in_a_Nutshell.md):
    // minimum font size in CSS pixels by font weight for each Lc level.
    private static readonly (int Level, (int Weight, double Size)[] Sizes)[] FontTable =
    [
        (45, [(400, 36), (700, 24)]),
        (60, [(200, 48), (300, 36), (400, 24), (500, 21), (600, 18), (700, 16)]),
        (75, [(300, 24), (400, 18), (500, 16), (700, 14)]),
        (90, [(300, 18), (400, 14)]),
    ];

    private static double? MinimumSize((int Weight, double Size)[] sizes, int weight)
    {
        double? size = null;
        foreach (var entry in sizes)
        {
            if (entry.Weight <= weight)
            {
                size = entry.Size;
            }
        }

        return size;
    }

    private static (double R, double G, double B, double A) ParseHex(string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);
        var hex = color.Trim().TrimStart('#');
        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length is not (6 or 8) || !hex.All(Uri.IsHexDigit))
        {
            throw new FormatException($"'{color}' is not a hex color (#rgb, #rgba, #rrggbb or #rrggbbaa).");
        }

        double Channel(int index) => int.Parse(hex.AsSpan(index * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (Channel(0), Channel(1), Channel(2), hex.Length == 8 ? Channel(3) / 255.0 : 1);
    }
}
