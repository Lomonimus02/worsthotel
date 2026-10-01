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
        RespondService, AcknowledgeService, CompleteWakeUp, CloseWakePhone,
        AnswerServiceCall, TalkServiceGuest, DiscussRoomConcern,
        AcceptBooking, CancelBooking, SetBookingPrice, BeginBoilerMaintenance,
        PurchaseBoilerUpgrade, PurchaseElectricalUpgrade, EndServicePhoneConversation, SelectBoilerService,
        SetRoomSalesPolicy, ReassignBooking, OfferLuggage, PurchaseInsulation, RestoreNorthWing, AcceptSpecialBooking, DeclineSpecialBooking, UnplugGuestAmplifier,
        OrderLaundry, OrderBulbs
    }

    [Serializable] public sealed class LanCommand
    {
        public int version = LanProtocol.Version;
        public long epoch, sequence;
        public int day, roomId, amount;
        public int expectedReservationRevision = -1;
        public int expectedMaintenanceRevision = -1;
        public int expectedPolicyRevision = -1;
        public int expectedSupplyRevision = -1;
        public bool openForSale;
        public string expectedDirectIntentId;
        public int expectedDirectIntentRevision = -1;
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
        public long openServiceRevision, openBoilerRevision;
        public bool servicePhone;
        public HotelBook ledgerBook;
        public DayPhase phase;
        public bool planCommitted, hostPaused;
        public string lastMessage, waitReason, repairStatus;
        public bool[] waitVotes;
        public float[] waitProgress;
        public LanStaffSleepFrame sleep;
        public PlanningSnapshot planning;
        public HotelModelSnapshot model;
    }

    /// <summary>Small, versioned LAN boundary. A network connection, never a payload, selects its staff identity.</summary>
    public static class LanProtocol
    {
        public const int Version = 28, MaxInputBytes = 4096, MaxCommandBytes = 2048, MaxSnapshotBytes = 524288;
        public const ushort DefaultPort = 7777;
        public const string BuildCompatibility = "worst-hotel-0.6.7-cadence28-gzip";

        public static bool ValidAddress(string value) => IPAddress.TryParse(value, out var address) &&
            address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any) &&
            !address.Equals(IPAddress.Broadcast) && address.GetAddressBytes()[0] < 224;

        public static bool ValidCommand(LanCommand command, long epoch, long lastSequence, int day, DayPhase phase,
            bool continuousOperations = false, bool automaticBookingsEnabled = false) =>
            command != null && command.version == Version && command.epoch == epoch && epoch > 0 &&
            command.sequence > lastSequence && command.sequence > 0 && command.day > 0 &&
            (continuousOperations ? command.day <= day && phase == DayPhase.Service : command.day == day) && command.phase == phase &&
            Enum.IsDefined(typeof(LanCommandKind), command.kind) && Enum.IsDefined(typeof(DayPhase), command.phase) &&
            (command.subject == null || command.subject.Length <= (UsesResponseIdentity(command.kind) ? 512 : 128)) &&
            command.amount >= 0 && command.amount <= 100000 &&
            command.kind != LanCommandKind.BeginBoilerMaintenance &&
            ((command.kind == LanCommandKind.OrderLaundry || command.kind == LanCommandKind.OrderBulbs) ?
                continuousOperations && command.expectedSupplyRevision >= 0 && command.amount == 0 && command.roomId == 0 &&
                string.IsNullOrEmpty(command.subject) : command.expectedSupplyRevision == -1) &&
            (!automaticBookingsEnabled || command.kind != LanCommandKind.AcceptBooking && command.kind != LanCommandKind.SetBookingPrice) &&
            (command.kind == LanCommandKind.SetRoomSalesPolicy ? continuousOperations && automaticBookingsEnabled &&
                command.expectedPolicyRevision >= 1 && string.IsNullOrEmpty(command.subject) && command.roomId >= 101 && command.roomId <= 110 :
                command.expectedPolicyRevision == -1 && !command.openForSale) &&
            (command.kind != LanCommandKind.ReassignBooking || continuousOperations &&
                !string.IsNullOrWhiteSpace(command.subject) && command.roomId >= 101 && command.roomId <= 110 && command.amount == 0) &&
            (command.kind == LanCommandKind.SelectBoilerService ? continuousOperations &&
                command.expectedMaintenanceRevision >= 0 &&
                (command.amount == (int)BoilerServiceKind.Basic || command.amount == (int)BoilerServiceKind.Full) :
                command.expectedMaintenanceRevision == -1) &&
            ((command.kind == LanCommandKind.AcceptSpecialBooking || command.kind == LanCommandKind.DeclineSpecialBooking) ?
                continuousOperations && !string.IsNullOrWhiteSpace(command.subject) && command.amount == 0 && command.expectedReservationRevision >= 1 &&
                (command.kind == LanCommandKind.AcceptSpecialBooking ? command.roomId >= 101 && command.roomId <= 110 : command.roomId == 0) :
                command.kind == LanCommandKind.ReassignBooking ? command.expectedReservationRevision >= 1 :
                command.kind == LanCommandKind.CancelBooking || command.kind == LanCommandKind.SetBookingPrice ?
                command.expectedReservationRevision >= 0 : command.expectedReservationRevision == -1) &&
            (UsesDirectDecision(command.kind) && continuousOperations ?
                !string.IsNullOrWhiteSpace(command.expectedDirectIntentId) && command.expectedDirectIntentId.Length <= 512 &&
                command.expectedDirectIntentRevision > 0 : string.IsNullOrEmpty(command.expectedDirectIntentId) && command.expectedDirectIntentRevision == -1) &&
            (command.roomId == 0 || command.roomId >= 101 && command.roomId <= 110);

        static bool UsesDirectDecision(LanCommandKind kind) => kind == LanCommandKind.CancelMove ||
            kind == LanCommandKind.OfferCredit || kind == LanCommandKind.AcceptConsequences;

        static bool UsesResponseIdentity(LanCommandKind kind) => kind == LanCommandKind.AnswerServiceCall ||
            kind == LanCommandKind.TalkServiceGuest || kind == LanCommandKind.DiscussRoomConcern;
    }
}
