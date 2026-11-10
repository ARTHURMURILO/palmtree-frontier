namespace Content.Shared.Radiation.Events;

/// <summary>
///     Raised on entity when it was irradiated
///     by some radiation source.
/// </summary>
public readonly record struct OnIrradiatedEvent(
    float FrameTime,
    float RadsPerSecond,
    float PowerRadsPerSecond,
    EntityUid Origin)
{
    public readonly float FrameTime = FrameTime;

    public readonly float RadsPerSecond = RadsPerSecond;

    /// <summary>
    ///     The portion of <see cref="RadsPerSecond"/> that may be harvested by radiation
    ///     collectors. Hazard-only sources are excluded here.
    /// </summary>
    public readonly float PowerRadsPerSecond = PowerRadsPerSecond;

    public readonly EntityUid Origin = Origin;

    public float TotalRads => RadsPerSecond * FrameTime;

    public float PowerRads => PowerRadsPerSecond * FrameTime;
}
