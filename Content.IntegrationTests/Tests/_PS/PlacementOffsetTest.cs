using Content.Shared._PS.CCVar;
using NUnit.Framework;
using Robust.Client.Placement;
using Robust.Shared.Configuration;
using Robust.Shared.IoC;

namespace Content.IntegrationTests.Tests._PS;

/// <summary>
/// Palmtree: the SnapgridCenterOffset placement mode (fractional offset on top of the tile-center
/// snap) is registered with the client placement manager - the same list every placement mode
/// dropdown is built from - and its offset CVars start at zero.
/// </summary>
[TestFixture]
public sealed class PlacementOffsetTest
{
    [Test]
    public async Task OffsetModeIsRegistered()
    {
        await using var pair = await PoolManager.GetServerClient();

        await pair.Client.WaitAssertion(() =>
        {
            var placement = pair.Client.ResolveDependency<IPlacementManager>();
            Assert.That(placement.AllModeNames, Does.Contain("SnapgridCenterOffset"),
                "SnapgridCenterOffset should be selectable in the spawn menu and map editor.");

            var cfg = pair.Client.ResolveDependency<IConfigurationManager>();
            Assert.That(cfg.GetCVar(PSCVars.PlacementOffsetX), Is.EqualTo(0f),
                "The X placement offset should default to zero.");
            Assert.That(cfg.GetCVar(PSCVars.PlacementOffsetY), Is.EqualTo(0f),
                "The Y placement offset should default to zero.");
        });

        await pair.CleanReturnAsync();
    }
}
