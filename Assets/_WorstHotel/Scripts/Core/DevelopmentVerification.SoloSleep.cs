#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        bool soloSleepFixture, soloSleepVerified, soloSleepCancelled, soloSleepMorning;
        bool soloSleepCritical, soloSleepContinuity, soloSleepPhysics;
        float soloSleepFixedStep, soloSleepUntil, soloSleepWokeAt, soloSleepWalkMetres;
        float soloSleepCriticalLoad, soloSleepCriticalCapacity, soloSleepTripAt;
        bool soloSleepWarningObserved;
        int soloSleepReportCount, soloSleepActualConsents;
        string soloSleepGuestId;

        // This mode never writes Clock.Speed in Update or LateUpdate. The ordinary
        // WaitController is its sole acceleration owner, including during GPU captures.
        void ObserveSoloSleepVerification()
        {
            Require(Time.timeScale == 1 && Time.fixedDeltaTime == soloSleepFixedStep,
                "bed sleep changes hotel time only; Unity physics cadence is unchanged");
            Require(session.Wait && session.Wait.isActiveAndEnabled, "the production WAIT/sleep owner stays enabled");
            if (session.Wait.IsSleeping)
                Require(session.Simulation.Clock.Speed == WaitController.SleepSpeed,
                    "only actual bed consent owns the expected eightfold model speed");
            else Require(session.Simulation.Clock.Speed == 1, "awake staff run at normal hotel speed");
        }

        IEnumerator VerifySoloSleep()
        {
            Require(soloTour && !continuousTour && !operationsUI && !presenceFixtures && !agencyFixtures && !serviceFixtures,
                "SOLO sleep is a separate production diagnostic, not a historical shift or three-day tour");
            Require(legacyVerificationConfig == null && session.Simulation.ContinuousOperations &&
                session.Simulation.AutomaticBookingsEnabled && session.Wait.isActiveAndEnabled,
                "production calendar, automatic sales and ordinary sleep policy are active");
            soloSleepFixedStep = Time.fixedDeltaTime;
            facts.Add("SOLO SLEEP SCOPE: two isolated production sessions, then a fresh reset. This is not the three-day economy tour or a human pacing/performance/AltTab test.");
            facts.Add("INPUT: only the driver's own background-capable synthetic pad. Every claimed bed consent/cancellation and heater switch uses the production reader and the actual first physical surface. No DebugStartSleep, ForceFailure, ForceTrip, forced activity, temperature or load override.");
            facts.Add("SETUP ADAPTERS: normal sales-policy commands and bounded ordinary AdvanceTime before sleep. The electrical scenario uses one real timed automatic contract, actual guest reception/room routes, and a timed model rack-key handoff. No guest route-completion callback is invoked by this driver.");
            facts.Add("PLACEMENT ADAPTERS: only explicitly empty employee approaches and two existing uncarried, switched-off heater bodies in room104. Room104-to-utility traversal and heater carrying are not claimed. The final utility-entry-to-bed walk, raycast, inputs and overload trip are actual.");
            yield return VerifySoloSleepMorning();
            yield return VerifySoloSleepPowerWake();
            var previous = session.Simulation;
            session.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            Require(!ReferenceEquals(previous, session.Simulation), "NewGame replaces only the explicitly finished diagnostic model");
            ValidateContinuousFreshReset();
            Require(!session.Wait.IsSleeping && !session.Wait.HasSleepConsent(0) && !session.Wait.HasSleepConsent(1) &&
                session.Wait.SleepUntil == 0 && session.Simulation.Clock.Speed == 1,
                "fresh session retains no sleep consent, deadline or acceleration");
            VerifySoloComposition();
            resetVerified = true;
            soloSleepVerified = soloSleepCancelled && soloSleepMorning && soloSleepCritical && soloSleepContinuity && soloSleepPhysics;
            Require(soloSleepVerified, "both independent sleep scenarios and the complete fresh reset passed");
            yield return Capture("solo-sleep-new-session", "Fresh production hotel after both isolated sleep scenarios / no old votes, faults, carried items or equipment upgrades");
        }

        IEnumerator VerifySoloSleepMorning()
        {
            yield return PrepareSoloSleepGameplay();
            var model = session.Simulation;
            CloseSoloSleepSales();
            AdvanceSoloSleepSetupTo(model.Calendar.At(1, 23));
            Require(model.Guests.Count == 0 && model.Reservations.Count == 0,
                "closed real sales leave this isolated quiet night without invented guests");
            var rooms = session.Rooms;
            var identities = rooms.ToArray();
            var clock = model.Clock; var boiler = model.Boiler;
            long epoch = LanSession.Instance ? LanSession.Instance.Epoch : 0;
            int reportCount = model.DayReports.Count, cash = model.Economy.Cash;
            float condition = model.Boiler.Condition;
            var bed = SoloSleepBed();
            yield return PlaceServiceStaff(new Vector3(.5f, .08f, 29.5f), new Vector3(.5f, 1.6f, 33));
            yield return WalkSoloSleepToBed(bed);
            yield return Capture("solo-sleep-staff-room", "Actual cot, shared narrow aisle and employee viewpoint after genuine controller walk from utility entrance");
            yield return ConsentSoloSleepBed(bed);
            Require(!session.Wait.HasSleepConsent(1), "SOLO does not invent a second employee's consent");
            yield return Capture("solo-sleep-active", "Actual bed consent active / ordinary WAIT owner advances only the hotel model at8x");
            Require(session.Wait.IsSleepCancellationArmed(0), "neutral input has armed the new cancellation edge");
            yield return PressMenu(GamepadButton.South);
            Require(!session.Wait.IsSleeping && session.Wait.WakeReason == StaffWakeReason.StaffCancelled &&
                session.Simulation.Clock.Speed == 1 && !session.Wait.HasSleepConsent(0),
                "fresh actual Use edge cancels sleep without reusing its held press");
            soloSleepCancelled = true;
            yield return ConsentSoloSleepBed(bed);
            soloSleepUntil = session.Wait.SleepUntil;
            Require(soloSleepUntil == model.Calendar.At(2, 6), "fresh consent is bound to the same next06 deadline");
            float before = model.Elapsed, realBefore = Time.realtimeSinceStartup;
            yield return new WaitForSecondsRealtime(.3f);
            float realElapsed = Time.realtimeSinceStartup - realBefore;
            Require(session.Wait.IsSleeping && model.Elapsed - before > realElapsed * 5 &&
                Time.timeScale == 1 && Time.fixedDeltaTime == soloSleepFixedStep,
                "actual sleep advances model time while leaving engine physics at1x");
            soloSleepPhysics = true;
            yield return Until(() => !session.Wait.IsSleeping, (soloSleepUntil - model.Elapsed) / WaitController.SleepSpeed + 8,
                "ordinary sleep ticks reach the06 boundary without a driver clock write");
            soloSleepWokeAt = model.Elapsed;
            Require(session.Wait.WakeReason == StaffWakeReason.Morning && Mathf.Abs(soloSleepWokeAt - soloSleepUntil) <= .4f &&
                model.Clock.Speed == 1 && !session.Wait.HasSleepConsent(0) && session.Wait.SleepUntil == 0,
                "06:00 wakes once and clears its old consent/deadline");
            Require(ReferenceEquals(session.Simulation, model) && ReferenceEquals(session.Rooms, rooms) &&
                ReferenceEquals(model.Clock, clock) && ReferenceEquals(model.Boiler, boiler) &&
                identities.Select((room, index) => ReferenceEquals(room, session.Rooms[index])).All(value => value) &&
                (LanSession.Instance ? LanSession.Instance.Epoch : 0) == epoch,
                "morning preserves the original model, rooms, clock, boiler and session epoch");
            soloSleepContinuity = true;
            soloSleepReportCount = model.DayReports.Count - reportCount;
            Require(soloSleepReportCount == 1 && model.DayReports.Last().Receipts.Count == 0 &&
                model.Economy.Cash == cash - session.Economy.DailyOperatingCost && model.Boiler.Condition < condition &&
                session.Phase == DayPhase.Service,
                "morning publishes one genuine bill/report, retains natural wear and leaves the hotel running");
            soloSleepMorning = true;
            facts.Add("MORNING PASS: target=" + SoloSleepNumber(soloSleepUntil) + " observed=" + SoloSleepNumber(soloSleepWokeAt) +
                " reportsAdded=" + soloSleepReportCount + " cashBefore=" + cash + " cashAfter=" + model.Economy.Cash +
                " conditionBefore=" + SoloSleepNumber(condition) + " conditionAfter=" + SoloSleepNumber(model.Boiler.Condition) + ".");
            yield return Capture("solo-sleep-morning", "Actual06:00 wake / same hotel and equipment / one scheduled report and operating bill");
        }

        IEnumerator VerifySoloSleepPowerWake()
        {
            session.NewGame(); ManagementUI.Instance.Close();
            yield return null; yield return null;
            yield return PrepareSoloSleepGameplay();
            var model = session.Simulation;
            CloseSoloSleepSales();
            var policy = model.RoomSalesPolicies.Single(row => row.RoomId == 104);
            Require(session.SetRoomSalesPolicy(0, 104, true, session.Economy.MinPrice, policy.Revision).Success,
                "offer one actual room at a permitted rate before its scheduled demand decision");
            AdvanceSoloSleepSetupTo(model.Calendar.At(1, 12.2f));
            Require(model.Reservations.Count == 1 && model.Reservations[0].IsAutomatic && model.Guests.Count == 0,
                "one future reservation arose from real automatic sales, without manual acceptance or arrival injection");
            var reservation = model.Reservations[0];
            Require(reservation.RoomId == 104 && reservation.Status == ReservationStatus.Reserved,
                "automatic contract belongs to the intended real electrical room");
            CloseSoloSleepSales();
            var heaters = FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).OrderBy(item => item.heaterId).ToArray();
            Require(heaters.Length == 2 && heaters.All(item => !item.IsCarried && item.State != null && !item.State.SwitchedOn),
                "use exactly the two authored uncarried off heaters, without spawning consumers");
            for (int index = 0; index < heaters.Length; index++)
            {
                heaters[index].Body.position = new Vector3(3.85f, .04f, index == 0 ? 18.6f : 16);
                heaters[index].Body.rotation = Quaternion.identity;
                heaters[index].Body.linearVelocity = heaters[index].Body.angularVelocity = Vector3.zero;
            }
            yield return Until(() => heaters.All(item => item.State.RoomId == 104 && !item.IsCarried &&
                item.Body.linearVelocity.sqrMagnitude < .05f), 5, "placed existing heater bodies settle in the actual room volume");
            AdvanceSoloSleepSetupTo(reservation.Offer.ArrivalAt + .25f);
            yield return Until(() => model.Guests.Any(item => item.GuestId == reservation.Id), 3, "ordinary calendar materializes the due contract");
            var guest = model.Guests.Single(item => item.GuestId == reservation.Id);
            soloSleepGuestId = guest.GuestId;
            yield return Until(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn &&
                guest.Agent.WaitingSeconds >= model.LivingSettings.KeyRetrievalEstimateSeconds, 65,
                "production guest body reaches reception and allows a bounded key-retrieval estimate");
            Require(model.Keys.PickUp(0, guest.RoomId).Success, "labelled timed model rack-key pickup");
            Require(model.CheckIn(0, guest.GuestId).Success, "labelled timed model key handoff after actual reception arrival");
            yield return Until(() => guest.Agent.HasReachedRoom && guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 65,
                "production guest body completes the room route and actual activity anchor");
            var circuit = model.Electrical.Find("B");
            Require(circuit.HasPower && !circuit.Tripped && circuit.RequestedLoad > 0 && circuit.RequestedLoad < circuit.Capacity &&
                !model.Boiler.Failed, "real present guest alone is below the branch limit and central heating still works");
            facts.Add("POWER SETUP: automaticGuest=" + guest.GuestId + " room=" + guest.RoomId +
                " state=" + guest.Agent.State + " activity=" + guest.Agent.Activity + " baselineB=" + SoloSleepNumber(circuit.RequestedLoad) +
                "/" + SoloSleepNumber(circuit.Capacity) + "; guest presentation routes remained enabled; only key commands were adapted.");
            yield return PlaceServiceStaff(new Vector3(4.75f, .08f, 17), heaters[0].placementCollider.bounds.center);
            foreach (var item in heaters)
            {
                yield return AimServicePoint(() => item.placementCollider.bounds.center);
                yield return Until(() => coop.Players[0].Interactor.Focused == item, 2, "actual heater is the focused use surface");
                Require(SoloSleepFirstSurface() == item, "the real heater, not an occluded target, receives Use");
                yield return PressMenu(GamepadButton.South);
                Require(item.State.SwitchedOn && item.State.Powered, "actual heater switch input enables its consumer and heat");
            }
            soloSleepCriticalLoad = circuit.RequestedLoad; soloSleepCriticalCapacity = circuit.Capacity;
            Require(soloSleepCriticalLoad > soloSleepCriticalCapacity && !circuit.Tripped &&
                model.Electrical.Consumers.Count(item => item.CircuitId == "B" && item.Id.StartsWith("heater:", StringComparison.Ordinal) && item.RequestedLoad > 0) == 2,
                "actual guest plus both switched heaters create sustained branch overload");
            // Explicit empty-rig approach fixture. We claim neither a carry route nor the
            // room104-to-utility journey within the production18-second overload window.
            yield return PlaceServiceStaff(new Vector3(.5f, .08f, 29.5f), new Vector3(.5f, 1.6f, 33));
            var bed = SoloSleepBed();
            yield return WalkSoloSleepToBed(bed);
            Require(!circuit.Tripped && guest.Agent.InAssignedRoom, "the genuine final walk reaches the bed before the loaded branch trips");
            yield return ConsentSoloSleepBed(bed);
            yield return Until(() => circuit.Warning || circuit.Tripped, 4, "ordinary overload stress reaches its warning threshold");
            Require(circuit.Warning && !circuit.Tripped && session.Wait.IsSleeping,
                "the observed real warning permits sleep; no claim that its onset followed initial consent");
            soloSleepWarningObserved = true;
            facts.Add("POWER WARNING WHILE ASLEEP: elapsed=" + SoloSleepNumber(model.Elapsed) +
                " storedStressSeconds=" + SoloSleepNumber(circuit.OverloadSeconds) + " demand=" + SoloSleepNumber(circuit.RequestedLoad) + ".");
            yield return Until(() => circuit.Tripped, 6, "continued actual consumer load trips the ordinary breaker timer");
            soloSleepTripAt = model.Elapsed;
            Require(!session.Wait.IsSleeping && session.Wait.WakeReason == StaffWakeReason.CircuitTrip &&
                model.Clock.Speed == 1 && session.Wait.SleepUntil == 0 && !session.Wait.HasSleepConsent(0),
                "the normal typed outage observer wakes staff without a separate driver stop command");
            Require(circuit.TripCount == 1 && heaters.All(item => !item.State.Powered && item.State.EffectiveHeatOutput == 0) &&
                model.Electrical.Find("A").HasPower && !model.Boiler.Failed,
                "the genuine trip removes heater delivery, preserves other-branch power and is not a boiler failure");
            soloSleepCritical = true;
            facts.Add("POWER WAKE PASS: cause=" + session.Wait.WakeReason + " elapsed=" + SoloSleepNumber(soloSleepTripAt) +
                " demand=" + SoloSleepNumber(circuit.RequestedLoad) + "/" + SoloSleepNumber(circuit.Capacity) +
                " trips=" + circuit.TripCount + " heaterHeat=0 AHasPower=True CentralHeatFailed=False.");
            yield return Capture("solo-sleep-critical-wake", "Actual loaded circuitB trip woke sleeping staff / no forced fault, no clock or wake command");
        }

        void CloseSoloSleepSales()
        {
            foreach (var row in session.Simulation.RoomSalesPolicies)
                Require(session.SetRoomSalesPolicy(0, row.RoomId, false, row.Price, row.Revision).Success,
                    "ordinary room policy closes new sales without cancelling a contract");
        }

        void AdvanceSoloSleepSetupTo(float destination)
        {
            Require(!session.Wait.IsSleeping && !session.Wait.HasSleepConsent(0) && destination >= session.Simulation.Elapsed,
                "bounded setup time is advanced only before real sleep consent");
            session.AdvanceTime(destination - session.Simulation.Elapsed);
            Require(session.Simulation.Elapsed >= destination - .02f && session.Simulation.Clock.Speed == 1,
                "setup uses normal model ticks with no forced speed or direct calendar write");
        }

        StaffBedInteraction SoloSleepBed() => FindObjectsByType<StaffBedInteraction>(FindObjectsSortMode.None).Single(item => item.bedId == 0);

        IEnumerator PrepareSoloSleepGameplay()
        {
            yield return Until(() => session.Plan != null && coop.Players[0] &&
                coop.Players[0].DeviceReady && !coop.IsPaused, 4, "normal SOLO model and owned staff input are ready");
            // The welcome ledger opens in ManagementUI.Update after the first usable
            // model. Closing before that Update races its legitimate startup opening.
            yield return null; yield return null;
            var ui = ManagementUI.Instance;
            Require(ui, "the normal management interface exists");
            if (ui.IsOpen)
            {
                Require(ui.IsOperationsOpen && ui.Owner == 0, "only the local startup ledger may be dismissed");
                yield return PressMenu(GamepadButton.East);
                yield return Until(() => !ui.IsOpen, 3, "owned controller Back closes the startup ledger before walking");
                facts.Add("SLEEP STARTUP: actual owned-pad Back closed the welcome ledger after normal model/UI readiness; no UI-block override.");
            }
            yield return Until(() => !ui.IsOpen && !coop.Players[0].IsUIBlocked && !coop.IsPaused,
                3, "ordinary SOLO gameplay input is unblocked before the physical route");
        }

        IEnumerator WalkSoloSleepToBed(StaffBedInteraction bed)
        {
            foreach (var point in new[] { new Vector3(.5f, 0, 33), new Vector3(-2.5f, 0, 34.4f),
                new Vector3(-4.1f, 0, 34.4f), bed.standingAnchor.position })
                yield return WalkSoloSleepSegment(point);
            yield return AimServicePoint(() => bed.InteractionPoint);
            yield return Until(() => coop.Players[0].Interactor.Focused == bed, 2, "actual bed focus after the complete physical niche route");
            Require(SoloSleepFirstSurface() == bed, "no wall, furniture or other interaction masks the actual bed surface");
        }

        IEnumerator WalkSoloSleepSegment(Vector3 destination)
        {
            var actor = coop.Players[0]; actor.enabled = true;
            Require(actor.Interactor.HeldBody == null && session.Simulation.Clock.Speed == 1, "actual niche walk starts empty-handed at ordinary clock speed");
            Require(!actor.IsUIBlocked && !coop.IsPaused && !ManagementUI.Instance.IsOpen,
                "actual niche walk requires the startup menu to be closed normally");
            float deadline = Time.realtimeSinceStartup + 12;
            while (ServiceDistance(actor.transform.position, destination) > .065f && Time.realtimeSinceStartup < deadline)
            {
                Vector3 previous = actor.transform.position;
                Vector3 delta = destination - previous; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                float speed = Mathf.Clamp(delta.magnitude * 1.5f, .14f, .8f);
                InputSystem.QueueStateEvent(verificationPads[0], new GamepadState { leftStick = new Vector2(local.x, local.z) * speed });
                yield return null;
                soloSleepWalkMetres += ServiceDistance(previous, actor.transform.position);
            }
            InputSystem.QueueStateEvent(verificationPads[0], new GamepadState()); yield return null; yield return null;
            Require(ServiceDistance(actor.transform.position, destination) < .14f && actor.BodyCollider.isGrounded,
                "actual empty staff route reaches " + destination.ToString("F3") + "; actual=" + actor.transform.position.ToString("F3") +
                "; active=" + actor.isActiveAndEnabled + "; device=" + actor.DeviceReady + "; paused=" + coop.IsPaused +
                "; uiBlocked=" + actor.IsUIBlocked + "; managementOpen=" + ManagementUI.Instance.IsOpen);
        }

        IEnumerator ConsentSoloSleepBed(StaffBedInteraction bed)
        {
            yield return AimServicePoint(() => bed.InteractionPoint);
            Require(coop.Players[0].Interactor.Focused == bed && SoloSleepFirstSurface() == bed,
                "fresh bed Use requires the same actual first-hit target");
            yield return PressMenu(GamepadButton.South);
            Require(session.Wait.IsSleeping && session.Wait.HasSleepConsent(0) && session.Wait.SleepBedId(0) == bed.bedId &&
                !session.Wait.HasSleepConsent(1) && session.Simulation.Clock.Speed == WaitController.SleepSpeed,
                "production input reader and physical bed create only SOLO's real consent");
            soloSleepActualConsents++;
        }

        HotelInteractable SoloSleepFirstSurface()
        {
            var actor = coop.Players[0];
            return Physics.RaycastAll(actor.PlayerCamera.transform.position, actor.PlayerCamera.transform.forward,
                actor.Interactor.reach, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.collider.transform.IsChildOf(actor.transform)).OrderBy(hit => hit.distance)
                .Select(hit => hit.collider.GetComponentInParent<HotelInteractable>()).FirstOrDefault();
        }

        static string SoloSleepNumber(float value) => value.ToString("F3", CultureInfo.InvariantCulture);

        void WriteSoloSleepReport(string outcome)
        {
            var text = new StringBuilder();
            text.AppendLine("Built-player SOLO physical sleep diagnostic / two isolated scenarios, not a three-day tour");
            text.AppendLine("ApplicationVersion=" + Application.version + " UnityVersion=" + Application.unityVersion);
            text.AppendLine("Outcome=" + outcome + " Errors=" + errors + " ResetVerified=" + resetVerified);
            text.AppendLine("Mode=Solo SyntheticPads=" + ActiveActors);
            text.AppendLine("SoloSleepVerified=" + soloSleepVerified + " ActualBedInput=" + (soloSleepActualConsents == 3) + " DriverClockOverride=False");
            text.AppendLine("FreshCancelVerified=" + soloSleepCancelled + " MorningWakeVerified=" + soloSleepMorning +
                " CriticalCircuitWakeVerified=" + soloSleepCritical + " ModelWorldContinuity=" + soloSleepContinuity +
                " ModelOnlyAcceleration=" + soloSleepPhysics);
            text.AppendLine("MorningDeadline=" + SoloSleepNumber(soloSleepUntil) + " ObservedMorning=" + SoloSleepNumber(soloSleepWokeAt) +
                " ReportsAdded=" + soloSleepReportCount + " ActualStaffWalkMetres=" + SoloSleepNumber(soloSleepWalkMetres));
            text.AppendLine("AutomaticPowerGuest=" + (soloSleepGuestId ?? "none") + " CircuitLoad=" + SoloSleepNumber(soloSleepCriticalLoad) +
                " CircuitCapacity=" + SoloSleepNumber(soloSleepCriticalCapacity) + " WarningObservedWhileAsleep=" + soloSleepWarningObserved +
                " ActualTripAt=" + SoloSleepNumber(soloSleepTripAt));
            text.AppendLine("RuntimeSeconds=" + SoloSleepNumber(Time.realtimeSinceStartup - began) + " Captures=" + captures.Count +
                " OffscreenGPUAvailable=" + offscreenAvailable);
            text.AppendLine("No physical heater-carry, room104-to-utility traversal, human pacing/performance, long-pause or AltTab-fix claim. GPU image legibility needs separate manual review.");
            foreach (string fact in facts) text.AppendLine(fact);
            File.WriteAllText(Path.Combine(output, "runtime-verification.txt"), text.ToString());
        }
    }
}
#endif
