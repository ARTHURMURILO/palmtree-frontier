using System.Numerics;
using Content.Shared._NF.Shuttles.Events;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Shuttles.Components
{
    [RegisterComponent]
    public sealed partial class ShuttleConsoleComponent : SharedShuttleConsoleComponent
    {
        [ViewVariables]
        public readonly List<EntityUid> SubscribedPilots = new();

        /// <summary>
        /// How much should the pilot's eye be zoomed by when piloting using this console?
        /// </summary>
        [DataField("zoom")]
        public Vector2 Zoom = new(1.5f, 1.5f);

        /// <summary>
        /// Should this console have access to restricted FTL destinations?
        /// </summary>
        [ViewVariables(VVAccess.ReadWrite), DataField("whitelistSpecific")]
        public List<EntityUid> FTLWhitelist = new List<EntityUid>();

        // Frontier: EMP-related state
        /// <summary>
        /// For EMP to allow keeping the shuttle off
        /// </summary>
        [DataField("enabled")]
        public bool MainBreakerEnabled = true;

        /// <summary>
        ///     While disabled by EMP
        /// </summary>
        [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
        public TimeSpan TimeoutFromEmp = TimeSpan.Zero;

        [DataField]
        public float DisableDuration = 60f;

        [DataField]
        public InertiaDampeningMode DampeningMode = InertiaDampeningMode.Dampen;

        // Palmtree/Coyote: radar proximity alert state
        [DataField]
        public bool ProximityAlertEnabled = false;

        [DataField]
        public float ProximityAlertRadius = 128f;

        [ViewVariables]
        public TimeSpan NextProximityAlert = TimeSpan.Zero;

        [ViewVariables]
        public TimeSpan NextProximityScan = TimeSpan.Zero;

        [ViewVariables]
        public bool HasProximityContact = false;

        [ViewVariables]
        public float CachedNearestProximityDistance = float.MaxValue;
        // End Palmtree/Coyote
        // End Frontier
    }
}
