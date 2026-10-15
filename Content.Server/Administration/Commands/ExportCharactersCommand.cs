using System;
using System.IO;
using System.Linq;
using Content.Server.Database;
using Content.Shared.Administration;
using Content.Shared.Humanoid;
using Robust.Shared.Console;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Server.Administration.Commands;

/// <summary>
/// Palmtree: exports every saved character profile in the database to YAML files,
/// one folder per ckey, one file per character slot. The output uses the same format
/// as the character editor's export button, so files can be re-imported in-game.
/// </summary>
[AdminCommand(AdminFlags.Server)]
public sealed class ExportCharactersCommand : IConsoleCommand
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly IEntitySystemManager _entSys = default!;
    [Dependency] private readonly IResourceManager _res = default!;

    public string Command => "exportcharacters";
    public string Description => "Exports every saved character profile from the database to YAML files grouped by ckey.";
    public string Help => "exportcharacters [directory] - directory is relative to the server user data folder (default: exported_characters).";

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var directory = args.Length > 0 ? args[0] : "exported_characters";
        // Palmtree: IWritableDirProvider only accepts rooted ResPaths, and a relative argument
        // like "exported_characters" threw "Path must be rooted" for every profile in the loop.
        var root = ResPath.FromRelativeSystemPath(directory).ToRootedPath();
        var appearance = _entSys.GetEntitySystem<SharedHumanoidAppearanceSystem>();

        try
        {
            var profiles = await _db.GetAllCharacterProfiles();

            foreach (var (userName, slot, profile) in profiles)
            {
                var dir = root / Sanitize(userName);
                _res.UserData.CreateDir(dir);

                await using var writer = _res.UserData.OpenWriteText(dir / $"character-{slot}.yml");
                appearance.ToDataNode(profile).Write(writer);
            }

            var userCount = profiles.Select(p => p.UserName).Distinct().Count();
            var physical = _res.UserData.RootDir is { } userDataRoot
                ? Path.Combine(userDataRoot, root.ToRelativePath().ToString())
                : root.ToString();
            shell.WriteLine($"Exported {profiles.Count} character(s) for {userCount} player(s) to {physical}.");
        }
        catch (Exception e)
        {
            shell.WriteError($"Failed to export characters: {e}");
        }
    }

    /// <summary>
    /// Makes a filesystem-safe, ckey-like folder name out of a player name.
    /// </summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();

        // On Linux only '/' and NUL are invalid, so '.' and '..' would otherwise walk up a level.
        if (cleaned is "." or "..")
            cleaned = string.Empty;

        return string.IsNullOrEmpty(cleaned) ? "unknown" : cleaned.ToLowerInvariant();
    }
}
