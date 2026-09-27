#if UNITY_EDITOR || DEVELOPMENT_BUILD
namespace WorstHotel
{
    public sealed partial class WaitController
    {
        /// <summary>Explicit F2 fixture: SOLO only, at an actual reachable bed. Never a network command.</summary>
        public CommandResult DebugStartSleep()
        {
            var coop = LocalCoopBootstrap.Instance;
            if (!session || session.IsLanReplica || !coop || !coop.IsSolo || coop.LanRole != LanRole.Offline ||
                LanSession.Instance && LanSession.Instance.IsActive)
                return CommandResult.Fail("Diagnostic sleep requires an offline SOLO hotel.");
            if (!SleepEnvironmentReady(coop, out _, out string reason)) return CommandResult.Fail(reason);
            if (CriticalSleepReason(out reason) != StaffWakeReason.None) return CommandResult.Fail(reason);
            if (SleepRevision >= long.MaxValue - 8) return CommandResult.Fail("Start a fresh hotel session.");
            int bedId = -1;
            for (int i = 0; i < 2; i++)
                if (StaffBedInteraction.TryFind(i, out var bed) && FirstSleepSurface(coop.Players[0], bed)) { bedId = i; break; }
            if (bedId < 0) return CommandResult.Fail("Stand beside a staff bed and look at it before opening F2.");
            Stop("Diagnostic bed consent.");
            sleepReady[0] = true; sleepBeds[0] = bedId; sleepStaffCount = 1;
            sleepCancelArmed[0] = sleepMoveNeutral[0] = sleepLookNeutral[0] = false;
            sleepMustRelease[0] = false;
            SleepUntil = NextMorningAt(session.Simulation.Calendar, session.Simulation.Elapsed);
            WakeReason = StaffWakeReason.None; Mode = HotelAdvanceMode.Sleep;
            session.Simulation.Clock.SetSpeed(SleepSpeed);
            AdvanceViewRevision(); observedRevision = session.Simulation.EventRevision;
            Reason = "Diagnostic SOLO bed consent. Ordinary wake rules are active.";
            session.RaiseChanged();
            return CommandResult.Ok(Reason);
        }
    }
}
#endif
