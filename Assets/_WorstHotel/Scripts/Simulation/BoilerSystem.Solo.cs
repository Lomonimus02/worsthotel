using System;

namespace WorstHotel
{
    public sealed partial class BoilerSystem
    {
        SoloAssistSettings soloAssist;
        bool soloLatchNeedsRelease;
        public bool SoloAssistEnabled => soloAssist != null;
        public float SoloValveHoldProgress { get; private set; }
        public float SoloLatchSecondsRemaining { get; private set; }
        public bool SoloValveLatched => SoloAssistEnabled && Failed && !MaintenanceInProgress && SoloLatchSecondsRemaining > 0;
        public float SoloValveRequiredHoldSeconds => soloAssist != null ? soloAssist.SafeValveHoldSeconds : 0;

        public void ConfigureSoloAssist(SoloAssistSettings settings)
        {
            if (ReadOnlyMirror) return;
            CancelSoloLatch(); soloAssist = settings;
        }

        /// <summary>Called only by a currently authenticated physical relief hold, using active real time.</summary>
        public CommandResult HoldSoloValve(int actorId, float realSeconds)
        {
            if (!Number.IsFinite(realSeconds) || realSeconds < 0) throw new ArgumentOutOfRangeException(nameof(realSeconds));
            if (ReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (!SoloAssistEnabled || actorId != 0 || !Failed || MaintenanceInProgress || ReliefActorId != actorId)
                return CommandResult.Fail("The mechanical valve catch is available only to the solo valve operator.");
            if (SoloValveLatched || soloLatchNeedsRelease) return CommandResult.Ok();
            if (!InRepairBand) { SoloValveHoldProgress = 0; return CommandResult.Fail("Hold pressure inside the green band to engage the catch."); }
            SoloValveHoldProgress = Math.Min(1, SoloValveHoldProgress + realSeconds / soloAssist.SafeValveHoldSeconds);
            if (SoloValveHoldProgress >= 1)
            {
                SoloLatchSecondsRemaining = soloAssist.ValveLatchSeconds;
                soloLatchNeedsRelease = true;
                return CommandResult.Ok("Valve mechanically secured. Release and complete the marked controls before the catch expires.");
            }
            return CommandResult.Ok("Keep holding relief in the green band to engage the mechanical catch.");
        }

        /// <summary>Independent of accelerated hotel time. Pauses/disable explicitly cancel the catch.</summary>
        public void AdvanceSoloLatch(float realSeconds)
        {
            if (!Number.IsFinite(realSeconds) || realSeconds < 0) throw new ArgumentOutOfRangeException(nameof(realSeconds));
            if (ReadOnlyMirror || SoloLatchSecondsRemaining <= 0) return;
            SoloLatchSecondsRemaining = Math.Max(0, SoloLatchSecondsRemaining - realSeconds);
            if (!Failed || !InRepairBand || MaintenanceInProgress) SoloLatchSecondsRemaining = 0;
            if (SoloLatchSecondsRemaining <= 0) SoloValveHoldProgress = 0;
        }

        public void ReleaseSoloValveCharge()
        {
            if (ReadOnlyMirror) return;
            SoloValveHoldProgress = 0; soloLatchNeedsRelease = false;
        }

        public void CancelSoloLatch()
        {
            if (ReadOnlyMirror) return;
            SoloLatchSecondsRemaining = SoloValveHoldProgress = 0; soloLatchNeedsRelease = false;
        }
    }
}
