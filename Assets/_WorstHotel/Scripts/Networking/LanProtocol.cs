using System;
using System.Net;
using System.Net.Sockets;

namespace WorstHotel
{
    public enum LanCommandKind
    {
        Assign, Remove, SetPrice, CommitPlan, OfferCredit, ContinueSettlement,
        Maintenance, RestartSession, MoveGuest, CancelMove, AcceptConsequences, AcceptBoilerConsequences,
        RequestQuiet, RequestGuestRoomEntry, CloseGuestConversation,
        RespondService, AcknowledgeService, CompleteWakeUp, CloseWakePhone
    }

    [Serializable] public sealed class LanCommand
    {
        public int version = LanProtocol.Version;
        public long epoch, sequence;
        public int day, roomId, amount;
        public DayPhase phase;
        public LanCommandKind kind;
        public string subject;
    }

    [Serializable] public sealed class LanHotelFrame
    {
        public int version = LanProtocol.Version, day;
        public long epoch, sequence, openLedgerRevision, openGuestRevision;
        public string conversationGuestId;
        public bool conversationThroughDoor;
        public long openServiceRevision;
        public bool servicePhone;
        public DayPhase phase;
        public bool planCommitted, hostPaused;
        public string lastMessage, waitReason, repairStatus;
        public bool[] waitVotes;
        public float[] waitProgress;
        public PlanningSnapshot planning;
        public HotelModelSnapshot model;
    }

    /// <summary>Small, versioned LAN boundary. A network connection, never a payload, selects its staff identity.</summary>
    public static class LanProtocol
    {
        public const int Version = 5, MaxInputBytes = 4096, MaxCommandBytes = 2048, MaxSnapshotBytes = 524288;
        public const ushort DefaultPort = 7777;
        public const string BuildCompatibility = "worst-hotel-0.3.1-services5-gzip";

        public static bool ValidAddress(string value) => IPAddress.TryParse(value, out var address) &&
            address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) &&
            !address.Equals(IPAddress.Broadcast) && address.GetAddressBytes()[0] < 224;

        public static bool ValidCommand(LanCommand command, long epoch, long lastSequence, int day, DayPhase phase) =>
            command != null && command.version == Version && command.epoch == epoch && epoch > 0 &&
            command.sequence > lastSequence && command.sequence > 0 && command.day == day && command.phase == phase &&
            Enum.IsDefined(typeof(LanCommandKind), command.kind) && Enum.IsDefined(typeof(DayPhase), command.phase) &&
            (command.subject == null || command.subject.Length <= 128) && command.amount >= 0 && command.amount <= 100000 &&
            (command.roomId == 0 || command.roomId >= 101 && command.roomId <= 106);
    }
}
