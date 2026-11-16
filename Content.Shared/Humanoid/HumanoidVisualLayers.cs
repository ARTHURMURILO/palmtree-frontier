using Content.Shared.Humanoid.Markings;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid
{
    [Serializable, NetSerializable]
    public enum HumanoidVisualLayers : byte
    {
        Special, // for the cat ears
        Tail,
        TailExtras, // Palmtree/Starlight
        Hair,
        FacialHair,
        UndergarmentTop,
        UndergarmentBottom,
        Genital, // Palmtree: genital markings layer
        Chest,
        Head,
        Snout,
        SnoutCover, // things layered over snouts (i.e. noses)
        HeadSide, // side parts (i.e., frills)
        HeadTop,  // top parts (i.e., ears)
        TailBehind, // Palmtree/Floof: markings that render behind the body
        TailOversuit, // Palmtree/Floof: markings that render over the suit
        NeckFluff, // Palmtree/TheDen: Ovinia neck fluff
        Eyes,
        RArm,
        LArm,
        RHand,
        LHand,
        RLegBehind, // Palmtree/Floof: behind-leg layers for digitigrade legs
        RLeg,
        LLegBehind,
        LLeg,
        RFootBehind,
        RFoot,
        LFootBehind,
        LFoot,
        Handcuffs,
        StencilMask,
        Ensnare,
        Fire,
        LArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings
        RArmExtension, // Frontier: a species-specific extension layer, e.g. for harpy wings
        UndershirtUnderclothes, // Palmtree/Floof
        UndershirtOverclothes, // Palmtree/Floof
        Disregard, // Palmtree/Floof: used as a null target for base-layer conversions
        GenitalUnder, // Palmtree: genital sublayer below the neck/breast group but above body markings
        GenitalLow, // Palmtree: genital sublayer below clothing but above body/leg markings
    }
}
