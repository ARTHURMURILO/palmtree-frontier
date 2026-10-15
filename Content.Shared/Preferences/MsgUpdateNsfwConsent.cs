using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Preferences;

/// <summary>
/// Palmtree: the client sends this once to answer the NSFW consent prompt. The server only
/// accepts it while the account has not answered yet; afterwards the flag can only be changed
/// with the <c>nsfwtoggle</c> console command.
/// </summary>
public sealed class MsgUpdateNsfwConsent : NetMessage
{
    public override MsgGroups MsgGroup => MsgGroups.Command;

    /// <summary>True if the player is 18+ and wishes to see NSFW features.</summary>
    public bool Allowed;

    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        Allowed = buffer.ReadBoolean();
    }

    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write(Allowed);
    }
}
