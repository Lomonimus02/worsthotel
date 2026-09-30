using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class GameSession
    {
        bool buildingReplica;
        long replicaEpoch;
        public bool IsLanReplica => Simulation != null && Simulation.IsReadOnlyMirror;
        bool ForwardLan(LanCommandKind kind, string subject = null, int room = 0, int amount = 0, int reservationRevision = -1,
            string directIntentId = null, int directIntentRevision = -1, int maintenanceRevision = -1,
            int policyRevision = -1, bool openForSale = false)
        {
            var lan = LanSession.Instance;
            if (!lan || !lan.IsClientReplica) return false;
            LastMessage = lan.SubmitCommand(kind, subject, room, amount, reservationRevision, directIntentId, directIntentRevision,
                maintenanceRevision, policyRevision, openForSale) ? "Sent to the host…" : "Waiting for the host connection.";
            return true;
        }

        public void PrepareLanReplica()
        {
            buildingReplica = true;
            try { NewGame(); }
            finally { buildingReplica = false; }
            Simulation.EnableReadOnlyMirror(); replicaEpoch = 0;
        }

        public LanHotelFrame CaptureLanFrame(long epoch, long sequence)
        {
            var repair = GetComponent<RepairSequenceController>();
            return new LanHotelFrame
            {
                epoch = epoch, sequence = sequence, day = Day, phase = Phase, planCommitted = PlanCommitted,
                lastMessage = LastMessage, planning = Plan.CaptureSnapshot(), model = Simulation.CaptureSnapshot(epoch, sequence),
                waitReason = Wait ? Wait.Reason : "", waitVotes = new[] { Wait && Wait.HasVoted(0), Wait && Wait.HasVoted(1) },
                waitProgress = new[] { Wait ? Wait.VoteProgress(0) : 0, Wait ? Wait.VoteProgress(1) : 0 },
                sleep = Wait ? LanStaffSleepFrame.FromView(Wait.CaptureSleepView()) : new LanStaffSleepFrame(),
                repairStatus = repair ? repair.Status : ""
            };
        }

        public CommandResult ApplyLanFrame(LanHotelFrame frame)
        {
            if (frame == null || frame.version != LanProtocol.Version || frame.epoch <= 0 || frame.sequence <= 0 ||
                frame.day < 1 || (frame.model == null || !frame.model.HasOperations) && frame.day > config.totalDays || !Enum.IsDefined(typeof(DayPhase), frame.phase) ||
                frame.planning == null || frame.model == null || frame.model.Epoch != frame.epoch || frame.model.Sequence != frame.sequence ||
                frame.epoch < replicaEpoch || frame.planCommitted != frame.planning.IsCommitted ||
                frame.waitVotes == null || frame.waitVotes.Length != 2 || frame.waitProgress == null || frame.waitProgress.Length != 2 ||
                frame.waitProgress.Any(value => !Number.IsFinite(value) || value < 0 || value > 1) ||
                frame.lastMessage != null && frame.lastMessage.Length > 1024 || frame.waitReason != null && frame.waitReason.Length > 1024 ||
                frame.repairStatus != null && frame.repairStatus.Length > 1024 || frame.openGuestRevision < 0 || frame.openServiceRevision < 0 || frame.openBoilerRevision < 0 ||
                frame.conversationGuestId != null && frame.conversationGuestId.Length > 160 ||
                frame.openGuestRevision > 0 && string.IsNullOrWhiteSpace(frame.conversationGuestId))
                return CommandResult.Fail("Invalid host frame.");
            if (!IsLanReplica) return CommandResult.Fail("Only a read-only replica accepts host state.");
            if (frame.model.HasOperations && (frame.model.Operations == null || frame.day != frame.model.Day ||
                frame.phase != (frame.model.Operations.OwnershipLost ? DayPhase.Results : DayPhase.Service) || frame.planCommitted))
                return CommandResult.Fail("Invalid continuous host frame.");
            bool fresh = replicaEpoch != frame.epoch;
            var previousPhase = Phase;
            var targetRooms = fresh ? config.rooms.Select(item => new RoomState(item.ToData())).ToArray() : Rooms;
            HotelSimulation targetSimulation;
            PlanningSystem targetPlan;
            try
            {
                // The host's explicit null selects a legacy fixture even if this build's asset is continuous.
                targetSimulation = fresh ? CreateSimulationForRooms(targetRooms, frame.model.HasOperations ? frame.model.Operations.ToSettings() : null) : Simulation;
                if (fresh) targetSimulation.EnableReadOnlyMirror();
                targetPlan = PlanningSystem.FromSnapshot(targetRooms, Economy, BoilerSettings, frame.planning);
            }
            catch (ArgumentException error) { return CommandResult.Fail("Invalid host planning: " + error.Message); }
            if (!ValidLanSleepFrame(frame, targetSimulation, fresh)) return CommandResult.Fail("Invalid host staff sleep state.");
            var result = targetSimulation.ApplySnapshot(frame.model);
            if (!result.Success) return result;
            Simulation = targetSimulation; Rooms = targetRooms; Plan = targetPlan;
            Day = frame.day; Phase = frame.phase; PlanCommitted = frame.planCommitted;
            CommittedBookings = PlanCommitted ? Plan.Assignments.ToArray() : Array.Empty<BookingAssignment>();
            Cash = Simulation.Economy.Cash; LastMessage = frame.lastMessage ?? "";
            reports.Clear(); reports.AddRange(Simulation.DayReports);
            Report = Simulation.ContinuousOperations ? Simulation.LastReport :
                Phase == DayPhase.Planning || Phase == DayPhase.Service ? null : Simulation.LastReport;
            replicaEpoch = frame.epoch; accumulator = 0;
            if (Wait) Wait.ApplyLanView(frame.waitReason, frame.waitVotes, frame.waitProgress);
            if (Wait) Wait.ApplyLanSleepView(frame.sleep.ToView());
            RaiseChanged();
            if (ManagementUI.Instance && (fresh || previousPhase != Phase))
            {
                ManagementUI.Instance.Close();
                if (Phase != DayPhase.Service) ManagementUI.Instance.Open(LocalCoopBootstrap.Instance.LocalActorId);
            }
            return CommandResult.Ok("Host state applied.");
        }

        public void ExecuteLanCommand(int playerId, LanCommand command)
        {
            // A lost hotel's restart belongs to the host, including calls that bypass the transport envelope.
            if (IsLanReplica || playerId != 1 || command == null || Simulation.OwnershipLost) return;
            switch (command.kind)
            {
                case LanCommandKind.Assign: Assign(playerId, command.subject, command.roomId, command.amount); break;
                case LanCommandKind.Remove: Remove(playerId, command.roomId); break;
                case LanCommandKind.SetPrice: SetPrice(playerId, command.roomId, command.amount); break;
                case LanCommandKind.CommitPlan: CommitPlan(playerId); break;
                case LanCommandKind.OfferCredit: OfferCompensation(playerId, command.subject, command.expectedDirectIntentId, command.expectedDirectIntentRevision); break;
                case LanCommandKind.ContinueSettlement: ContinueAfterSettlement(playerId); break;
                case LanCommandKind.Maintenance:
                    if (Enum.IsDefined(typeof(MaintenanceChoice), command.amount)) ChooseMaintenance(playerId, (MaintenanceChoice)command.amount);
                    break;
                case LanCommandKind.PurchaseBoilerUpgrade: PurchaseBoilerUpgrade(playerId); break;
                case LanCommandKind.PurchaseInsulation: PurchaseInsulation(playerId); break;
                case LanCommandKind.RestoreNorthWing: RestoreNorthWing(playerId); break;
                case LanCommandKind.PurchaseElectricalUpgrade: PurchaseElectricalUpgrade(playerId, command.subject); break;
                case LanCommandKind.SelectBoilerService:
                    SelectBoilerService(playerId, (BoilerServiceKind)command.amount, command.expectedMaintenanceRevision); break;
                case LanCommandKind.RestartSession: RestartSession(playerId); break;
                case LanCommandKind.MoveGuest: MoveGuest(playerId, command.subject, command.roomId); break;
                case LanCommandKind.CancelMove: CancelGuestMove(playerId, command.subject, command.expectedDirectIntentId, command.expectedDirectIntentRevision); break;
                case LanCommandKind.AcceptConsequences: AcceptConsequences(playerId, command.subject, command.expectedDirectIntentId, command.expectedDirectIntentRevision); break;
                case LanCommandKind.AcceptBoilerConsequences: AcceptBoilerConsequences(playerId); break;
                case LanCommandKind.RequestQuiet: RequestQuiet(playerId, command.subject); break;
                case LanCommandKind.RequestGuestRoomEntry: RequestGuestRoomEntry(playerId, command.subject); break;
                case LanCommandKind.CloseGuestConversation: CloseGuestConversation(playerId, command.subject); break;
                case LanCommandKind.RespondService: if (command.amount <= 1) RespondToService(playerId, command.subject, command.amount == 1); break;
                case LanCommandKind.AcknowledgeService: AcknowledgeService(playerId, command.subject); break;
                case LanCommandKind.OfferLuggage: if (command.amount <= 1) OfferLuggage(playerId, command.subject, command.amount == 1); break;
                case LanCommandKind.CompleteWakeUp: CompleteWakeUpCall(playerId, command.subject); break;
                case LanCommandKind.CloseWakePhone: CloseWakePhone(playerId); break;
                case LanCommandKind.EndServicePhoneConversation: EndServicePhoneConversation(playerId, command.subject); break;
                case LanCommandKind.AnswerServiceCall: AnswerIncomingServiceCall(playerId, command.subject); break;
                case LanCommandKind.TalkServiceGuest:
                    TalkToServiceGuest(playerId, Simulation.Services?.FindResponse(command.subject)?.GuestId, command.subject); break;
                case LanCommandKind.DiscussRoomConcern:
                    DiscussRoomConcern(playerId, Simulation.Services?.FindResponse(command.subject)?.GuestId, command.subject); break;
                case LanCommandKind.AcceptBooking: AcceptBooking(playerId, command.subject, command.roomId, command.amount); break;
                case LanCommandKind.CancelBooking: CancelBooking(playerId, command.subject, command.expectedReservationRevision); break;
                case LanCommandKind.SetBookingPrice: SetBookingPrice(playerId, command.subject, command.amount, command.expectedReservationRevision); break;
                case LanCommandKind.SetRoomSalesPolicy:
                    SetRoomSalesPolicy(playerId, command.roomId, command.openForSale, command.amount, command.expectedPolicyRevision); break;
                case LanCommandKind.AcceptSpecialBooking:
                case LanCommandKind.DeclineSpecialBooking:
                    DecideSpecialBooking(playerId, command.subject, command.roomId, command.kind == LanCommandKind.AcceptSpecialBooking,
                        command.expectedReservationRevision); break;
                case LanCommandKind.UnplugGuestAmplifier:
                    AskToUnplugAmplifier(playerId, command.subject); break;
                case LanCommandKind.ReassignBooking:
                    ReassignBooking(playerId, command.subject, command.roomId, command.expectedReservationRevision); break;
            }
        }
    }
}
