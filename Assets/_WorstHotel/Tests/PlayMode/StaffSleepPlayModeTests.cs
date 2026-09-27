using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        // Actual authored bed surfaces and player input establish every sleep consent below.
        // Empty-actor approach placement is a labelled fixture, not staff-navigation evidence.
        // Production automatic sales remain enabled; quiet setups close rooms through policy.
        IEnumerator PrepareStaffSleep(bool solo = false, bool fundedMaintenance = false)
        {
            var session = GameSession.Instance;
            ManagementUI.Instance.Close();
            if (solo)
            {
                bootstrap.ConfigureSolo();
                InputSystem.RemoveDevice(padB); padB = null;
            }
            if (fundedMaintenance)
            {
                // Starting capital only. Full price, shutdown duration and heat remain production.
                waitScenarioSessionConfig = Object.Instantiate(session.config);
                maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
                maintenanceFixtureEconomy.startingCash = maintenanceFixtureEconomy.properRepairCost + 750;
                waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
                session.config = waitScenarioSessionConfig;
            }
            session.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            Assert.That(session.Simulation.ContinuousOperations && session.Simulation.AutomaticBookingsEnabled, Is.True);
            foreach (var policy in session.Simulation.RoomSalesPolicies.ToArray())
                SleepRequire(session.SetRoomSalesPolicy(0, policy.RoomId, false, policy.Price, policy.Revision));
            Assert.That(session.Simulation.Reservations, Is.Empty);
            Assert.That(Object.FindObjectsByType<StaffBedInteraction>(FindObjectsSortMode.None)
                .Select(bed => bed.bedId).OrderBy(id => id), Is.EqualTo(new[] { 0, 1 }));
            yield return null; yield return null;
        }

        static void SleepRequire(CommandResult result) => Assert.That(result.Success, Is.True, result.Message);
        static StaffBedInteraction StaffBed(int id) => Object.FindObjectsByType<StaffBedInteraction>(FindObjectsSortMode.None)
            .Single(bed => bed.bedId == id);

        IEnumerator FaceStaffBed(int actorId, int bedId)
        {
            var bed = StaffBed(bedId); var player = bootstrap.Players[actorId];
            var pad = actorId == 0 ? padA : padB;
            QueueUse(pad, false);
            Assert.That(bed.standingAnchor, Is.Not.Null, "The authored bed needs a clear standing approach.");
            var approach = new GameObject("Staff sleep labelled approach");
            approach.transform.position = bed.standingAnchor.position;
            Vector3 facing = bed.InteractionPoint - approach.transform.position; facing.y = 0;
            approach.transform.rotation = Quaternion.LookRotation(facing);
            player.ResetToSpawn(approach.transform); Object.Destroy(approach);
            yield return WaitForGroundContact(player);
            yield return AimAtKeyScenarioPoint(player, pad, () => bed.InteractionPoint);
            Assert.That(player.Interactor.HeldBody, Is.Null);
            Assert.That(player.Interactor.Focused, Is.SameAs(bed), "Consent must hit the actual first bed surface.");
        }

        IEnumerator ConsentStaffBed(int actorId, int bedId, bool keepHeld = false)
        {
            yield return FaceStaffBed(actorId, bedId);
            var pad = actorId == 0 ? padA : padB;
            QueueUse(pad, true);
            yield return WaitForCondition(() => Waiter.HasSleepConsent(actorId), 2,
                "Actual bed use must register this actor's consent: " + Waiter.Reason);
            Assert.That(Waiter.SleepBedId(actorId), Is.EqualTo(bedId));
            if (!keepHeld)
            {
                QueueUse(pad, false); yield return null; yield return null;
            }
        }

        void AdvanceStaffSleepSetupTo(float target)
        {
            var session = GameSession.Instance;
            // Labelled setup advance through ordinary model ticks; never writes clock state.
            for (int attempt = 0; attempt < 4 && session.Simulation.Elapsed < target; attempt++)
                session.AdvanceTime(Math.Min(target - session.Simulation.Elapsed, session.Simulation.Operations.SecondsPerDay));
            Assert.That(session.Simulation.Elapsed, Is.EqualTo(target).Within(.04f));
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator TwoActualBedsRequireDistinctConsentAndFreshCancelWhileOnlyHotelTimeAccelerates()
        {
            yield return PrepareStaffSleep();
            var model = GameSession.Instance.Simulation;
            yield return ConsentStaffBed(0, 0, true);
            Assert.That(Waiter.IsSleeping, Is.False);
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.HasSleepConsent(1), Is.False);
            Assert.That(Waiter.CanUseBed(1, 0).Success, Is.False, "Two employees cannot consent to the same occupied bed.");
            Assert.That(Waiter.SleepUntil, Is.GreaterThan(model.Elapsed));
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(Waiter.HasSleepConsent(0), Is.True, "The initiating held button is not a cancellation.");
            QueueUse(padA, false); yield return null; yield return null;

            yield return ConsentStaffBed(1, 1, true);
            Assert.That(Waiter.Mode, Is.EqualTo(HotelAdvanceMode.Sleep));
            Assert.That(model.Clock.Speed, Is.EqualTo(8));
            Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False, "WAIT votes do not authorize bed sleep.");
            float physicsStep = Time.fixedDeltaTime;
            var fall = new GameObject("Sleep ordinary-gravity probe");
            var body = fall.AddComponent<Rigidbody>();
            body.position = new Vector3(100, 100, 100); body.useGravity = true; body.linearDamping = 0;
            float modelBefore = model.Elapsed, realBefore = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(.3f);
            float realSeconds = Time.realtimeSinceStartup - realBefore;
            Assert.That(Waiter.IsSleeping, Is.True, "Residual input and holding the second activation must not immediately wake.");
            Assert.That(model.Elapsed - modelBefore, Is.GreaterThan(realSeconds * 5));
            Assert.That(Time.timeScale, Is.EqualTo(1));
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
            Assert.That(-body.linearVelocity.y, Is.EqualTo(-Physics.gravity.y * realSeconds).Within(1.2f),
                "A freely falling Rigidbody still accumulates ordinary real-time gravity, not eightfold physics.");
            Object.Destroy(fall);

            QueueUse(padB, false); yield return null; yield return null;
            Assert.That(Waiter.IsSleeping && Waiter.IsSleepCancellationArmed(1), Is.True);
            QueueUse(padB, true);
            yield return WaitForCondition(() => !Waiter.IsSleeping, 1, "A fresh use edge must wake both employees.");
            Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.StaffCancelled));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(Waiter.HasSleepConsent(1), Is.False, "The cancelling held press cannot immediately consent again.");
            QueueUse(padB, false); yield return null; yield return null;

            // A pending first vote has its own morning expiry; it cannot join a next-day vote.
            AdvanceStaffSleepSetupTo(model.Calendar.At(2, 6) - 4);
            yield return ConsentStaffBed(0, 0);
            float firstDeadline = Waiter.SleepUntil;
            Assert.That(firstDeadline, Is.EqualTo(model.Calendar.At(2, 6)));
            AdvanceStaffSleepSetupTo(firstDeadline + .1f);
            yield return null;
            Assert.That(Waiter.HasSleepConsent(0), Is.False);
            yield return ConsentStaffBed(1, 1);
            Assert.That(Waiter.IsSleeping, Is.False);
            Assert.That(Waiter.SleepUntil, Is.EqualTo(model.Calendar.At(3, 6)));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator SoloBedIgnoresRealFutureBookingAndReachesMorningWithoutResettingTheHotel()
        {
            yield return PrepareStaffSleep(solo: true);
            var session = GameSession.Instance; var model = session.Simulation;
            // All today's decisions were legitimately closed. A future room is now offered
            // before tomorrow's real 16:00 decision; its reservation is a minor hotel event.
            AdvanceStaffSleepSetupTo(model.Calendar.At(1, 15.8f));
            var policy = model.RoomSalesPolicies.Single(row => row.RoomId == 106);
            SleepRequire(session.SetRoomSalesPolicy(0, 106, true, policy.Price, policy.Revision));
            var rooms = session.Rooms.ToArray(); var boiler = model.Boiler;
            float condition = boiler.Condition;
            int eventBefore = model.EventRevision, cashBefore = model.Economy.Cash;
            yield return ConsentStaffBed(0, 0, true);
            Assert.That(bootstrap.Players[1], Is.Null);
            Assert.That(Waiter.HasSleepConsent(1), Is.False, "SOLO never invents the absent employee's readiness.");
            Assert.That(Waiter.IsSleeping, Is.True);
            float until = Waiter.SleepUntil;
            QueueUse(padA, false); yield return null; yield return null;
            yield return WaitForCondition(() => model.Reservations.Any(row => row.Offer.ArrivalDay == 2), 20,
                "Real scheduled sales must continue while staff sleep.");
            Assert.That(model.EventRevision, Is.GreaterThan(eventBefore));
            Assert.That(Waiter.IsSleeping, Is.True, "A genuine future booking must not be treated as an emergency.");
            var reservation = model.Reservations.Single();
            Assert.That(reservation.Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(model.Economy.Cash, Is.EqualTo(cashBefore), "A sleeping employee does not collect unearned booking revenue.");
            yield return WaitForCondition(() => !Waiter.IsSleeping, (until - model.Elapsed) / 8 + 6,
                "Ordinary accelerated ticks must reach the next 06:00 boundary.");
            Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.Morning));
            Assert.That(model.Elapsed, Is.EqualTo(until).Within(.25f));
            Assert.That(session.Simulation, Is.SameAs(model));
            Assert.That(model.Boiler, Is.SameAs(boiler));
            Assert.That(model.FindReservation(reservation.Id), Is.SameAs(reservation));
            for (int index = 0; index < rooms.Length; index++) Assert.That(session.Rooms[index], Is.SameAs(rooms[index]));
            Assert.That(boiler.Condition, Is.LessThan(condition), "Normal wear continued; morning did not rebuild equipment.");
            Assert.That(model.DayReports.Count, Is.EqualTo(1));
            Assert.That(model.DayReports[0].Receipts, Is.Empty);
            Assert.That(model.Economy.Cash, Is.EqualTo(cashBefore - session.Economy.DailyOperatingCost));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator RealLoadedBranchWarningDoesNotWakeButItsCausalTripImmediatelyEndsSleep()
        {
            yield return PrepareStaffSleep(solo: true);
            var session = GameSession.Instance; var model = session.Simulation;
            // One actual dated sale; reception/key/room callbacks below are a labelled model
            // staging adapter. This test proves bed/switch input and causal power, not navigation.
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var policy = model.RoomSalesPolicies.Single(row => row.RoomId == 104);
            SleepRequire(session.SetRoomSalesPolicy(0, 104, true, policy.Price, policy.Revision));
            AdvanceStaffSleepSetupTo(model.Calendar.At(1, 12.2f));
            var reservation = model.Reservations.Single();
            AdvanceStaffSleepSetupTo(reservation.Offer.ArrivalAt + .25f);
            var guest = model.Guests.Single();
            SleepRequire(session.ReportGuestReachedReception(guest.GuestId));
            SleepRequire(CheckInWithModelKeyFixture(session, 0, guest.GuestId));
            SleepRequire(session.ReportGuestReachedRoom(guest.GuestId));
            SleepRequire(model.ForceActivity(guest.GuestId, GuestActivity.QuietRest));
            var heaters = Object.FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).OrderBy(item => item.heaterId).ToArray();
            Assert.That(heaters.Length, Is.EqualTo(2));
            // Place the two existing bodies off in the real room; no spawned consumer or load override.
            for (int index = 0; index < heaters.Length; index++)
            {
                heaters[index].Body.position = new Vector3(3.85f, .04f, index == 0 ? 18.6f : 16);
                heaters[index].Body.rotation = Quaternion.identity;
                heaters[index].Body.linearVelocity = heaters[index].Body.angularVelocity = Vector3.zero;
            }
            yield return WaitForCondition(() => heaters.All(item => item.State?.RoomId == 104 &&
                Vector3.Distance(item.transform.TransformPoint(item.placementCollider.center), item.placementCollider.bounds.center) < .05f),
                3, "Authored heater bodies must actually resolve the selected room.");
            foreach (var heater in heaters) yield return UseElectricalServiceControl(heater, heater.placementCollider.bounds.center);
            var circuit = model.Electrical.Find("B");
            Assert.That(circuit.LoadOverride, Is.Null);
            Assert.That(circuit.ActualRequestedLoad, Is.GreaterThan(circuit.Capacity));
            Assert.That(heaters.All(item => item.State.SwitchedOn && item.State.Powered), Is.True);
            Assert.That(circuit.Tripped, Is.False);
            yield return ConsentStaffBed(0, 0);
            Assert.That(Waiter.IsSleeping, Is.True);
            yield return WaitForCondition(() => circuit.Warning, 3, "Real sustained consumer demand must first warn.");
            Assert.That(!circuit.Tripped && Waiter.IsSleeping, Is.True, "Electrical warning is actionable but not yet an outage.");
            yield return WaitForCondition(() => circuit.Tripped, 4, "The existing overload timer must trip without ForceTrip/ForceFailure.");
            Assert.That(Waiter.IsSleeping, Is.False);
            Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.CircuitTrip));
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            Assert.That(circuit.OverloadSeconds, Is.LessThanOrEqualTo(model.ElectricitySettings.TripSeconds + .4f),
                "The accelerated frame budget must stop at the fault tick.");
            Assert.That(circuit.TripCount, Is.EqualTo(1));
            Assert.That(heaters.All(item => !item.State.Powered && item.State.EffectiveHeatOutput == 0), Is.True);
            Assert.That(model.Electrical.Find("A").Tripped, Is.False);
            Assert.That(model.Boiler.Failed, Is.False, "No boiler fault was injected to establish this wake.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator PaidFullMaintenanceOfExistingFailureAllowsSleepAndCompletesOnceWithoutWaking()
        {
            yield return PrepareStaffSleep(solo: true, fundedMaintenance: true);
            var session = GameSession.Instance; var model = session.Simulation; var boiler = model.Boiler;
            // Explicit pre-existing failure isolates admission; causal failure wake is tested above.
            boiler.ForceFailure();
            yield return FaceStaffBed(0, 0);
            QueueUse(padA, true); yield return null; yield return null;
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.IsSleeping, Is.False);
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            QueueUse(padA, false); yield return null; yield return null;
            int cash = model.Economy.Cash;
            // Paid authority transaction is a labelled setup; physical preparation has its own tests.
            SleepRequire(model.BeginBoilerMaintenance(0, BoilerServiceKind.Full, boiler.MaintenanceRevision));
            int revision = boiler.MaintenanceRevision;
            float deadline = boiler.MaintenanceEndsAt;
            Assert.That(boiler.Failed && boiler.MaintenanceInProgress, Is.True);
            Assert.That(boiler.HeatingOutput, Is.Zero);
            yield return ConsentStaffBed(0, 0);
            Assert.That(Waiter.IsSleeping, Is.True, "A funded, already started Full shutdown is planned downtime.");
            yield return WaitForCondition(() => !boiler.MaintenanceInProgress, 12,
                "Sleeping staff still run the ordinary paid completion deadline.");
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(deadline));
            Assert.That(Waiter.IsSleeping, Is.True, "Planned completion is not an emergency wake.");
            Assert.That(boiler.Failed || boiler.EmergencyPatchActive, Is.False);
            Assert.That(boiler.ActiveServiceKind, Is.EqualTo(BoilerServiceKind.None));
            Assert.That(boiler.MaintenanceRevision, Is.EqualTo(revision + 1));
            Assert.That(boiler.Condition, Is.EqualTo(session.BoilerSettings.Capacity.ProperMaintenanceCondition).Within(.1f));
            Assert.That(boiler.HeatingOutput, Is.GreaterThan(0));
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - session.Economy.ProperRepairCost));
            QueueUse(padA, true);
            yield return WaitForCondition(() => !Waiter.IsSleeping, 1, "Fresh actor input ends planned rest after maintenance.");
            QueueUse(padA, false);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator PauseControllerLossAndNewGameClearBedConsentAndCannotReuseHeldInput()
        {
            yield return PrepareStaffSleep();
            yield return ConsentStaffBed(0, 0);
            yield return ConsentStaffBed(1, 1, true);
            var model = GameSession.Instance.Simulation;
            Assert.That(Waiter.IsSleeping, Is.True);
            bootstrap.SetPaused(true); yield return null; yield return null;
            float pausedAt = model.Elapsed;
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            yield return new WaitForSecondsRealtime(.15f);
            Assert.That(model.Elapsed, Is.EqualTo(pausedAt));
            bootstrap.SetPaused(false); yield return null; yield return null;
            Assert.That(Waiter.HasSleepConsent(1), Is.False, "Held initiating use cannot survive pause as renewed consent.");
            QueueUse(padB, false); yield return null; yield return null;
            yield return ConsentStaffBed(0, 0);
            yield return ConsentStaffBed(1, 1);
            Assert.That(Waiter.IsSleeping, Is.True);
            InputSystem.RemoveDevice(padB); padB = null;
            yield return WaitForCondition(() => bootstrap.WaitingForDevices && !Waiter.IsSleeping, 2,
                "Losing one real assigned device cancels both bed votes.");
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(model.Clock.Speed, Is.EqualTo(1));
            padB = InputSystem.AddDevice<Gamepad>("SleepReconnectedStaffB");
            yield return WaitForCondition(() => !bootstrap.WaitingForDevices && !bootstrap.IsPaused, 2, "Fresh controller must reconnect.");
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            yield return ConsentStaffBed(0, 0);
            yield return ConsentStaffBed(1, 1, true);
            Assert.That(Waiter.IsSleeping, Is.True);
            GameSession.Instance.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            Assert.That(GameSession.Instance.Simulation, Is.Not.SameAs(model));
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(Waiter.SleepBedId(0), Is.EqualTo(-1)); Assert.That(Waiter.SleepBedId(1), Is.EqualTo(-1));
            Assert.That(Waiter.SleepUntil, Is.Zero);
            QueueUse(padB, false);
            LogAssert.NoUnexpectedReceived();
        }

        IEnumerator HostBedUseWithRemoteLease()
        {
            // Actor0's authored standing pose/focus is prepared before either vote. Every
            // waiting frame renews actor1's authenticated lease instead of relying on device flags.
            QueueUse(padA, false); yield return RemoteNeutralFrames(2);
            QueueUse(padA, true);
            float deadline = Time.realtimeSinceStartup + 2;
            while (!Waiter.HasSleepConsent(0) && Time.realtimeSinceStartup < deadline)
            { SubmitLanFrame(); yield return null; }
            Assert.That(Waiter.HasSleepConsent(0), Is.True, Waiter.Reason);
            QueueUse(padA, false); yield return RemoteNeutralFrames(2);
        }

        IEnumerator RemoteBedUse()
        {
            var bed = StaffBed(1); var remote = bootstrap.Players[1];
            remote.ResetToSpawn(bed.standingAnchor);
            yield return RemoteNeutralFrames(3);
            yield return AimRemoteAt(() => bed.InteractionPoint);
            Assert.That(remote.Interactor.Focused, Is.SameAs(bed));
            SubmitLanFrame(frame => { frame.primaryPressed = true; frame.primaryHeld = true; });
            yield return null;
            float deadline = Time.realtimeSinceStartup + 2;
            while (!Waiter.HasSleepConsent(1) && Time.realtimeSinceStartup < deadline)
            { SubmitLanFrame(frame => frame.primaryHeld = true); yield return null; }
            Assert.That(Waiter.HasSleepConsent(1), Is.True, Waiter.Reason);
            SubmitLanFrame(frame => frame.primaryReleased = true); yield return null;
            yield return RemoteNeutralFrames(2);
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator RemoteBedConsentExpiresWithInputLeaseAndReconnectRequiresTwoFreshPhysicalVotes()
        {
            yield return PrepareStaffSleep();
            yield return ConfigureHostInputFixture();
            yield return FaceStaffBed(0, 0);
            yield return RemoteNeutralFrames(2);
            yield return HostBedUseWithRemoteLease();
            Assert.That(Waiter.IsSleeping, Is.False);
            yield return RemoteBedUse();
            Assert.That(Waiter.IsSleeping, Is.True);
            var last = SubmitLanFrame(); yield return null;
            yield return new WaitForSecondsRealtime(LocalCoopBootstrap.RemoteInputLeaseSeconds + .15f);
            Assert.That(bootstrap.RemoteInputLeaseExpired, Is.True);
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(Waiter.WakeReason, Is.EqualTo(StaffWakeReason.InputExpired));
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            Assert.That(bootstrap.SubmitRemoteInput(last), Is.False, "An old epoch/sequence cannot recover expired consent.");
            yield return RemoteNeutralFrames(3);
            Assert.That(Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False,
                "Fresh neutral lease renewal reconnects input, but does not count as a bed vote.");

            // Reverse order on the second interval: the actual remote actor votes first.
            yield return RemoteBedUse();
            Assert.That(Waiter.IsSleeping, Is.False);
            yield return HostBedUseWithRemoteLease();
            Assert.That(Waiter.IsSleeping, Is.True);
            bootstrap.SetRemoteConnected(false); yield return null; yield return null;
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            bootstrap.SetRemoteConnected(true); yield return RemoteNeutralFrames(3);
            Assert.That(Waiter.IsSleeping || Waiter.HasSleepConsent(0) || Waiter.HasSleepConsent(1), Is.False);
            Assert.That(GameSession.Instance.Simulation.Clock.Speed, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
