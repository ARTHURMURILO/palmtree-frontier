using System.Linq;
using System.Text;
using Content.Shared.Administration;
using Content.Shared.Chat.Prototypes;
using Robust.Shared.Console;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server.Chat.Commands;

/// <summary>
/// Palmtree: dumps every chat-triggerable emote and its third-person text as
/// [*emote = "text"] entries, three per console line.
/// </summary>
[AnyCommand]
public sealed class EmoteListCommand : IConsoleCommand
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly ILocalizationManager _loc = default!;

    public string Command => "emotelist";
    public string Description => "Lists chat emotes and their text, three per line.";
    public string Help => "Usage: emotelist";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        // Use the caller's entity for pronoun-aware text. The server console has no entity, so pass
        // a plain string instead: Fluent's GENDER() then falls back to neuter without looking up
        // MetaDataComponent on EntityUid.Invalid (which logs an error).
        object entity = shell.Player?.AttachedEntity is { } attached ? attached : "it";
        var entries = new List<(string Trigger, string Text)>();

        foreach (var emote in _prototypeManager.EnumeratePrototypes<EmotePrototype>())
        {
            if (emote.ChatTriggers.Count == 0 || emote.ChatMessages.Count == 0)
                continue;

            // Prefer the prototype id as the canonical trigger (e.g. *scream, not *yell);
            // fall back to the shortest alias for emotes whose id is not a chat trigger.
            var idTrigger = emote.ID.ToLowerInvariant();
            var trigger = emote.ChatTriggers.Contains(idTrigger)
                ? idTrigger
                : emote.ChatTriggers
                    .OrderBy(trigger => trigger.Length)
                    .ThenBy(trigger => trigger, StringComparer.Ordinal)
                    .First();

            var text = _loc.GetString(emote.ChatMessages[0], ("entity", entity));
            entries.Add((trigger, text));
        }

        entries.Sort((a, b) => string.Compare(a.Trigger, b.Trigger, StringComparison.Ordinal));

        var line = new StringBuilder();
        var count = 0;
        foreach (var (trigger, text) in entries)
        {
            if (count > 0)
                line.Append(" | ");

            line.Append("[*")
                .Append(trigger)
                .Append(" = \"")
                .Append(text.Replace("\"", "\\\""))
                .Append("\"]");

            count++;
            if (count < 3)
                continue;

            shell.WriteLine(line.ToString());
            line.Clear();
            count = 0;
        }

        if (line.Length > 0)
            shell.WriteLine(line.ToString());
    }
}
