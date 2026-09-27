using System;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class WaitController
    {
        public const float SleepSpeed = 8;
        public const float MorningHour = 6;
        public HotelAdvanceMode Mode { get; private set; }
        public bool IsSleeping => Mode == HotelAdvanceMode.Sleep;
        public long SleepRevision { get; private set; }
        public float SleepUntil { get; private set; }
        public StaffWakeReason WakeReason { get; private set; }
        public bool HasSleepConsent(int actorId) => actorId >= 0 && actorId < 2 && sleepReady[actorId];
        public int SleepBedId(int actorId) => HasSleepConsent(actorId) ? sleepBeds[actorId] : -1;
        public bool IsSleepCancellationArmed(int actorId) => actorId >= 0 && actorId < 2 && sleepCancelArmed[actorId];
        readonly bool[] sleepReady = new bool[2], sleepCancelArmed = new bool[2], sleepMustRelease = new bool[2];
        readonly bool[] sleepMoveNeutral = new bool[2], sleepLookNeutral = new bool[2];
        readonly int[] sleepBeds = { -1, -1 };
        readonly RaycastHit[] sleepHits = new RaycastHit[32];
        int sleepStaffCount;
        bool HasSleepConsentInProgress => sleepReady[0] || sleepReady[1];

        public static float NextMorningAt(HotelCalendar calendar, float now)
        {
            if (calendar == null || !Number.IsFinite(now) || now < 0) throw new ArgumentException("A sleep deadline needs a valid hotel calendar and time.");
            int day = calendar.DayAt(now);
            float morning = calendar.At(day, MorningHour);
            return morning > now ? morning : calendar.At(checked(day + 1), MorningHour);
        }

        public StaffSleepView CaptureSleepView() => new StaffSleepView(Mode, SleepRevision, SleepUntil, WakeReason,
            new StaffSleepConsent(0, SleepBedId(0), sleepReady[0]), new StaffSleepConsent(1, SleepBedId(1), sleepReady[1]));

        /// <summary>Called only after the host frame has been completely validated and applied.</summary>
        public void ApplyLanSleepView(StaffSleepView view)
        {
            if (!session || !session.IsLanReplica) return;
            Mode = view.Mode; SleepRevision = view.Revision; SleepUntil = view.Until; WakeReason = view.WakeReason;
            sleepReady[0] = view.Staff0.Ready; sleepReady[1] = view.Staff1.Ready;
            sleepBeds[0] = view.Staff0.BedId; sleepBeds[1] = view.Staff1.BedId;
            for (int i = 0; i < 2; i++) sleepCancelArmed[i] = sleepMoveNeutral[i] = sleepLookNeutral[i] = false;
        }

        /// <summary>A new session/epoch discards even a previous replica's local presentation immediately.</summary>
        public void ResetForSession()
        {
            if (!session) session = GetComponent<GameSession>();
            session?.Simulation?.Clock.SetSpeed(1); // The old mirror clock is itself read-only.
            bool changed = Mode != HotelAdvanceMode.None || HasSleepConsentInProgress || SleepUntil != 0 || WakeReason != StaffWakeReason.SessionChanged;
            Mode = HotelAdvanceMode.None; SleepUntil = 0; WakeReason = StaffWakeReason.SessionChanged;
            sleepStaffCount = 0;
            for (int i = 0; i < 2; i++)
            {
                sleepReady[i] = sleepCancelArmed[i] = sleepMoveNeutral[i] = sleepLookNeutral[i] = false;
                sleepBeds[i] = -1; sleepMustRelease[i] = true;
                votes[i] = false; heldSeconds[i] = 0; mustRelease[i] = true;
            }
            if (changed) AdvanceViewRevision();
            Reason = SleepWakeText(StaffWakeReason.SessionChanged);
        }

        void AdvanceViewRevision() { if (SleepRevision < long.MaxValue) SleepRevision++; }
        void BeginWaitMode()
        {
            if (Mode != HotelAdvanceMode.Wait || WakeReason != StaffWakeReason.None)
            { Mode = HotelAdvanceMode.Wait; WakeReason = StaffWakeReason.None; AdvanceViewRevision(); }
        }

        public CommandResult CanUseBed(int actorId, int bedId)
        {
            if (!session || session.IsLanReplica) return CommandResult.Fail("Only the host can accept physical bed use.");
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || actorId < 0 || actorId >= coop.RequiredStaffCount || !StaffBedInteraction.TryFind(bedId, out _))
                return CommandResult.Fail("Choose an available staff bed.");
            if (sleepMustRelease[actorId]) return CommandResult.Fail("Release Use before choosing a bed again.");
            if (HasSleepConsent(actorId)) return CommandResult.Fail("Already ready. Press again after release to cancel sleep.");
            if (HasSleepConsentInProgress && SleepUntil <= session.Simulation.Elapsed)
                return CommandResult.Fail("That morning has arrived. Choose a bed again.");
            for (int i = 0; i < 2; i++)
                if (sleepReady[i] && sleepBeds[i] == bedId) return CommandResult.Fail("Your colleague is using this bed.");
            if (!SleepEnvironmentReady(coop, out _, out string reason)) return CommandResult.Fail(reason);
            if (CriticalSleepReason(out reason) != StaffWakeReason.None) return CommandResult.Fail(reason);
            if (SleepRevision >= long.MaxValue - 8) return CommandResult.Fail("Sleep consent needs a fresh hotel session.");
            return CommandResult.Ok();
        }

        public CommandResult TrySleep(int actorId, int bedId)
        {
            var allowed = CanUseBed(actorId, bedId);
            if (!allowed.Success) { Reason = allowed.Message; return allowed; }
            var coop = LocalCoopBootstrap.Instance;
            var player = coop.Players[actorId];
            if (!player.Input.PrimaryPressed || !player.HasWorldAuthority || !StaffBedInteraction.TryFind(bedId, out var bed) ||
                player.Interactor.Focused != bed || !FirstSleepSurface(player, bed))
                return CommandResult.Fail("Use the actual nearby bed with a fresh button press.");
            // WAIT and bed votes cannot lend consent to each other.
            if (Mode == HotelAdvanceMode.Wait || HasConsentInProgress) Stop("Bed consent replaces WAIT.");
            if (!HasSleepConsentInProgress)
            {
                SleepUntil = NextMorningAt(session.Simulation.Calendar, session.Simulation.Elapsed);
                sleepStaffCount = coop.RequiredStaffCount;
            }
            sleepReady[actorId] = true; sleepBeds[actorId] = bedId;
            sleepCancelArmed[actorId] = sleepMoveNeutral[actorId] = sleepLookNeutral[actorId] = false;
            WakeReason = StaffWakeReason.None;
            Mode = sleepReady[0] && (coop.IsSolo || sleepReady[1]) ? HotelAdvanceMode.Sleep : HotelAdvanceMode.None;
            session.Simulation.Clock.SetSpeed(IsSleeping ? SleepSpeed : 1);
            AdvanceViewRevision(); observedRevision = session.Simulation.EventRevision;
            Reason = IsSleeping ? "Sleeping until 06:00. Press Use again, move, or pause to wake." : "Ready for sleep. Your colleague must use the other bed.";
            session.RaiseChanged();
            return CommandResult.Ok(Reason);
        }

        bool FirstSleepSurface(FirstPersonController player, StaffBedInteraction bed)
        {
            float distance = float.PositiveInfinity; Collider first = null;
            int count = Physics.RaycastNonAlloc(player.PlayerCamera.transform.position, player.PlayerCamera.transform.forward,
                sleepHits, player.Interactor.reach, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = sleepHits[i];
                if (hit.collider.transform.IsChildOf(player.transform) || hit.distance >= distance) continue;
                distance = hit.distance; first = hit.collider;
            }
            return first && first.GetComponentInParent<StaffBedInteraction>() == bed;
        }

        public CommandResult CancelSleep(int actorId)
        {
            if (!session || session.IsLanReplica) return CommandResult.Fail("Only the host can cancel shared sleep.");
            var coop = LocalCoopBootstrap.Instance;
            if (!coop || actorId < 0 || actorId >= coop.RequiredStaffCount || !HasSleepConsentInProgress)
                return CommandResult.Fail("There is no staff sleep consent to cancel.");
            RevokeSleep(StaffWakeReason.StaffCancelled);
            return CommandResult.Ok(Reason);
        }

        public void RevokeSleep(StaffWakeReason reason)
        {
            if (!session || session.IsLanReplica) return;
            if (!Enum.IsDefined(typeof(StaffWakeReason), reason)) return;
            bool changed = Mode != HotelAdvanceMode.None || HasSleepConsentInProgress || SleepUntil != 0 || WakeReason != reason;
            Mode = HotelAdvanceMode.None; SleepUntil = 0; WakeReason = reason;
            session.Simulation?.Clock.SetSpeed(1);
            for (int i = 0; i < 2; i++)
            {
                sleepReady[i] = sleepCancelArmed[i] = sleepMoveNeutral[i] = sleepLookNeutral[i] = false;
                sleepBeds[i] = -1; sleepMustRelease[i] = true;
                votes[i] = false; heldSeconds[i] = 0; mustRelease[i] = true;
            }
            if (changed) AdvanceViewRevision();
            Reason = SleepWakeText(reason);
        }

        void StopAdvanceView()
        {
            // Repeated ordinary Stop calls must not overwrite an already recorded disconnect/wake reason.
            if (HasSleepConsentInProgress || IsSleeping) RevokeSleep(StaffWakeReason.Stopped);
            else if (Mode != HotelAdvanceMode.None) { Mode = HotelAdvanceMode.None; AdvanceViewRevision(); }
        }

        public float ClampSleepStep(float requested)
        {
            if (!HasSleepConsentInProgress || session == null || session.Simulation == null) return requested;
            return Mathf.Max(0, Mathf.Min(requested, SleepUntil - session.Simulation.Elapsed));
        }

        bool SleepEnvironmentReady(LocalCoopBootstrap coop, out StaffWakeReason cause, out string reason)
        {
            cause = StaffWakeReason.Stopped; reason = "Sleep is available in a running continuous hotel.";
            if (!session || session.Simulation == null || !session.Simulation.ContinuousOperations || !session.Simulation.Running || session.Phase != DayPhase.Service)
                return false;
            if (LanSession.Instance && LanSession.Instance.MenuOpen)
            { cause = StaffWakeReason.MenuOpened; reason = "Close the session menu before sleeping."; return false; }
            if (coop.IsPaused) { cause = StaffWakeReason.Paused; reason = "Hotel paused."; return false; }
            if (!coop.HasApplicationFocus || coop.LanRole == LanRole.Host && coop.RemoteInputLeaseExpired)
            { cause = StaffWakeReason.InputExpired; reason = "Fresh staff input is required before sleeping."; return false; }
            if (coop.WaitingForDevices || !coop.IsSolo && coop.DebugKeyboardOnly)
            { cause = StaffWakeReason.DeviceUnavailable; reason = "Every staff member needs their connected controller."; return false; }
            for (int i = 0; i < coop.RequiredStaffCount; i++)
            {
                var player = coop.Players[i];
                if (!player || !player.DeviceReady)
                { cause = StaffWakeReason.DeviceUnavailable; reason = "A staff controller is disconnected."; return false; }
                if (player.IsUIBlocked)
                { cause = StaffWakeReason.MenuOpened; reason = "Close the hotel ledger before sleeping."; return false; }
                if (sleepReady[i] && (!StaffBedInteraction.TryFind(sleepBeds[i], out var bed) ||
                    Vector3.Distance(player.PlayerCamera.transform.position, bed.InteractionPoint) > player.Interactor.reach + .25f))
                { cause = StaffWakeReason.StaffCancelled; reason = "Staff moved away from their bed."; return false; }
                if (player.Interactor.HeldBody || player.Input.PrimaryHeld && !(player.Interactor.Focused is StaffBedInteraction))
                { cause = StaffWakeReason.StaffCancelled; reason = "Put down objects and finish physical work before sleeping."; return false; }
            }
            cause = StaffWakeReason.None; reason = null; return true;
        }

        StaffWakeReason CriticalSleepReason(out string reason)
        {
            var model = session.Simulation;
            if (model.Boiler.Failed && !(model.Boiler.MaintenanceInProgress && model.Boiler.ActiveServiceKind == BoilerServiceKind.Full))
            { reason = "The boiler has failed. Respond before sleeping."; return StaffWakeReason.BoilerFailure; }
            if (model.Electrical != null)
                foreach (var circuit in model.Electrical.Circuits)
                    if (circuit.Tripped)
                    { reason = "Circuit " + circuit.Id + " has tripped. Restore power before sleeping."; return StaffWakeReason.CircuitTrip; }
            foreach (var incident in model.Incidents.Items)
                if (incident.Active && incident.HasContactedStaff && !incident.PausedForTransfer && incident.Stage == SituationStage.Critical)
                { reason = "Room " + incident.RoomId + " has a communicated critical problem."; return StaffWakeReason.CriticalGuest; }
            reason = null; return StaffWakeReason.None;
        }

        // Returns true when sleep owns or just ended this Update, so held WAIT cannot join it.
        bool UpdateSleep(LocalCoopBootstrap coop)
        {
            bool pending = HasSleepConsentInProgress;
            if (!SleepEnvironmentReady(coop, out var blocked, out _))
            {
                if (pending) RevokeSleep(blocked);
                return pending; // Invalid/stripped input never rearms a released button.
            }
            if (pending && sleepStaffCount != coop.RequiredStaffCount)
            { RevokeSleep(StaffWakeReason.SessionChanged); return true; }
            for (int i = 0; i < coop.RequiredStaffCount; i++)
            {
                var input = coop.Players[i].Input;
                if (!input.PrimaryHeld) sleepMustRelease[i] = false;
                if (!sleepReady[i]) continue; // A colleague may walk to the unoccupied bed.
                bool wasArmed = sleepCancelArmed[i];
                bool move = input.Move.sqrMagnitude > .0225f;
                var player = coop.Players[i];
                Vector2 degrees = input.Look * (input.LookIsDegrees ? 1 : input.IsMouseLook ? player.mouseSensitivity : player.controllerLookSpeed * Time.unscaledDeltaTime);
                bool look = degrees.sqrMagnitude > .04f;
                if (wasArmed && (input.PrimaryPressed || input.SecondaryPressed || input.GrabPressed ||
                    input.MenuCancelPressed || input.PausePressed || sleepMoveNeutral[i] && move || sleepLookNeutral[i] && look))
                { RevokeSleep(StaffWakeReason.StaffCancelled); return true; }
                if (!input.PrimaryHeld)
                {
                    sleepCancelArmed[i] = true;
                    if (!move) sleepMoveNeutral[i] = true;
                    if (!look) sleepLookNeutral[i] = true;
                }
            }
            if (pending) ObserveSleepAfterTick();
            return pending;
        }

        void ObserveSleepAfterTick()
        {
            if (!HasSleepConsentInProgress || !session || session.IsLanReplica) return;
            var coop = LocalCoopBootstrap.Instance;
            if (!coop) { RevokeSleep(StaffWakeReason.DeviceUnavailable); return; }
            if (!SleepEnvironmentReady(coop, out var blocked, out _)) { RevokeSleep(blocked); return; }
            var critical = CriticalSleepReason(out string detail);
            if (critical != StaffWakeReason.None) { RevokeSleep(critical); Reason = detail; return; }
            if (session.Simulation.Elapsed >= SleepUntil) RevokeSleep(StaffWakeReason.Morning);
        }

        static string SleepWakeText(StaffWakeReason reason) => reason == StaffWakeReason.Morning ? "06:00 — time to run the hotel." :
            reason == StaffWakeReason.StaffCancelled ? "Staff resumed work. Use the beds again for fresh consent." :
            reason == StaffWakeReason.BoilerFailure ? "The boiler has failed. Staff are awake." :
            reason == StaffWakeReason.CircuitTrip ? "A circuit has tripped. Staff are awake." :
            reason == StaffWakeReason.CriticalGuest ? "A guest has reported a critical problem. Staff are awake." :
            reason == StaffWakeReason.Paused ? "Hotel paused; sleep consent cleared." :
            reason == StaffWakeReason.InputExpired ? "Staff input expired; fresh bed consent is required." :
            reason == StaffWakeReason.DeviceUnavailable ? "A staff controller disconnected; fresh bed consent is required." :
            reason == StaffWakeReason.MenuOpened ? "Menu opened; sleep consent cleared." :
            reason == StaffWakeReason.SessionChanged ? "Session changed; fresh bed consent is required." : "Sleep stopped.";
    }
}
