using System.Numerics;
using Content.Client._PS.Consent; // Palmtree: NSFW age gate
using Content.Client.DisplacementMap;
using Content.Shared._CS; // Palmtree/Coyote: leg displacement
using Content.Shared.CCVar;
using Content.Shared.DisplacementMap; // Palmtree/Coyote: leg displacement
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared._PS.Organs; // Palmtree
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, AfterAutoHandleStateEvent>(OnHandleState);
        Subs.CVar(_configurationManager, CCVars.AccessibilityClientCensorNudity, OnCvarChanged, true);
        Subs.CVar(_configurationManager, CCVars.AccessibilityServerCensorNudity, OnCvarChanged, true);
        NsfwGate.Changed += OnNsfwGateChanged; // Palmtree
    }

    public override void Shutdown()
    {
        NsfwGate.Changed -= OnNsfwGateChanged; // Palmtree
        base.Shutdown();
    }

    private void OnHandleState(EntityUid uid, HumanoidAppearanceComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite((uid, component, Comp<SpriteComponent>(uid)));
    }

    /// <summary>
    /// Palmtree: rebuilds the sprite layers from the current marking set. Local visibility toggles
    /// (lobby preview undergarments) do not go through component state, so the editor calls this
    /// after changing <c>Marking.Visible</c>.
    /// </summary>
    public void RefreshSprite(EntityUid uid, HumanoidAppearanceComponent? component = null)
    {
        if (!Resolve(uid, ref component) || !TryComp<SpriteComponent>(uid, out var sprite))
            return;

        UpdateSprite((uid, component, sprite));
    }

    private void OnCvarChanged(bool value)
    {
        RefreshAllHumanoids();
    }

    // Palmtree: the NSFW gate adds/removes genital sprite layers, so every humanoid sprite must be
    // rebuilt when the player's consent state changes (lobby prompt answer or an admin flip).
    private void OnNsfwGateChanged()
    {
        RefreshAllHumanoids();
    }

    private void RefreshAllHumanoids()
    {
        var humanoidQuery = AllEntityQuery<HumanoidAppearanceComponent, SpriteComponent>();
        while (humanoidQuery.MoveNext(out var uid, out var humanoidComp, out var spriteComp))
        {
            UpdateSprite((uid, humanoidComp, spriteComp));
        }
    }

    private void UpdateSprite(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        UpdateLayers(entity);
        ApplyMarkingSet(entity);
        UpdateLayersAgain(entity); // Palmtree/Floof: hide base layers replaced by base markings

        var humanoidAppearance = entity.Comp1;
        var sprite = entity.Comp2;

        sprite[_sprite.LayerMapReserve((entity.Owner, sprite), HumanoidVisualLayers.Eyes)].Color = humanoidAppearance.EyeColor;

        // Palmtree: character height/width sliders scale the whole sprite.
        sprite.Scale = new Vector2(humanoidAppearance.Width, humanoidAppearance.Height);
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        var oldLayers = new HashSet<HumanoidVisualLayers>(component.BaseLayers.Keys);
        component.BaseLayers.Clear();
        component.HiddenBaseLayers.Clear(); // Palmtree/Floof

        // add default species layers
        var speciesProto = _prototypeManager.Index(component.Species);
        var baseSprites = _prototypeManager.Index(speciesProto.SpriteSet);
        foreach (var (key, id) in baseSprites.Sprites)
        {
            oldLayers.Remove(key);
            if (!component.CustomBaseLayers.ContainsKey(key))
                SetLayerData(entity, key, id, sexMorph: true);
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            SetLayerData(entity, key, info.Id, sexMorph: false, color: info.Color);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (_sprite.LayerMapTryGet((entity.Owner, sprite), key, out var index, false))
                sprite[index].Visible = false;
        }
    }

    private void SetLayerData(
        Entity<HumanoidAppearanceComponent, SpriteComponent> entity,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        var layerIndex = _sprite.LayerMapReserve((entity.Owner, sprite), key);
        var layer = sprite[layerIndex];
        layer.Visible = !IsHidden(component, key);

        if (color != null)
            layer.Color = color.Value;

        if (protoId == null)
            return;

        if (sexMorph)
            protoId = HumanoidVisualLayersExtension.GetSexMorph(key, component.Sex, protoId);

        var proto = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(protoId);
        component.BaseLayers[key] = proto;

        if (proto.MatchSkin)
            layer.Color = component.SkinColor.WithAlpha(proto.LayerAlpha);

        if (proto.BaseSprite != null)
        {
            var appropriateSprite = proto.BaseSprite;

            // Palmtree/Coyote: swap base sprites for the current leg style (e.g. digitigrade).
            if (component.LegStyle != HumanoidLegStyle.Plantigrade && proto.AltSprites.Count > 0)
            {
                if (proto.AltSprites.TryGetValue(component.LegStyle, out var altSprite)
                    || proto.AltSprites.TryGetValue(HumanoidLegStyle.Digitigrade, out altSprite))
                {
                    if (_prototypeManager.TryIndex(altSprite, out MarkingPrototype? altMarkingProto))
                    {
                        if (altMarkingProto.BaseLayerSprite is SpriteSpecifier.Rsi)
                            appropriateSprite = altMarkingProto.BaseLayerSprite;
                        else if (altMarkingProto.Sprites.Count > 0)
                            appropriateSprite = altMarkingProto.Sprites[0];
                    }
                }
            }

            _sprite.LayerSetSprite((entity.Owner, sprite), layerIndex, appropriateSprite);
        }
    }

    /// <summary>
    /// Palmtree/Floof: hides base layers that are replaced by base markings (e.g. species adaptors).
    /// </summary>
    private void UpdateLayersAgain(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var component = entity.Comp1;
        var sprite = entity.Comp2;

        foreach (var layer in component.HiddenBaseLayers)
        {
            if (_sprite.LayerMapTryGet((entity.Owner, sprite), layer, out var index, false))
                sprite[index].Visible = false;
        }
    }

    /// <summary>
    ///     Loads a profile directly into a humanoid.
    /// </summary>
    /// <param name="uid">The humanoid entity's UID</param>
    /// <param name="profile">The profile to load.</param>
    /// <param name="humanoid">The humanoid entity's humanoid component.</param>
    /// <remarks>
    ///     This should not be used if the entity is owned by the server. The server will otherwise
    ///     override this with the appearance data it sends over.
    /// </remarks>
    public override void LoadProfile(EntityUid uid, HumanoidCharacterProfile? profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (profile == null)
            return;

        if (!Resolve(uid, ref humanoid))
        {
            return;
        }

        var customBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>();

        var speciesPrototype = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var markings = new MarkingSet(speciesPrototype.MarkingPoints, _markingManager, _prototypeManager);

        // Add markings that doesn't need coloring. We store them until we add all other markings that doesn't need it.
        var markingFColored = new Dictionary<Marking, MarkingPrototype>();
        foreach (var marking in profile.Appearance.Markings)
        {
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                if (!prototype.ForcedColoring)
                {
                    markings.AddBack(prototype.MarkingCategory, marking);
                }
                else
                {
                    markingFColored.Add(marking, prototype);
                }
            }
        }

        // legacy: remove in the future?
        //markings.RemoveCategory(MarkingCategories.Hair);
        //markings.RemoveCategory(MarkingCategories.FacialHair);

        // We need to ensure hair before applying it or coloring can try depend on markings that can be invalid
        var hairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.Hair, out var hairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(hairAlpha)
            : profile.Appearance.HairColor;
        var hair = new Marking(profile.Appearance.HairStyleId,
            new[] { hairColor });

        var facialHairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.FacialHair, out var facialHairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(facialHairAlpha)
            : profile.Appearance.FacialHairColor;
        var facialHair = new Marking(profile.Appearance.FacialHairStyleId,
            new[] { facialHairColor });

        // Frontier: Match hair and facial hair colors to the forced color if it exists
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.Hair, out var forcedHairAlpha, _prototypeManager) is Color forcedHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedHairAlpha);
            hairColor = forcedHairColor;
        }
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.FacialHair, out var forcedFacialHairAlpha, _prototypeManager) is Color forcedFacialHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedFacialHairAlpha);
            facialHairColor = forcedFacialHairColor;
        }
        // End Frontier

        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, hair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.Hair, hair);
        }
        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, facialHair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.FacialHair, facialHair);
        }

        // Finally adding marking with forced colors
        foreach (var (marking, prototype) in markingFColored)
        {
            var markingColors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                profile.Appearance.SkinColor,
                profile.Appearance.EyeColor,
                markings
            );
            markings.AddBack(prototype.MarkingCategory, new Marking(marking.MarkingId, markingColors));
        }

        markings.EnsureSpecies(profile.Species, profile.Appearance.SkinColor, _markingManager, _prototypeManager);
        markings.EnsureSexes(profile.Sex, _markingManager);
        markings.EnsureDefault(
            profile.Appearance.SkinColor,
            profile.Appearance.EyeColor,
            _markingManager);

        DebugTools.Assert(IsClientSide(uid));

        humanoid.MarkingSet = markings;
        humanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>();
        humanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>();
        humanoid.CustomBaseLayers = customBaseLayers;
        humanoid.Sex = profile.Sex;
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.LegStyle = profile.Appearance.LegStyle; // Palmtree/Coyote
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor;
        humanoid.EyeColor = profile.Appearance.EyeColor;
        humanoid.Height = profile.Height; // Palmtree
        humanoid.Width = profile.Width; // Palmtree

        // Palmtree: profile-driven genital organs (lobby preview). The server path uses
        // HumanoidProfileAppliedEvent; this override does not call the shared LoadProfile.
        EntityManager.System<GenitalOrganSystem>().SyncFromProfile(uid, profile);

        UpdateSprite((uid, humanoid, Comp<SpriteComponent>(uid)));
    }

    private void ApplyMarkingSet(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;

        // I am lazy and I CBF resolving the previous mess, so I'm just going to nuke the markings.
        // Really, markings should probably be a separate component altogether.
        ClearAllMarkings(entity);

        var censorNudity = _configurationManager.GetCVar(CCVars.AccessibilityClientCensorNudity) ||
                           _configurationManager.GetCVar(CCVars.AccessibilityServerCensorNudity);
        // The reason we're splitting this up is in case the character already has undergarment equipped in that slot.
        var applyUndergarmentTop = censorNudity;
        var applyUndergarmentBottom = censorNudity;

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype))
                {
                    // Palmtree/Coyote: swap the marking for its alternate-leg-style version if one exists.
                    markingPrototype = GetMarkingForLegStyle(humanoid, markingPrototype);
                    ApplyMarking(markingPrototype, marking.MarkingColors, marking.Visible, entity, marking.MarkingScale, marking.MarkingOffset, marking.MarkingGlow, marking.RenderOverClothing);
                    if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentTop)
                        applyUndergarmentTop = false;
                    else if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentBottom)
                        applyUndergarmentBottom = false;
                }
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);

        AddUndergarments(entity, applyUndergarmentTop, applyUndergarmentBottom);
    }

    private void ClearAllMarkings(Entity<HumanoidAppearanceComponent, SpriteComponent> entity)
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;

        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, (entity, sprite));
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, (entity, sprite));
            }
        }
    }

    private void RemoveMarking(Marking marking, Entity<SpriteComponent> spriteEnt)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (!_sprite.LayerMapTryGet(spriteEnt.AsNullable(), layerId, out var index, false))
            {
                continue;
            }

            _sprite.LayerMapRemove(spriteEnt.AsNullable(), layerId);
            _sprite.RemoveLayer(spriteEnt.AsNullable(), index);

            // Palmtree/Coyote: remove the glow companion layer as well.
            var glowLayerId = $"{layerId}-glow";
            if (_sprite.LayerMapTryGet(spriteEnt.AsNullable(), glowLayerId, out var glowIndex, false))
            {
                _sprite.LayerMapRemove(spriteEnt.AsNullable(), glowLayerId);
                _sprite.RemoveLayer(spriteEnt.AsNullable(), glowIndex);
            }
        }
    }

    private void AddUndergarments(Entity<HumanoidAppearanceComponent, SpriteComponent> entity, bool undergarmentTop, bool undergarmentBottom)
    {
        var humanoid = entity.Comp1;

        if (undergarmentTop && humanoid.UndergarmentTop != null)
        {
            var marking = new Marking(humanoid.UndergarmentTop, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                // Markings are added to ClientOldMarkings because otherwise it causes issues when toggling the feature on/off.
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentTop, new List<Marking> { marking });
                ApplyMarking(prototype, null, true, entity);
            }
        }

        if (undergarmentBottom && humanoid.UndergarmentBottom != null)
        {
            var marking = new Marking(humanoid.UndergarmentBottom, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentBottom, new List<Marking> { marking });
                ApplyMarking(prototype, null, true, entity);
            }
        }
    }

    private void ApplyMarking(MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool visible,
        Entity<HumanoidAppearanceComponent, SpriteComponent> entity,
        // Palmtree/Coyote Start: advanced marking editor
        float scale = 1.0f,
        Vector2? offset = null,
        IReadOnlyList<float>? glowLevels = null,
        bool renderOverClothing = false)
        // Palmtree/Coyote End
    {
        var humanoid = entity.Comp1;
        var sprite = entity.Comp2;
        var markingOffset = offset ?? Vector2.Zero;

        // Palmtree: NSFW age gate. Genital sprites (organ render markings and the legacy genital
        // marking art) are never drawn for players who have not enabled NSFW content.
        if (markingPrototype.BodyPart == HumanoidVisualLayers.Genital && !NsfwGate.Allowed)
            return;

        // Palmtree/Floof: map sprite states to colors so colorLinks can copy colors between sprites.
        var colorDict = new Dictionary<string, Color>();
        for (var i = 0; i < markingPrototype.Sprites.Count; i++)
        {
            var spriteName = markingPrototype.Sprites[i] switch
            {
                SpriteSpecifier.Rsi rsi => rsi.RsiState,
                SpriteSpecifier.Texture texture => texture.TexturePath.Filename,
                _ => null
            };

            if (spriteName == null)
                continue;

            colorDict[spriteName] = colors != null && i < colors.Count ? colors[i] : Color.White;
        }

        // Palmtree/Coyote: map sprite states to glow levels.
        var glowDict = new Dictionary<string, float>();
        for (var i = 0; i < markingPrototype.Sprites.Count; i++)
        {
            var spriteName = markingPrototype.Sprites[i] switch
            {
                SpriteSpecifier.Rsi rsi => rsi.RsiState,
                SpriteSpecifier.Texture texture => texture.TexturePath.Filename,
                _ => null
            };

            if (spriteName == null)
                continue;

            glowDict[spriteName] = glowLevels != null && i < glowLevels.Count
                ? Math.Clamp(glowLevels[i], 0f, 1f)
                : 0f;
        }

        if (markingPrototype.ColorLinks != null)
        {
            foreach (var (child, parent) in markingPrototype.ColorLinks)
            {
                if (colorDict.TryGetValue(parent, out var linkedColor))
                    colorDict[child] = linkedColor;

                if (glowDict.TryGetValue(parent, out var linkedGlow))
                    glowDict[child] = linkedGlow;
            }
        }

        // Palmtree/Floof: track the sprite order for markings that place several sprites on one layer.
        var layerDict = new Dictionary<string, int>();

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            var markingSprite = markingPrototype.Sprites[j];

            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerSlot = markingPrototype.BodyPart;

            // Palmtree/Floof: a marking may route individual sprites to arbitrary layers.
            if (markingPrototype.Layering != null
                && markingPrototype.Layering.TryGetValue(rsi.RsiState, out var parsedLayer))
            {
                layerSlot = parsedLayer;
            }

            if (!_sprite.LayerMapTryGet((entity.Owner, sprite), layerSlot, out var targetLayer, false))
            {
                continue;
            }

            var layerVisible = visible;
            layerVisible &= !IsHidden(humanoid, layerSlot);
            layerVisible &= humanoid.BaseLayers.TryGetValue(layerSlot, out var setting)
               && setting.AllowsMarkings;

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";

            if (layerDict.TryGetValue(layerSlot.ToString(), out var layerIndex))
            {
                layerDict[layerSlot.ToString()] = layerIndex + 1;
            }
            else
            {
                layerDict.Add(layerSlot.ToString(), 0);
            }

            var targLayerAdj = targetLayer + layerDict[layerSlot.ToString()] + 1;

            // Palmtree: keep genital markings beneath clothing while they sit on the genital layer,
            // unless the organ uses the "Never hidden" visibility rule.
            if (layerSlot == HumanoidVisualLayers.Genital)
            {
                if (renderOverClothing)
                {
                    if (_sprite.LayerMapTryGet((entity.Owner, sprite), "outerClothing", out var overLayer, false))
                        targLayerAdj = Math.Max(targLayerAdj, overLayer + 1);
                }
                else
                {
                    if (_sprite.LayerMapTryGet((entity.Owner, sprite), "jumpsuit", out var jumpsuitLayer, false))
                        targLayerAdj = Math.Min(targLayerAdj, jumpsuitLayer - 1);

                    if (_sprite.LayerMapTryGet((entity.Owner, sprite), "outerClothing", out var outerClothingLayer, false))
                        targLayerAdj = Math.Min(targLayerAdj, outerClothingLayer - 1);

                    if (targLayerAdj < 0)
                        targLayerAdj = 0;
                }
            }

            if (!_sprite.LayerMapTryGet((entity.Owner, sprite), layerId, out _, false))
            {
                var layer = _sprite.AddLayer((entity.Owner, sprite), markingSprite, targLayerAdj);
                _sprite.LayerMapSet((entity.Owner, sprite), layerId, layer);
                _sprite.LayerSetSprite((entity.Owner, sprite), layerId, rsi);
            }

            // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
            if (markingPrototype.Shader != null)
            {
                // use spriteComponent's layersetshader function to set the layer's shader to that which is specified.
                sprite.LayerSetShader(layerId, markingPrototype.Shader);
            }
            // impstation edit end

            _sprite.LayerSetVisible((entity.Owner, sprite), layerId, layerVisible);
            // Palmtree/Coyote Start: advanced marking editor
            sprite.LayerSetScale(layerId, new Vector2(scale, scale));
            sprite.LayerSetOffset(layerId, markingOffset);
            // Palmtree/Coyote End

            if (!layerVisible || setting == null) // this is kinda implied
            {
                // Palmtree/Coyote: clean up any glow layer when the marking is hidden.
                var hiddenGlowId = $"{layerId}-glow";
                if (_sprite.LayerMapTryGet((entity.Owner, sprite), hiddenGlowId, out var hiddenGlowIndex, false))
                {
                    _sprite.LayerMapRemove((entity.Owner, sprite), hiddenGlowId);
                    _sprite.RemoveLayer((entity.Owner, sprite), hiddenGlowIndex);
                }
                continue;
            }

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct.
            // So if that happens just default to white?
            var color = colorDict.TryGetValue(rsi.RsiState, out var targetColor) ? targetColor : Color.White;
            var glowFactor = glowDict.TryGetValue(rsi.RsiState, out var targetGlow) ? targetGlow : 0f;
            var clampedGlow = Math.Clamp(glowFactor, 0f, 1f);
            var glowLayerId = $"{layerId}-glow";

            // Palmtree/Coyote: split alpha between the base and glow layers so the composed
            // result keeps the original alpha. total = base + glow * (1 - base).
            var baseAlpha = color.A;
            var glowAlpha = baseAlpha * clampedGlow;
            var denom = 1f - glowAlpha;
            var baseLayerAlpha = denom > 0f ? (baseAlpha - glowAlpha) / denom : 0f;
            baseLayerAlpha = Math.Clamp(baseLayerAlpha, 0f, 1f);

            _sprite.LayerSetColor((entity.Owner, sprite), layerId, color.WithAlpha(baseLayerAlpha));

            if (clampedGlow > 0f)
            {
                if (!_sprite.LayerMapTryGet((entity.Owner, sprite), glowLayerId, out _, false))
                {
                    var glowLayer = _sprite.AddLayer((entity.Owner, sprite), markingSprite, targLayerAdj + 1);
                    _sprite.LayerMapSet((entity.Owner, sprite), glowLayerId, glowLayer);
                    _sprite.LayerSetSprite((entity.Owner, sprite), glowLayerId, rsi);
                }

                sprite.LayerSetShader(glowLayerId, "unshaded");
                _sprite.LayerSetVisible((entity.Owner, sprite), glowLayerId, layerVisible);
                _sprite.LayerSetColor((entity.Owner, sprite), glowLayerId, color.WithAlpha(glowAlpha));
                sprite.LayerSetScale(glowLayerId, new Vector2(scale, scale));
                sprite.LayerSetOffset(glowLayerId, markingOffset);
            }
            else if (_sprite.LayerMapTryGet((entity.Owner, sprite), glowLayerId, out var glowIndex, false))
            {
                _sprite.LayerMapRemove((entity.Owner, sprite), glowLayerId);
                _sprite.RemoveLayer((entity.Owner, sprite), glowIndex);
            }

            if (humanoid.MarkingsDisplacement.TryGetValue(markingPrototype.BodyPart, out var displacementData) && markingPrototype.CanBeDisplaced)
            {
                _displacement.TryAddDisplacement(displacementData, (entity.Owner, sprite), targLayerAdj + 1, layerId, out _);
            }
        }

        // Palmtree/Floof: base markings (e.g. species adaptors) hide the base layer they replace.
        if (MarkingCategoriesConversion.Category2Layer(
                markingPrototype.MarkingCategory,
                markingPrototype.BodyPart,
                out var baseLayerToHide))
        {
            if (!humanoid.HiddenBaseLayers.Contains(baseLayerToHide))
                humanoid.HiddenBaseLayers.Add(baseLayerToHide);
        }
    }

    /// <summary>
    /// Palmtree/Coyote: returns the alternate marking prototype for the humanoid's leg style, if any.
    /// </summary>
    private MarkingPrototype GetMarkingForLegStyle(HumanoidAppearanceComponent humanoid, MarkingPrototype markingPrototype)
    {
        if (humanoid.LegStyle == HumanoidLegStyle.Plantigrade
            || markingPrototype.AlternateSprites.Count == 0)
        {
            return markingPrototype;
        }

        if (markingPrototype.AlternateSprites.TryGetValue(humanoid.LegStyle, out var altMarkingProtoId)
            || (humanoid.LegStyle != HumanoidLegStyle.Digitigrade
                && markingPrototype.AlternateSprites.TryGetValue(HumanoidLegStyle.Digitigrade, out altMarkingProtoId)))
        {
            if (_prototypeManager.TryIndex(altMarkingProtoId, out MarkingPrototype? altPrototype))
                return altPrototype;
        }

        return markingPrototype;
    }

    public override void SetSkinColor(EntityUid uid, Color skinColor, bool sync = true, bool verify = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid) || humanoid.SkinColor == skinColor)
            return;

        base.SetSkinColor(uid, skinColor, false, verify, humanoid);

        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
                continue;

            var index = _sprite.LayerMapReserve((uid, sprite), layer);
            sprite[index].Color = skinColor.WithAlpha(spriteInfo.LayerAlpha);
        }
    }

    public override void SetLayerVisibility(
        Entity<HumanoidAppearanceComponent> ent,
        HumanoidVisualLayers layer,
        bool visible,
        SlotFlags? slot,
        ref bool dirty)
    {
        base.SetLayerVisibility(ent, layer, visible, slot, ref dirty);

        var sprite = Comp<SpriteComponent>(ent);
        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), layer, out var index, false))
        {
            if (!visible)
                return;
            index = _sprite.LayerMapReserve((ent.Owner, sprite), layer);
        }

        var spriteLayer = sprite[index];
        if (spriteLayer.Visible == visible)
            return;

        spriteLayer.Visible = visible;

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                    ApplyMarking(markingPrototype, marking.MarkingColors, marking.Visible, (ent, ent.Comp, sprite), marking.MarkingScale, marking.MarkingOffset, marking.MarkingGlow, marking.RenderOverClothing);
            }
        }
    }

    // Palmtree/Coyote: override clothing displacement maps based on the humanoid's leg style.
    public void GetDisplacementForLegStyle(
        EntityUid uid,
        string slot,
        HumanoidAppearanceComponent? humanoidAppearance,
        DisplacementData? baseDisplacementDataIn,
        DisplacementData? maleDisplacementDataIn,
        DisplacementData? femaleDisplacementDataIn,
        out DisplacementData? baseDisplacementData,
        out DisplacementData? maleDisplacementData,
        out DisplacementData? femaleDisplacementData)
    {
        baseDisplacementData = baseDisplacementDataIn;
        maleDisplacementData = maleDisplacementDataIn;
        femaleDisplacementData = femaleDisplacementDataIn;

        if (!Resolve(uid, ref humanoidAppearance))
            return;

        if (!_prototypeManager.TryIndex(humanoidAppearance.Species, out SpeciesPrototype? species)
            || !species.AllowDigilegDisplacement)
        {
            return;
        }

        if (!humanoidAppearance.LegDisplacements.TryGetValue(
                humanoidAppearance.LegStyle,
                out ProtoId<LegDisplacementPrototype> displacement))
        {
            return;
        }

        if (!_prototypeManager.TryIndex(displacement, out LegDisplacementPrototype? legDisplacement))
            return;

        baseDisplacementData = legDisplacement.Displacements.GetValueOrDefault(slot, baseDisplacementDataIn);
        maleDisplacementData = legDisplacement.MaleDisplacements.GetValueOrDefault(slot, maleDisplacementDataIn);
        femaleDisplacementData = legDisplacement.FemaleDisplacements.GetValueOrDefault(slot, femaleDisplacementDataIn);
    }
}
