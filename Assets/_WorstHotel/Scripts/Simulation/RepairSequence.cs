using System;

namespace WorstHotel
{
    public enum RepairControlKind { ReliefValve, Panel, Breaker, LatchA, LatchB, Restart }
    public enum RepairStep { Idle, Panel, Breaker, LatchA, LatchB, Restart, Complete }

    /// <summary>Ordered mechanical commands. The physical controller authenticates current actor holds/range.</summary>
    public sealed class RepairSequence
    {
        public RepairStep Step { get; private set; }
        public int OperatorActorId { get; private set; } = -1;
        public float LatchAProgress { get; private set; }
        public float LatchBProgress { get; private set; }
        public bool PanelOpen => Step >= RepairStep.Breaker;
        public bool BreakerIsolated => Step >= RepairStep.LatchA && Step < RepairStep.Complete;
        public string Status { get; private set; } = "Heating is running.";
        private readonly BoilerSystem boiler;
        private readonly BoilerSettings settings;
        private RepairControlKind? heldLatch;

        public RepairSequence(BoilerSystem boiler, BoilerSettings settings)
        {
            this.boiler = boiler ?? throw new ArgumentNullException(nameof(boiler));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Refresh();
        }

        public void ResetForFailure() => ResetAttempt(boiler.SoloAssistEnabled ?
            "Hold relief in the green band until the catch secures it, then release and follow the marked controls." :
            "One staff member holds relief. The other opens the panel once pressure is green.");

        public void Refresh()
        {
            if (!boiler.Failed)
            {
                if (Step != RepairStep.Complete)
                {
                    Step = RepairStep.Idle; OperatorActorId = -1;
                    LatchAProgress = LatchBProgress = 0; heldLatch = null;
                    Status = "Heating is running.";
                }
                return;
            }
            if (Step == RepairStep.Idle || Step == RepairStep.Complete) ResetForFailure();
            else if (boiler.SoloAssistEnabled && !boiler.SoloValveLatched && Step != RepairStep.Panel)
                ResetAttempt("Solo catch expired. Secure the relief valve, then reopen the panel.");
            else if (!boiler.InRepairBand && Step != RepairStep.Panel)
                ResetAttempt("Pressure left the green band. Hold relief, then reopen the panel.");
        }

        public CommandResult Press(RepairControlKind control, int actorId)
        {
            Refresh();
            var allowed = ValidateOperator(actorId);
            if (!allowed.Success) return allowed;
            if (control == RepairControlKind.ReliefValve) return Fail("Relief is the other staff member's job.");
            if (control != ExpectedControl())
            {
                ResetAttempt("Wrong order. Reopen panel, isolate breaker, turn latch A, latch B, then restart.");
                return CommandResult.Fail(Status);
            }
            if (OperatorActorId < 0) OperatorActorId = actorId;
            switch (control)
            {
                case RepairControlKind.Panel:
                    Step = RepairStep.Breaker;
                    return Success("Panel open. Isolate the red breaker; keep relief supported.");
                case RepairControlKind.Breaker:
                    Step = RepairStep.LatchA;
                    return Success("Breaker isolated. Turn latch A fully, then latch B.");
                case RepairControlKind.LatchA:
                case RepairControlKind.LatchB:
                    heldLatch = control;
                    return Success(control == RepairControlKind.LatchA ? "Turn latch A until it seats." : "Turn latch B until it seats.");
                case RepairControlKind.Restart:
                    var restart = boiler.Restart(actorId);
                    if (!restart.Success) return Fail(restart.Message);
                    Step = RepairStep.Complete; heldLatch = null;
                    return Success("Heating restarted. Mechanical wear remains; guests' rooms still need time to warm.");
                default:
                    return Fail("Unknown mechanical control.");
            }
        }

        public CommandResult HoldLatch(RepairControlKind control, int actorId, float dt)
        {
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            Refresh();
            if (heldLatch != control) return CommandResult.Fail("Press the required latch to begin turning it.");
            var allowed = ValidateOperator(actorId);
            if (!allowed.Success) return allowed;
            if (control != ExpectedControl()) return CommandResult.Fail("Follow the marked mechanical sequence.");
            if (control == RepairControlKind.LatchA)
            {
                LatchAProgress = Math.Min(1, LatchAProgress + dt / settings.LatchTravelSeconds);
                if (LatchAProgress >= 1)
                { Step = RepairStep.LatchB; heldLatch = null; return Success("Latch A seated. Turn latch B fully."); }
            }
            else if (control == RepairControlKind.LatchB)
            {
                LatchBProgress = Math.Min(1, LatchBProgress + dt / settings.LatchTravelSeconds);
                if (LatchBProgress >= 1)
                { Step = RepairStep.Restart; heldLatch = null; return Success("Both latches seated. Press green restart while relief remains supported."); }
            }
            return CommandResult.Ok(Status);
        }

        public void ReleaseLatch(RepairControlKind control, int actorId)
        {
            if (heldLatch != control || actorId != OperatorActorId) return;
            heldLatch = null;
            if (control == RepairControlKind.LatchA && Step == RepairStep.LatchA) LatchAProgress = 0;
            if (control == RepairControlKind.LatchB && Step == RepairStep.LatchB) LatchBProgress = 0;
            Status = "Unseated latch sprang back. Hold the indicated latch until it is fully turned.";
        }

        public void CancelAttempt()
        {
            if (boiler.Failed) ResetAttempt("Repair interrupted. Hold relief, then reopen the panel.");
            else Refresh();
        }

        private CommandResult ValidateOperator(int actorId)
        {
            if (actorId < 0 || actorId > 1) return Fail("Unknown staff actor.");
            if (!boiler.Failed) return Fail("The boiler is already running.");
            bool soloSupport = boiler.SoloValveLatched && actorId == 0;
            if (!soloSupport && boiler.ReliefActorId < 0) return Fail(boiler.SoloAssistEnabled ?
                "Secure the solo relief catch before operating the panel." : "A staff member must keep holding the relief valve.");
            if (!soloSupport && boiler.ReliefActorId == actorId) return Fail(boiler.SoloAssistEnabled ?
                "Keep pressure green until the mechanical catch engages." : "Two different staff members are required: keep holding relief while your partner works.");
            if (!boiler.InRepairBand) return Fail("Pressure must enter the green repair band before opening the panel.");
            if (OperatorActorId >= 0 && OperatorActorId != actorId)
            {
                ResetAttempt("Repair roles changed. The new operator must reopen the panel.");
                return CommandResult.Fail(Status);
            }
            return CommandResult.Ok();
        }

        private RepairControlKind ExpectedControl()
        {
            switch (Step)
            {
                case RepairStep.Panel: return RepairControlKind.Panel;
                case RepairStep.Breaker: return RepairControlKind.Breaker;
                case RepairStep.LatchA: return RepairControlKind.LatchA;
                case RepairStep.LatchB: return RepairControlKind.LatchB;
                case RepairStep.Restart: return RepairControlKind.Restart;
                default: return RepairControlKind.ReliefValve;
            }
        }

        private void ResetAttempt(string message)
        {
            Step = RepairStep.Panel; OperatorActorId = -1;
            LatchAProgress = LatchBProgress = 0; heldLatch = null; Status = message;
        }

        private CommandResult Success(string message) { Status = message; return CommandResult.Ok(message); }
        private CommandResult Fail(string message) { Status = message; return CommandResult.Fail(message); }
    }
}
