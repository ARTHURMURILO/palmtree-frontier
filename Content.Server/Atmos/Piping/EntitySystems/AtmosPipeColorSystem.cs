using Content.Server.Atmos.Piping.Components;
using Content.Shared.Atmos.Piping;
using Content.Shared.RCD; // Palmtree: RPD pipe colouring
using Robust.Server.GameObjects;

namespace Content.Server.Atmos.Piping.EntitySystems
{
    public sealed class AtmosPipeColorSystem : EntitySystem
    {
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<AtmosPipeColorComponent, ComponentStartup>(OnStartup);
            SubscribeLocalEvent<AtmosPipeColorComponent, ComponentShutdown>(OnShutdown);
            SubscribeLocalEvent<RCDPipeColorAppliedEvent>(OnRCDPipeColorApplied); // Palmtree: RPD pipe colouring
        }

        // Palmtree: an RPD built a pipe with a colour selected, so adopt it as the functional colour.
        // The shared RCD system already set the appearance datum; this keeps the component (which atmos
        // monitoring consoles and the spray painter read) in sync with it.
        private void OnRCDPipeColorApplied(EntityUid uid, ref RCDPipeColorAppliedEvent args)
        {
            if (!TryComp<AtmosPipeColorComponent>(uid, out var component))
                return;

            SetColor(uid, component, args.Color);
        }

        private void OnStartup(EntityUid uid, AtmosPipeColorComponent component, ComponentStartup args)
        {
            if (!TryComp(uid, out AppearanceComponent? appearance))
                return;

            _appearance.SetData(uid, PipeColorVisuals.Color, component.Color, appearance);
        }

        private void OnShutdown(EntityUid uid, AtmosPipeColorComponent component, ComponentShutdown args)
        {
            if (!TryComp(uid, out AppearanceComponent? appearance))
                return;

            _appearance.SetData(uid, PipeColorVisuals.Color, Color.White, appearance);
        }

        public void SetColor(EntityUid uid, AtmosPipeColorComponent component, Color color)
        {
            component.Color = color;

            if (!TryComp(uid, out AppearanceComponent? appearance))
                return;

            _appearance.SetData(uid, PipeColorVisuals.Color, color, appearance);

            var ev = new AtmosPipeColorChangedEvent(color);
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
