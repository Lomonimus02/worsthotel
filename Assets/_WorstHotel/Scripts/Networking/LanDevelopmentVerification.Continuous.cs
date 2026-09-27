#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class LanDevelopmentVerification
    {
        ScheduledBookingOffer[] ContinuousTomorrowOffers() => session.Simulation.BookingOffers
            .Where(item => item.ArrivalDay == 2).OrderBy(item => item.ArrivalAt).ToArray();

        int ContinuousReferenceRate(ScheduledBookingOffer offer)
        {
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int max = min + (session.Economy.MaxPrice - min) / step * step;
            return Mathf.Clamp(min + Mathf.RoundToInt((offer.Application.ReferencePrice - min) / (float)step) * step, min, max);
        }

        void RequireProductionContinuous()
        {
            Require(session.config.continuousOperations && session.Simulation.ContinuousOperations,
                "continuous LAN fixture uses production continuous mode, not a legacy clone");
            Require(session.config.hotelDaySeconds == 720 && session.config.openingHour == 8 && session.config.reportHour == 6,
                "continuous LAN fixture retains production calendar timing");
            Require(session.Phase == DayPhase.Service && !session.PlanCommitted && session.Simulation.Running,
                "hotel operates without committing a shift plan");
            facts.Add("LANVersion=" + LanProtocol.Version + " ModelSchema=" + HotelModelSnapshot.ProtocolVersion +
                " Compatibility=" + LanProtocol.BuildCompatibility);
        }

        IEnumerator RunContinuousHost()
        {
            Require(NetworkManager.Singleton && NetworkManager.Singleton.IsListening, "actual NGO host listens");
            RequireProductionContinuous();
            WriteStage("host-listening");
            yield return Until(() => lan.PeerConnected, 35, "continuous remote client connected normally");
            CheckCameraAndAuthority();
            var model = session.Simulation;
            var rooms = session.Rooms;
            long epoch = lan.Epoch;
            var offers = ContinuousTomorrowOffers();
            Require(offers.Length >= 3 && model.Guests.Count == 0, "tomorrow enquiries have no materialized guests");
            string primaryId = offers[0].Id, cancelledId = offers[1].Id, laterId = offers[2].Id;
            int updatedPrice = ContinuousReferenceRate(offers[0]) + session.Economy.PriceStep;
            var terminal = FindAnyObjectByType<ReceptionTerminal>();
            Require(terminal, "authored reception terminal exists");
            Vector3 aim = terminal.transform.position + Vector3.up * .35f;
            PositionEmptyServiceActor(new Vector3(aim.x, .08f, aim.z - 1.45f), aim);
            facts.Add("DIAGNOSTIC: empty remote employee placed before the authored reception terminal; actual client look/use and the normal physical ledger grant perform all access.");
            facts.Add("PRODUCTION CONTINUOUS: 720-second day, opens08:00, report06:00; no legacy clone, synthetic guest arrival or fabricated transport state.");
            WriteStage("continuous-reception-ready");
            yield return Stage("continuous-ledger-open", 30);
            yield return Until(() => coop.Players[1].IsUIBlocked, 5, "host received actual client ledger UI state");
            yield return Until(() => model.FindReservation(primaryId)?.Revision == 2, 45, "remote controller accepts and edits tomorrow reservation");
            var reservation = model.FindReservation(primaryId);
            Require(reservation.Status == ReservationStatus.Reserved && reservation.RoomId == 101 &&
                reservation.Price == updatedPrice && reservation.ActorId == 1, "host owns exact edited reservation from authenticated actor1");
            Require(model.Guests.Count == 0 && rooms.All(room => !room.Occupied && room.ReservedGuestId == null),
                "future acceptance does not occupy today's rooms or spawn guests");
            WriteStage("continuous-edit-observed");
            yield return Stage("continuous-stale-sent", 12);
            yield return Until(() => session.LastMessage.Contains("booking changed"), 8, "normal command route rejects stale reservation revision");
            Require(reservation.Revision == 2 && reservation.Status == ReservationStatus.Reserved && reservation.Price == updatedPrice,
                "stale cancel does not mutate the newer price agreement");
            WriteStage("continuous-stale-rejected");
            yield return Until(() => model.FindReservation(cancelledId)?.Status == ReservationStatus.Cancelled, 40,
                "second controller-created reservation is cancelled through host command");
            yield return Stage("continuous-ready-boundary", 12);

            int cashBefore = model.Economy.Cash;
            Require(model.DayReports.Count == 0, "prepared boundary starts before the first daily report");
            model.Boiler.ForceFailure();
            string circuit = rooms.Single(room => room.Profile.Id == 106).CircuitId;
            Require(model.DebugTripCircuit(circuit).Success, "labelled circuit fault setup");
            Require(model.DebugMarkRoomDirty(106).Success, "labelled dirty vacant room setup creates real turnover");
            var turnover = model.Housekeeping.Find(106);
            string dirtyLinen = turnover.DirtyLinenId;
            int dirtyGeneration = turnover.Generation;
            var rackKey = model.Keys.Find(101);
            session.RaiseChanged();
            facts.Add("DIAGNOSTIC: ForceFailure + DebugTripCircuit(" + circuit + ") + DebugMarkRoomDirty(106) create persistence conditions. Rack key stays on rack; no physical carry is claimed.");
            WriteStage("continuous-faults-ready");
            yield return Stage("continuous-faults-observed", 12);
            float target = model.NextReportAt + .4f;
            Require(target > model.Elapsed && target - model.Elapsed < session.config.hotelDaySeconds,
                "first report fits one bounded diagnostic calendar advance");
            float advancedFrom = model.Elapsed;
            AdvanceContinuousDiagnosticTo(target, "first accounting boundary");
            Require(session.Simulation == model && session.Rooms == rooms && lan.Epoch == epoch,
                "date/report boundary preserves host model, rooms and network epoch");
            Require(session.Day == 2 && session.Phase == DayPhase.Service && model.DayReports.Count == 1,
                "midnight plus06:00 report leaves the continuous hotel in service");
            Require(model.Boiler.Failed && model.Electrical.Find(circuit).Tripped && rooms.Single(room => room.Profile.Id == 106).Cleanliness == Cleanliness.Dirty,
                "report does not reset failed boiler, tripped circuit or dirt");
            Require(model.Housekeeping.Find(106) == turnover && turnover.DirtyLinenId == dirtyLinen && turnover.Generation == dirtyGeneration &&
                model.Housekeeping.FindLinen(dirtyLinen).Location == LinenLocation.OnBed,
                "turnover identity and dirty linen persist across the calendar boundary");
            Require(model.Keys.Find(101) == rackKey && rackKey.Location == RoomKeyLocation.OnRack,
                "the same physical room key remains on its rack");
            Require(model.FindReservation(primaryId).Status == ReservationStatus.Reserved && model.FindReservation(primaryId).Price == updatedPrice,
                "tomorrow reservation survives midnight with its agreed price");
            Require(model.Economy.Cash == cashBefore - session.Economy.DailyOperatingCost && model.LastReport.Receipts.Count == 0,
                "exactly one operating charge and no premature guest receipts");
            var report = model.LastReport;
            facts.Add("DIAGNOSTIC CLOCK: " + advancedFrom.ToString("F2") + " -> " + model.Elapsed.ToString("F2") +
                "; normal fixed-tick AdvanceTime crosses midnight/report; not a human-duration or arrival-route test.");
            WriteStage("continuous-boundary-ready");
            yield return Stage("continuous-boundary-observed", 18);
            yield return Until(() => model.FindReservation(laterId)?.Status == ReservationStatus.Reserved, 35,
                "client can accept another current-day booking after midnight through the same connection");
            Require(model.FindReservation(laterId).ActorId == 1 && model.FindReservation(laterId).RoomId == 102,
                "cancelled room capacity is reused by the new authenticated reservation");
            Require(model.LastReport == report && model.DayReports.Count == 1 && model.Economy.Cash == cashBefore - session.Economy.DailyOperatingCost,
                "subsequent booking and report reading cannot charge the accounting period twice");
            Require(lan.AcceptedRemoteCommands >= 6, "normal NGO accepted booking, edit, stale attempt, second booking, cancel and post-boundary booking envelopes");
            facts.Add("PhysicalLedgerAccess=True BookingRoundtrip=True PriceEdit=True StaleRevisionRejected=True Cancellation=True MidnightPersistence=True ReportOnce=True PostBoundaryBooking=True");
            facts.Add("HostEpoch=" + epoch + " Day=" + session.Day + " Cash=" + model.Economy.Cash + " Report=" + report.DayNumber +
                " RemoteCommands=" + lan.AcceptedRemoteCommands + " ModelBytes=" + lan.LastModelBytes + " WorldBytes=" + lan.LastWorldBytes);
            WriteStage("continuous-booking-roundtrip-complete");
            yield return RunContinuousCapitalHost();
            WriteStage("continuous-host-complete");
            yield return Stage("client-complete", 20);
        }

        IEnumerator RunContinuousClient()
        {
            yield return Until(() => lan.PeerConnected && lan.HasSnapshot, 35, "continuous client receives authoritative model");
            RequireProductionContinuous(); CheckCameraAndAuthority();
            Require(session.IsLanReplica && session.Simulation.IsReadOnlyMirror, "continuous client has no simulation authority");
            Require(pad != null && pad.added && pad.enabled && pad.canRunInBackground, "continuous fixture owns its background-capable virtual pad");
            yield return Until(() => ReferenceEquals(coop.Players[1].Input.Gamepad, pad), 3, "private client pad bound");
            lastClientSequence = lan.AppliedModelSequence; lastClientClock = session.Simulation.Elapsed; clockTracking = true;
            ManagementUI.Instance.Close();
            var mirror = session.Simulation; var rooms = session.Rooms; long epoch = lan.Epoch;
            var offers = ContinuousTomorrowOffers();
            Require(offers.Length >= 3, "client received dated enquiries");
            string primaryId = offers[0].Id, cancelledId = offers[1].Id, laterId = offers[2].Id;
            yield return Stage("continuous-reception-ready", 12);
            var terminal = FindAnyObjectByType<ReceptionTerminal>();
            Vector3 aim = terminal.transform.position + Vector3.up * .35f;
            yield return Until(() => Horizontal(coop.Players[1].transform.position, new Vector3(aim.x, .08f, aim.z - 1.45f)) < .2f,
                6, "empty reception approach arrives via world snapshot");
            yield return ServicesAim(() => aim, "Open the reception ledger");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => ManagementUI.Instance.IsOperationsOpen && ManagementUI.Instance.Owner == 1, 6,
                "real client use opens the physically authorized operations journal");
            WriteStage("continuous-ledger-open");
            yield return ContinuousChoose("Tomorrow's bookings");
            yield return ContinuousChoose(offers[0].Application.GuestName + " · ");
            yield return ContinuousChoose("Accept booking");
            yield return Until(() => mirror.FindReservation(primaryId)?.Status == ReservationStatus.Reserved, 8, "accepted booking returns through normal snapshots");
            int originalRevision = mirror.FindReservation(primaryId).Revision;
            int originalPrice = mirror.FindReservation(primaryId).Price;
            yield return ContinuousChoose("+ $");
            yield return ContinuousChoose("Update agreed price");
            yield return Until(() => mirror.FindReservation(primaryId)?.Revision == originalRevision + 1 &&
                mirror.FindReservation(primaryId).Price == originalPrice + session.Economy.PriceStep, 8, "edited price returns through host snapshot");
            yield return Stage("continuous-edit-observed", 8);
            // Explicit negative command fixture, not an injected server mutation or custom diagnostic RPC.
            session.CancelBooking(1, primaryId, originalRevision);
            WriteStage("continuous-stale-sent");
            yield return Stage("continuous-stale-rejected", 10);
            Require(mirror.FindReservation(primaryId).Status == ReservationStatus.Reserved &&
                mirror.FindReservation(primaryId).Revision == originalRevision + 1, "stale partner intent leaves the newer reservation intact");
            yield return ContinuousChoose("Back to bookings");
            yield return ContinuousChoose(offers[1].Application.GuestName + " · ");
            yield return ContinuousChoose("Accept booking");
            yield return Until(() => mirror.FindReservation(cancelledId)?.Status == ReservationStatus.Reserved, 8, "second future reservation is authoritative");
            yield return ContinuousChoose("Cancel reservation");
            yield return Until(() => mirror.FindReservation(cancelledId)?.Status == ReservationStatus.Cancelled, 8, "cancellation returns in normal snapshot");
            yield return ContinuousChoose("Back to bookings");
            if (capture) yield return Capture("client-continuous-bookings");
            yield return ContinuousChoose("Back to operations");
            WriteStage("continuous-ready-boundary");
            yield return Stage("continuous-faults-ready", 12);
            string circuit = rooms.Single(room => room.Profile.Id == 106).CircuitId;
            yield return Until(() => mirror.Boiler.Failed && mirror.Electrical.Find(circuit).Tripped &&
                rooms.Single(room => room.Profile.Id == 106).Cleanliness == Cleanliness.Dirty && mirror.Housekeeping.Find(106) != null,
                8, "prepared physical-system conditions arrive before the boundary");
            string dirtyLinen = mirror.Housekeeping.Find(106).DirtyLinenId;
            int dirtyGeneration = mirror.Housekeeping.Find(106).Generation;
            int cashBefore = mirror.Economy.Cash;
            var rackKey = mirror.Keys.Find(101);
            WriteStage("continuous-faults-observed");
            yield return Stage("continuous-boundary-ready", 15);
            yield return Until(() => session.Day == 2 && session.Reports.Count == 1, 12, "same replica receives midnight and report snapshots");
            Require(session.Simulation == mirror && session.Rooms == rooms && lan.Epoch == epoch && session.Phase == DayPhase.Service,
                "boundary preserves replica model, room objects, connection and service phase");
            Require(ManagementUI.Instance.IsOperationsOpen && ManagementUI.Instance.Owner == 1 && coop.Players[1].IsUIBlocked,
                "calendar boundary preserves the already-open local journal and owner");
            Require(mirror.Boiler.Failed && mirror.Electrical.Find(circuit).Tripped && rooms.Single(room => room.Profile.Id == 106).Cleanliness == Cleanliness.Dirty,
                "failed systems and dirty room survive on client");
            Require(mirror.Housekeeping.Find(106).DirtyLinenId == dirtyLinen && mirror.Housekeeping.Find(106).Generation == dirtyGeneration &&
                mirror.Housekeeping.FindLinen(dirtyLinen).Location == LinenLocation.OnBed && mirror.Keys.Find(101) == rackKey && rackKey.Location == RoomKeyLocation.OnRack,
                "same dirty linen generation and room key survive on client");
            Require(mirror.Economy.Cash == cashBefore - session.Economy.DailyOperatingCost && session.Report.Receipts.Count == 0,
                "client receives exactly one operating cost without premature room payments");
            Require(mirror.FindReservation(primaryId).Status == ReservationStatus.Reserved &&
                mirror.FindReservation(primaryId).Price == originalPrice + session.Economy.PriceStep &&
                mirror.FindReservation(cancelledId).Status == ReservationStatus.Cancelled, "both edited and cancelled booking decisions survive");
            if (capture) yield return Capture("client-continuous-boundary");
            yield return ContinuousChoose("Daily reports");
            yield return ContinuousChoose("Operating report 1");
            if (capture) yield return Capture("client-continuous-report");
            int reportedCash = mirror.Economy.Cash;
            yield return new WaitForSecondsRealtime(.6f);
            Require(session.Reports.Count == 1 && mirror.Economy.Cash == reportedCash, "reading and repeated snapshots never post costs again");
            WriteStage("continuous-boundary-observed");
            yield return ContinuousChoose("Back to reports");
            yield return ContinuousChoose("Back to operations");
            yield return ContinuousChoose("Today's bookings");
            yield return ContinuousChoose(offers[2].Application.GuestName + " · ");
            yield return ContinuousChoose("Accept booking");
            yield return Until(() => mirror.FindReservation(laterId)?.Status == ReservationStatus.Reserved, 8,
                "post-midnight enquiry still uses live authenticated network commands");
            yield return Stage("continuous-booking-roundtrip-complete", 10);
            yield return RunContinuousCapitalClient();
            yield return Stage("continuous-host-complete", 10);
            float before = mirror.Elapsed; mirror.Tick(10);
            Require(mirror.Elapsed == before && !mirror.CancelBooking(1, primaryId).Success && stableClockChecks >= 10,
                "mirror clock and reservations only change through host snapshots");
            Require(ManagementUI.Instance.IsOperationsOpen && lan.Epoch == epoch && session.Simulation == mirror,
                "same connection and local menu remain usable throughout");
            facts.Add("PhysicalLedgerAccess=True BookingRoundtrip=True PriceEdit=True StaleRevisionRejected=True Cancellation=True MidnightPersistence=True ReportOnce=True PostBoundaryBooking=True");
            facts.Add("ReadOnlyMirror=True SnapshotOnlyClockChecks=" + stableClockChecks + " HostEpoch=" + epoch +
                " Day=" + session.Day + " Cash=" + mirror.Economy.Cash + " Report=" + session.Report.DayNumber +
                "; forced system faults and bounded diagnostic advance are setup, not human play or natural overload evidence.");
            Queue(default); WriteStage("client-complete");
        }

        IEnumerator ContinuousChoose(string prefix)
        {
            var ui = ManagementUI.Instance;
            Queue(default); yield return null; yield return null;
            Require(ui.IsOperationsOpen && ui.OperationsOptionTitles.Any(title => title.StartsWith(prefix, StringComparison.Ordinal)),
                "operations contains " + prefix);
            int attempts = 0;
            while (ui.FocusedOperationsOption == null || !ui.FocusedOperationsOption.StartsWith(prefix, StringComparison.Ordinal))
            {
                Require(attempts++ < 24, "controller can reach enabled operations choice " + prefix);
                yield return TapButton(GamepadButton.DpadDown);
            }
            yield return TapButton(GamepadButton.South);
        }
    }
}
#endif
