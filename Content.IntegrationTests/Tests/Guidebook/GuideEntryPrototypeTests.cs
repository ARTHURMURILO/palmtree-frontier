using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Robust.Shared.ContentPack;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using System.Linq;
using Content.Shared.Guidebook;

namespace Content.IntegrationTests.Tests.Guidebook;

[TestFixture]
[TestOf(typeof(GuidebookSystem))]
[TestOf(typeof(GuideEntryPrototype))]
[TestOf(typeof(DocumentParsingManager))]
public sealed class GuideEntryPrototypeTests
{
    [Test]
    public async Task ValidatePrototypeContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitIdleAsync();
        var protoMan = client.ResolveDependency<IPrototypeManager>();
        var resMan = client.ResolveDependency<IResourceManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();
        var prototypes = protoMan.EnumeratePrototypes<GuideEntryPrototype>().ToList();

        // Palmtree: the Chemicals page embeds every reagent and each of its reactions, which queues more
        // style updates in a single frame than the client's limit (25k). That is a client perf warning,
        // not a content error, and this test only cares that the markup parses - keep the ui sawmill
        // from failing the run over it.
        var uiSawmill = client.ResolveDependency<ILogManager>().GetSawmill("ui");
        var previousLevel = uiSawmill.Level;
        uiSawmill.Level = LogLevel.Error;

        try
        {
            foreach (var proto in prototypes)
            {
                await client.WaitAssertion(() =>
                {
                    using var reader = resMan.ContentFileReadText(proto.Text);
                    var text = reader.ReadToEnd();
                    Assert.That(parser.TryAddMarkup(new Document(), text), $"Failed to parse guidebook: {proto.Id}");
                });

                // Avoid styleguide update limit
                await client.WaitRunTicks(1);
            }
        }
        finally
        {
            uiSawmill.Level = previousLevel;
        }

        await pair.CleanReturnAsync();
    }
}
