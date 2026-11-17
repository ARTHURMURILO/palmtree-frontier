using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Shared._PS.Interactions;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._PS.Organs;

/// <summary>
/// Profile-persisted genital organ configuration (one entry per <see cref="GenitalType"/>)
/// plus the semen volume produced per climax.
/// </summary>
/// <remarks>
/// Persisted in the DB as a compact string (<see cref="ToDbString"/>/<see cref="FromDbString"/>),
/// mirroring how markings are stored, so both SQLite and Postgres only need a text column.
/// </remarks>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class GenitalOrganSettings
{
    public const int DefaultSemenVolume = 30;
    public const int MinSemenVolume = 1;
    public const int MaxSemenVolume = 500;

    public const float MinScale = 0.25f;
    public const float MaxScale = 3f;

    [DataField("organs")]
    public List<GenitalOrganData> Organs = new();

    [DataField("semenVolume")]
    public int SemenVolume = DefaultSemenVolume;

    public GenitalOrganData? Get(GenitalType type)
    {
        return Organs.FirstOrDefault(o => o.Type == type);
    }

    public void Set(GenitalType type, GenitalOrganData? data)
    {
        Organs.RemoveAll(o => o.Type == type);

        if (data != null)
        {
            data.Type = type;
            Organs.Add(data);
        }
    }

    public void Remove(GenitalType type)
    {
        Organs.RemoveAll(o => o.Type == type);
    }

    public GenitalOrganSettings Clone()
    {
        var clone = new GenitalOrganSettings
        {
            SemenVolume = SemenVolume,
        };

        foreach (var organ in Organs)
            clone.Organs.Add(organ.Clone());

        return clone;
    }

    public bool MemberwiseEquals(GenitalOrganSettings? other)
    {
        if (other == null)
            return false;

        if (SemenVolume != other.SemenVolume || Organs.Count != other.Organs.Count)
            return false;

        var ours = Organs.OrderBy(o => o.Type).ToList();
        var theirs = other.Organs.OrderBy(o => o.Type).ToList();

        for (var i = 0; i < ours.Count; i++)
        {
            if (!ours[i].MemberwiseEquals(theirs[i]))
                return false;
        }

        return true;
    }

    /// <summary>Clamps semen volume and drops entries whose catalog prototype/size no longer exists.</summary>
    public void Validate(IPrototypeManager prototypes)
    {
        SemenVolume = Math.Clamp(SemenVolume, MinSemenVolume, MaxSemenVolume);

        var valid = new List<GenitalOrganData>();
        foreach (var organ in Organs)
        {
            if (!prototypes.HasIndex<GenitalOrganPrototype>(organ.Prototype))
                continue;

            var catalog = prototypes.Index<GenitalOrganPrototype>(organ.Prototype);
            if (catalog.GenitalType != organ.Type)
                continue;

            if (organ.Size < 1 || organ.Size > catalog.Sizes.Count)
                continue;

            if (valid.Any(o => o.Type == organ.Type))
                continue;

            if (!Enum.IsDefined(organ.Visibility))
                organ.Visibility = GenitalVisibility.HiddenByUnderwear;

            organ.Offset = new Vector2(Math.Clamp(organ.Offset.X, -2f, 2f), Math.Clamp(organ.Offset.Y, -2f, 2f));
            organ.Scale = Math.Clamp(organ.Scale, MinScale, MaxScale);
            organ.Glow = Math.Clamp(organ.Glow, 0f, 1f);
            organ.DetailGlow = Math.Clamp(organ.DetailGlow, 0f, 1f);

            valid.Add(organ);
        }

        Organs = valid;
    }

    /// <summary>
    /// Best-effort conversion of a legacy genital marking into an organ entry. Matches the marking
    /// against every catalog size's flaccid/aroused/skintoned render marking; the first match wins,
    /// and an organ that is already configured for that type is never overwritten. A flaccid
    /// marking also matches its *Alt variant (balls render the aroused sprite as their standard).
    /// </summary>
    public static void TryConvertMarking(GenitalOrganSettings settings, string markingId, IPrototypeManager prototypes)
    {
        foreach (var catalog in prototypes.EnumeratePrototypes<GenitalOrganPrototype>())
        {
            if (settings.Get(catalog.GenitalType) != null)
                continue;

            for (var i = 0; i < catalog.Sizes.Count; i++)
            {
                var size = catalog.Sizes[i];
                var flaccid = size.Flaccid?.Id;
                var matchesFlaccid = flaccid == markingId ||
                                     (flaccid != null && flaccid == markingId + "Alt");
                if (!matchesFlaccid && size.Aroused?.Id != markingId && size.Skintoned?.Id != markingId)
                    continue;

                settings.Set(catalog.GenitalType, new GenitalOrganData
                {
                    Prototype = catalog.ID,
                    Size = i + 1,
                    SkinTone = size.Skintoned?.Id == markingId,
                });
                return;
            }
        }
    }

    public string ToDbString()
    {
        var parts = new List<string>();
        foreach (var organ in Organs)
        {
            var part = $"{organ.Type}:{organ.Prototype}:{organ.Size}:{organ.Visibility}";

            // Colors, offset, scale, glow and skin tone share the optional tail of the string; null
            // colors and zero values become '-' placeholders so later segments can still be written.
            var hasColors = organ.Color != null || organ.DetailColor != null;
            var hasOffset = organ.Offset != Vector2.Zero;
            var hasScale = organ.Scale != 1f;
            var hasGlow = organ.Glow > 0f || organ.DetailGlow > 0f;

            if (hasColors || hasOffset || hasScale || hasGlow || organ.SkinTone)
            {
                part += $":{organ.Color?.ToHex() ?? "-"}:{organ.DetailColor?.ToHex() ?? "-"}";
                part += ":" + (hasOffset
                    ? organ.Offset.X.ToString(CultureInfo.InvariantCulture) + ","
                        + organ.Offset.Y.ToString(CultureInfo.InvariantCulture)
                    : "-");
                part += ":" + (hasScale ? organ.Scale.ToString(CultureInfo.InvariantCulture) : "-");
                part += ":" + (hasGlow
                    ? organ.Glow.ToString(CultureInfo.InvariantCulture) + ","
                        + organ.DetailGlow.ToString(CultureInfo.InvariantCulture)
                    : "-");
                part += ":" + (organ.SkinTone ? "1" : "0");
            }

            parts.Add(part);
        }

        parts.Add($"semen={SemenVolume}");
        return string.Join(';', parts);
    }

    public static GenitalOrganSettings FromDbString(string? raw)
    {
        var settings = new GenitalOrganSettings();

        if (string.IsNullOrWhiteSpace(raw))
            return settings;

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("semen=", StringComparison.Ordinal))
            {
                if (int.TryParse(part.Substring(6), out var volume))
                    settings.SemenVolume = Math.Clamp(volume, MinSemenVolume, MaxSemenVolume);
                continue;
            }

            var halves = part.Split(':');
            if (halves.Length is < 3 or > 10)
                continue;

            if (!Enum.TryParse<GenitalType>(halves[0], ignoreCase: true, out var type))
                continue;

            if (!int.TryParse(halves[2], out var size))
                continue;

            var visibility = GenitalVisibility.HiddenByUnderwear;
            if (halves.Length >= 4 &&
                Enum.TryParse<GenitalVisibility>(halves[3], ignoreCase: true, out var parsedVisibility))
            {
                visibility = parsedVisibility;
            }

            Color? color = null;
            Color? detailColor = null;
            if (halves.Length >= 5)
                color = ParseColor(halves[4]);
            if (halves.Length >= 6)
                detailColor = ParseColor(halves[5]);

            var offset = Vector2.Zero;
            if (halves.Length >= 7 && halves[6] != "-")
            {
                var offsetParts = halves[6].Split(',');
                if (offsetParts.Length == 2 &&
                    float.TryParse(offsetParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetX) &&
                    float.TryParse(offsetParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var offsetY))
                {
                    offset = new Vector2(offsetX, offsetY);
                }
            }

            var scale = 1f;
            if (halves.Length >= 8 && halves[7] != "-" &&
                float.TryParse(halves[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedScale))
            {
                scale = parsedScale;
            }

            var glow = 0f;
            var detailGlow = 0f;
            if (halves.Length >= 9 && halves[8] != "-")
            {
                var glowParts = halves[8].Split(',');
                if (glowParts.Length == 2)
                {
                    float.TryParse(glowParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out glow);
                    float.TryParse(glowParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out detailGlow);
                }
            }

            var skinTone = halves.Length >= 10 && halves[9] == "1";

            settings.Set(type, new GenitalOrganData
            {
                Type = type,
                Prototype = halves[1],
                Size = Math.Max(1, size),
                Visibility = visibility,
                Color = color,
                DetailColor = detailColor,
                Offset = offset,
                Scale = scale,
                Glow = glow,
                DetailGlow = detailGlow,
                SkinTone = skinTone,
            });
        }

        return settings;
    }

    private static Color? ParseColor(string value)
    {
        if (value.Length == 0 || value == "-")
            return null;

        return Color.TryFromHex(value);
    }

    public override bool Equals(object? obj)
    {
        return obj is GenitalOrganSettings other && MemberwiseEquals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SemenVolume);

        foreach (var organ in Organs.OrderBy(o => o.Type))
        {
            hash.Add(organ.Type);
            hash.Add(organ.Prototype);
            hash.Add(organ.Size);
        }

        return hash.ToHashCode();
    }
}

/// <summary>One selected genital organ: catalog type, 1-based size and visibility rule.</summary>
[DataDefinition]
[Serializable, NetSerializable]
public sealed partial class GenitalOrganData
{
    [DataField("type")]
    public GenitalType Type;

    [DataField("prototype")]
    public string Prototype = string.Empty;

    [DataField("size")]
    public int Size = 1;

    /// <summary>SPLURT-style exposure rule for this organ.</summary>
    [DataField("visibility")]
    public GenitalVisibility Visibility = GenitalVisibility.HiddenByUnderwear;

    /// <summary>Primary sprite color; null follows the mob's skin color.</summary>
    [DataField("color")]
    public Color? Color;

    /// <summary>Secondary/detail sprite color (e.g. nipples); null falls back to <see cref="Color"/>.</summary>
    [DataField("detailColor")]
    public Color? DetailColor;

    /// <summary>Sprite offset applied on top of the organ's render marking (like marking offsets).</summary>
    [DataField("offset")]
    public Vector2 Offset;

    /// <summary>Sprite scale multiplier applied to the organ's render marking (like marking scale).</summary>
    [DataField("scale")]
    public float Scale = 1f;

    /// <summary>Glow level (0-1) for the primary color group, like marking glow.</summary>
    [DataField("glow")]
    public float Glow;

    /// <summary>Glow level (0-1) for the detail color group (e.g. nipples).</summary>
    [DataField("detailGlow")]
    public float DetailGlow;

    /// <summary>
    /// Whether to draw the skin-toned render marking (baked-in skin shading) instead of the
    /// tinted one, when the organ's catalog offers one (breasts).
    /// </summary>
    [DataField("skinTone")]
    public bool SkinTone;

    public GenitalOrganData Clone()
    {
        return new GenitalOrganData
        {
            Type = Type,
            Prototype = Prototype,
            Size = Size,
            Visibility = Visibility,
            Color = Color,
            DetailColor = DetailColor,
            Offset = Offset,
            Scale = Scale,
            Glow = Glow,
            DetailGlow = DetailGlow,
            SkinTone = SkinTone,
        };
    }

    public bool MemberwiseEquals(GenitalOrganData? other)
    {
        return other != null &&
               Type == other.Type &&
               Prototype == other.Prototype &&
               Size == other.Size &&
               Visibility == other.Visibility &&
               Nullable.Equals(Color, other.Color) &&
               Nullable.Equals(DetailColor, other.DetailColor) &&
               Offset.Equals(other.Offset) &&
               Scale.Equals(other.Scale) &&
               Glow.Equals(other.Glow) &&
               DetailGlow.Equals(other.DetailGlow) &&
               SkinTone == other.SkinTone;
    }
}
