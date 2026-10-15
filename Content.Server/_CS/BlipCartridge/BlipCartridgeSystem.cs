using Content.Shared._CS.BlipCartridge;
using Content.Shared._NF.Radar;
using Content.Shared.CartridgeLoader;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;

namespace Content.Server._CS.BlipCartridge;

/// <summary>
/// This system handles the Blip Cartridge, which adds a radar blip for your PDA!
/// You can customize it too!
/// </summary>
public sealed class BlipCartridgeSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public static readonly VerbCategory BlipPresetCat =
        new("verb-categories-blip-preset", null);

    public static readonly VerbCategory BlipColorCat =
        new("verb-categories-blip-color", null);

    public static readonly VerbCategory BlipShapeCat =
        new("verb-categories-blip-shape", null);

    public static readonly VerbCategory BlipSizeCat =
        new("verb-categories-blip-size", null);

    public static readonly VerbCategory BlipToggleCat =
        new("verb-categories-blip-toggle", null);

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();
        // Palmtree: MapInit, not ComponentInit - spawning the cartridge on an un-initialized map (or
        // loading one) must not modify component data (PrototypeSaveTest). MapInit runs on real
        // spawns and after loading, so the blip still gets its preset data.
        SubscribeLocalEvent<BlipCartridgeComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BlipCartridgeComponent, CartridgeAddedEvent>(OnCartridgeAdded);
        SubscribeLocalEvent<BlipCartridgeComponent, CartridgeRemovedEvent>(OnCartridgeRemoved);
        SubscribeLocalEvent<BlipCartridgeComponent, GetVerbsEvent<Verb>>(GetVerbs);
        // SubscribeLocalEvent<BlipCartridgeComponent, RadarBlipEvent>(UpdateBlipData); // todo, make it flash in crit
    }

    private void OnMapInit(Entity<BlipCartridgeComponent> ent, ref MapInitEvent args)
    {
        // Load the initial data from the cartridge component to the blip component
        EnsureComp<RadarBlipComponent>(ent.Owner);
        LoadStoredBlipData(ent, true);
    }

    private void OnCartridgeAdded(Entity<BlipCartridgeComponent> ent, ref CartridgeAddedEvent args)
    {
        EnsureComp<RadarBlipComponent>(args.Loader);
        LoadStoredBlipData(ent, true);
    }

    private void OnCartridgeRemoved(Entity<BlipCartridgeComponent> ent, ref CartridgeRemovedEvent args)
    {
        RemComp<RadarBlipComponent>(args.Loader);
    }

    /// <summary>
    /// Take the data from the BlipCartridgeComponent and apply it to the RadarBlipComponent.
    /// </summary>
    private void LoadStoredBlipData(Entity<BlipCartridgeComponent> comp, bool initial = false)
    {
        var blip = EnsureComp<RadarBlipComponent>(comp.Owner);
        var cartridge = comp.Comp;
        if (initial)
        {
            ApplyPresetBlipData(
                blip,
                cartridge,
                cartridge.DefaultPreset);
            blip.Enabled = cartridge.Enabled;
        }
        else
        {
            LoadBlipColorData(blip, cartridge);
            LoadBlipShapeData(blip, cartridge);
            LoadBlipScaleData(blip, cartridge);
        }

        LoadDefaultBlipData(blip, cartridge);
    }

    private void ApplyPresetBlipData(
        RadarBlipComponent blip,
        BlipCartridgeComponent cartridge,
        ProtoId<RadarBlipPresetPrototype> presetProto)
    {
        var safety = 3; // Safety counter to prevent infinite loops
        while (safety-- > 0)
        {
            if (_prototype.TryIndex(presetProto, out var preset))
            {
                cartridge.BlipColor = preset.ColorSet;
                cartridge.BlipShape = preset.ShapeSet;
                cartridge.Scale = preset.Scale;
                LoadBlipColorData(blip, cartridge);
                LoadBlipShapeData(blip, cartridge);
                LoadBlipScaleData(blip, cartridge);
                cartridge.CurrentPreset = presetProto; // Update the current preset
            }
            else
            {
                Log.Warning(
                    $"BlipCartridge {cartridge} has an invalid RadarBlipPreset: "
                    + $"{presetProto}. Using default preset.");
                presetProto = "BlipPresetCivilian";
                continue;
            }

            return;
        }

        Log.Error($"Failed to load RadarBlipPreset after multiple attempts for cartridge {cartridge}.");
        blip.RadarColor = Color.Red; // Fallback color
        blip.HighlightedRadarColor = Color.OrangeRed; // Fallback highlighted color
        blip.Shape = RadarBlipShape.Circle; // Fallback shape
        blip.Scale = 1f; // Fallback scale
    }

    /// <summary>
    /// Takes the prototypes from the BlipCartridgeComponent and applies them to the RadarBlipComponent.
    /// </summary>
    private void LoadBlipColorData(RadarBlipComponent blip, BlipCartridgeComponent cartridge)
    {
        var safety = 3; // Safety counter to prevent infinite loops
        while (safety-- > 0)
        {
            if (_prototype.TryIndex(cartridge.BlipColor, out var colorSet))
            {
                blip.RadarColor = Color.FromName(colorSet.Color);
                blip.HighlightedRadarColor = Color.FromName(colorSet.HighlightedColor);
            }
            else
            {
                Log.Warning(
                    $"BlipCartridge {cartridge} has an invalid RadarBlipColorSet: "
                    + $"{cartridge.BlipColor}. Using default color.");
                cartridge.BlipColor = "BlipColorGreen"; // Default color set
                continue;
            }

            return; // Exit the loop if we successfully loaded the color set
        }

        Log.Error($"Failed to load BlipColorSet after multiple attempts for cartridge {cartridge}.");
        blip.RadarColor = Color.Red; // Fallback color
        blip.HighlightedRadarColor = Color.OrangeRed; // Fallback highlighted color
    }

    /// <summary>
    /// Takes the shape from the BlipCartridgeComponent and applies it to the RadarBlipComponent.
    /// </summary>
    private void LoadBlipShapeData(RadarBlipComponent blip, BlipCartridgeComponent cartridge)
    {
        if (_prototype.TryIndex(cartridge.BlipShape, out var shapeSet))
        {
            blip.Shape = Enum.Parse<RadarBlipShape>(shapeSet.Shape, true);
        }
        else
        {
            Log.Warning(
                $"BlipCartridge {cartridge} has an invalid RadarBlipShapeSet: "
                + $"{cartridge.BlipShape}. Using default shape.");
            blip.Shape = RadarBlipShape.Circle;
        }
    }

    /// <summary>
    /// Sets the scale of the blip.
    /// </summary>
    private void LoadBlipScaleData(RadarBlipComponent blip, BlipCartridgeComponent cartridge)
    {
        blip.Scale = cartridge.Scale;
    }

    /// <summary>
    /// Applies the default blip data for a PDA.
    /// </summary>
    private void LoadDefaultBlipData(RadarBlipComponent blip, BlipCartridgeComponent cartridge)
    {
        blip.RequireNoGrid = false;
        blip.VisibleFromOtherGrids = true; // PDA blips are visible from other grids.
    }

    /// <summary>
    /// Sets the blip to enabled or disabled.
    /// </summary>
    private void ToggleBlip(Entity<BlipCartridgeComponent> ent, RadarBlipComponent radBlip)
    {
        radBlip.Enabled = !radBlip.Enabled;
        LoadStoredBlipData(ent);
    }

    /// <summary>
    /// Changes the blip preset to the given preset.
    /// </summary>
    private void ChangeBlipPreset(Entity<BlipCartridgeComponent> ent, ProtoId<RadarBlipPresetPrototype> presetProto)
    {
        var blipData = ent.Comp;
        ApplyPresetBlipData(
            EnsureComp<RadarBlipComponent>(ent.Owner),
            blipData,
            presetProto);
    }

    /// <summary>
    /// Changes the blip color to the given color.
    /// </summary>
    private void ChangeBlipColor(Entity<BlipCartridgeComponent> ent, ProtoId<BlipColorSetPrototype> colorProto)
    {
        var blipData = ent.Comp;
        blipData.BlipColor = colorProto;
        LoadStoredBlipData(ent);
    }

    /// <summary>
    /// Changes the blip shape to the given shape.
    /// </summary>
    private void ChangeBlipShape(Entity<BlipCartridgeComponent> ent, ProtoId<BlipShapeSetPrototype> shapeProto)
    {
        var blipData = ent.Comp;
        blipData.BlipShape = shapeProto;
        LoadStoredBlipData(ent);
    }

    /// <summary>
    /// Changes the blip scale to the given scale.
    /// </summary>
    private void ChangeBlipScale(Entity<BlipCartridgeComponent> ent, float scale)
    {
        var blipData = ent.Comp;
        blipData.Scale = scale;
        LoadStoredBlipData(ent);
    }

    private void GetVerbs(Entity<BlipCartridgeComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        var blipData = ent.Comp;
        var radBlip = EnsureComp<RadarBlipComponent>(ent.Owner);

        // Toggle the blip on or off.
        var toggleBlipVerb = new Verb()
        {
            Text = radBlip.Enabled ? "ON" : "OFF",
            Category = BlipToggleCat,
            Act = () =>
            {
                ToggleBlip(ent, radBlip);
            },
        };
        args.Verbs.Add(toggleBlipVerb);

        // Switch between presets.
        foreach (var preset in blipData.Presets)
        {
            _prototype.TryIndex(preset, out RadarBlipPresetPrototype? presetProto);
            if (presetProto == null)
                continue;
            var presetVerb = new Verb()
            {
                Text = $"{presetProto.Name}",
                Category = BlipPresetCat,
                Act = () =>
                {
                    ChangeBlipPreset(ent, preset);
                },
            };
            args.Verbs.Add(presetVerb);
        }

        // Switch between colors.
        foreach (var color in blipData.ColorTable)
        {
            _prototype.TryIndex(color, out BlipColorSetPrototype? colorProto);
            if (colorProto == null)
                continue;
            var colorVerb = new Verb()
            {
                Text = $"{colorProto.Name}",
                Category = BlipColorCat,
                Act = () =>
                {
                    ChangeBlipColor(ent, color);
                },
            };
            args.Verbs.Add(colorVerb);
        }

        // Switch between shapes.
        foreach (var shape in blipData.ShapeTable)
        {
            _prototype.TryIndex(shape, out BlipShapeSetPrototype? shapeProto);
            if (shapeProto == null)
                continue;
            var shapeVerb = new Verb()
            {
                Text = $"{shapeProto.Name}",
                Category = BlipShapeCat,
                Act = () =>
                {
                    ChangeBlipShape(ent, shape);
                },
            };
            args.Verbs.Add(shapeVerb);
        }

        // Switch between scales.
        List<float> scales = new()
        {
            0.5f,
            1f,
            1.5f,
            2f,
            2.5f,
            3f,
            3.5f,
            4f,
        };
        foreach (var scale in scales)
        {
            // The floats may render as 1.499999999999999, so format them to one decimal.
            var scaleString = scale.ToString("0.0");
            var scaleVerb = new Verb()
            {
                Text = $"x{scaleString}",
                Category = BlipSizeCat,
                Act = () =>
                {
                    ChangeBlipScale(ent, scale);
                },
            };
            args.Verbs.Add(scaleVerb);
        }
    }
}
