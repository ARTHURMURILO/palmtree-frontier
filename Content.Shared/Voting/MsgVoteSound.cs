using Lidgren.Network;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Voting
{
    /// <summary>
    /// Palmtree: which sound cues a customvote plays when it starts, is voted on and finishes.
    /// </summary>
    public enum VoteSoundMode : byte
    {
        /// <summary>No custom sounds (standard votes).</summary>
        None = 0,

        /// <summary>Start, Yes on each cast, and success on finish (customvotes with custom answer options).</summary>
        StartedOnly,

        /// <summary>Start, Yes/No on each cast, and success/failure on finish (question-only votes).</summary>
        Binary,
    }

    /// <summary>
    /// Palmtree: sound cue types broadcast to every client when a custom vote starts, is voted on or
    /// finishes.
    /// </summary>
    public enum VoteSoundType : byte
    {
        Started,
        Yes,
        No,
        Success,
        Failure,
    }

    /// <summary>
    /// Palmtree: tells every client to play a vote sound. Sent for customvotes only.
    /// </summary>
    public sealed class MsgVoteSound : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.Command;

        public int VoteId;
        public VoteSoundType Sound;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            VoteId = buffer.ReadVariableInt32();
            Sound = (VoteSoundType) buffer.ReadByte();
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.WriteVariableInt32(VoteId);
            buffer.Write((byte) Sound);
        }

        public override NetDeliveryMethod DeliveryMethod => NetDeliveryMethod.ReliableOrdered;
    }
}
