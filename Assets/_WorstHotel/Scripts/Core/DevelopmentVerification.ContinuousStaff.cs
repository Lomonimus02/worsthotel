#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        // Deliberate model-input adapter: no guest presentation callbacks, no physical carry claim.
        string continuousJob, continuousKeyGuest, continuousCleanId;
        int continuousStaffPhase, continuousKeyRoom, continuousTurnovers, continuousKeyHandoffs, continuousInspections, continuousPatches;
        float continuousDue, continuousLastBedAction, continuousPreviousDoor = 1.3f, continuousRepairHold;
        HousekeepingTask continuousTurnover;
        RepairSequence continuousRepair;
        RepairSequenceController continuousRepairController;
        bool continuousRepairControllerSuspended, continuousUpgradeBought;
        const float ContinuousStaffSpeed = 3.5f, ContinuousStorageZ = 30.65f;
        static float ContinuousDoorZ(int roomId) => 10 + (roomId - 101) / 2 * 7;

        void TickContinuousStaff(float dt)
        {
            float now = continuousModel.Elapsed;
            if (continuousJob == null)
            {
                // One slot: a key, linen bundle or repair can never be issued in parallel.
                if (continuousModel.Boiler.Failed && continuousModel.Economy.Cash >= session.Economy.CheapPatchCost)
                {
                    continuousJob = "repair"; continuousStaffPhase = 0;
                    continuousDue = now + (Math.Abs(35 - continuousPreviousDoor) + 4) / ContinuousStaffSpeed + 1;
                }
                else
                {
                    var waiting = continuousModel.Guests.Where(g => g.Agent.State == GuestAgentState.WaitingForCheckIn)
                        .OrderBy(g => g.Agent.Schedule.ArrivalTime).FirstOrDefault(g => continuousRooms.Any(r =>
                            r.Profile.Id == g.RoomId && r.Cleanliness == Cleanliness.Clean && r.DepartingGuestId == null));
                    if (waiting != null)
                    {
                        continuousJob = "key:" + waiting.GuestId; continuousKeyGuest = waiting.GuestId; continuousKeyRoom = waiting.RoomId;
                        continuousStaffPhase = 0;
                        continuousDue = now + Math.Abs(continuousPreviousDoor - 1.3f) / ContinuousStaffSpeed +
                            continuousModel.LivingSettings.KeyRetrievalEstimateSeconds;
                    }
                    else
                    {
                        continuousTurnover = continuousModel.Housekeeping.Tasks.FirstOrDefault(t => t.Step == RoomPreparationStep.DirtyLinenOnBed &&
                            continuousRooms.Any(r => r.Profile.Id == t.RoomId && !r.Occupied && r.DepartingGuestId == null));
                        if (continuousTurnover != null)
                        {
                            continuousJob = "linen:" + continuousTurnover.RoomId; continuousStaffPhase = 0;
                            continuousDue = now + (Math.Abs(ContinuousDoorZ(continuousTurnover.RoomId) - continuousPreviousDoor) + 5.8f) / ContinuousStaffSpeed + .8f;
                        }
                        else if (!continuousUpgradeBought && !continuousModel.Boiler.CapacityUpgradePurchased &&
                            now < continuousModel.Calendar.At(3, 20) && continuousModel.Economy.Cash >=
                            session.Economy.BoilerUpgradeCost + session.Economy.DailyOperatingCost + session.Economy.CheapPatchCost)
                        {
                            continuousJob = "upgrade"; continuousDue = now + Math.Abs(continuousPreviousDoor - 1.3f) / ContinuousStaffSpeed + 3;
                        }
                    }
                }
            }
            if (continuousJob == null || now < continuousDue) return;
            if (continuousJob == "repair") { TickContinuousRepair(dt); return; }
            if (continuousJob == "upgrade")
            {
                // Recheck funds after the actual travel; an intervening bill cannot be ignored.
                if (continuousModel.Economy.Cash >= session.Economy.BoilerUpgradeCost + session.Economy.DailyOperatingCost + session.Economy.CheapPatchCost)
                {
                    RequireContinuousCommand(continuousModel.PurchaseBoilerUpgrade(0), "earned capacity purchase");
                    continuousUpgradeBought = true;
                    facts.Add("Earned boiler upgrade at=" + now.ToString("F1") + " cashAfter=" + continuousModel.Economy.Cash +
                        "; next operating bill and one emergency patch retained as buffer.");
                }
                continuousPreviousDoor = 1.3f; continuousJob = null; return;
            }
            if (continuousJob.StartsWith("key:", StringComparison.Ordinal))
            {
                var guest = continuousModel.Guests.FirstOrDefault(g => g.GuestId == continuousKeyGuest);
                Require(guest != null && guest.Agent.State == GuestAgentState.WaitingForCheckIn, "timed reception key job still has its waiting guest");
                RequireContinuousCommand(continuousModel.Keys.PickUp(0, continuousKeyRoom), "model rack pickup after timed retrieval");
                RequireContinuousCommand(continuousModel.CheckIn(0, continuousKeyGuest), "model key handoff at actual reception arrival");
                continuousKeyHandoffs++; continuousPreviousDoor = 1.3f; continuousJob = null; return;
            }
            Require(continuousTurnover != null, "one staff linen job owns an actual housekeeping task");
            var room = continuousRooms.Single(r => r.Profile.Id == continuousTurnover.RoomId);
            Require(!room.Occupied && room.DepartingGuestId == null, "manual preparation waits for actual physical departure");
            float storageTrip = (Math.Abs(ContinuousStorageZ - ContinuousDoorZ(room.Profile.Id)) + 9.9f) / ContinuousStaffSpeed + .8f;
            switch (continuousStaffPhase)
            {
                case 0:
                    RequireContinuousCommand(continuousModel.PickUpLinen(0, continuousTurnover.DirtyLinenId), "dirty linen pickup");
                    continuousStaffPhase = 1; continuousDue = now + storageTrip; break;
                case 1:
                    RequireContinuousCommand(continuousModel.DepositDirtyLinen(0, continuousTurnover.DirtyLinenId), "dirty hamper deposit");
                    continuousStaffPhase = 2; continuousDue = now + 1.8f / ContinuousStaffSpeed + .5f; break;
                case 2:
                    var clean = continuousModel.Housekeeping.Linens.FirstOrDefault(i => i.Kind == LinenKind.Clean && i.Location == LinenLocation.OnShelf);
                    if (clean == null) return; // Finite stock: cannot mint a clean bundle.
                    continuousCleanId = clean.Id;
                    RequireContinuousCommand(continuousModel.PickUpLinen(0, clean.Id), "finite clean shelf pickup");
                    continuousStaffPhase = 3; continuousDue = now + storageTrip; break;
                case 3:
                    continuousStaffPhase = 4; continuousDue = now + 3; break;
                case 4:
                    RequireContinuousCommand(continuousModel.SetRadiatorSetting(0, room.Profile.Id, 1), "three-second vacant-room valve inspection");
                    continuousInspections++;
                    RequireContinuousCommand(continuousModel.BeginMakeBed(0, room.Profile.Id, continuousCleanId), "begin manual bed with actual carried clean bundle");
                    continuousStaffPhase = 5; continuousLastBedAction = now; continuousDue = now + .1f; break;
                case 5:
                    float remaining = now - continuousLastBedAction;
                    while (remaining >= .09999f && continuousModel.Housekeeping.Find(room.Profile.Id) != null)
                    {
                        RequireContinuousCommand(continuousModel.AdvanceMakeBed(0, room.Profile.Id, .1f), "explicit model bed-work sample");
                        remaining -= .1f; continuousLastBedAction += .1f;
                    }
                    if (continuousModel.Housekeeping.Find(room.Profile.Id) == null)
                    {
                        continuousTurnovers++; continuousPreviousDoor = ContinuousDoorZ(room.Profile.Id);
                        continuousTurnover = null; continuousCleanId = null; continuousJob = null;
                    }
                    break;
            }
        }

        void TickContinuousRepair(float dt)
        {
            var boiler = continuousModel.Boiler;
            if (!boiler.Failed)
            { RestoreContinuousRepairController(); continuousPreviousDoor = 35; continuousJob = null; return; }
            if (!continuousRepairControllerSuspended)
            {
                // The real input authenticator correctly rejects a non-physical relief hold.
                // This explicitly declared model adapter temporarily owns those model commands.
                continuousRepairController.enabled = false;
                continuousRepairControllerSuspended = true;
                continuousRepair.ResetForFailure(); continuousRepairHold = 0;
                facts.Add("Natural repair model job begins at=" + continuousModel.Elapsed.ToString("F1") + " cash=" + continuousModel.Economy.Cash);
            }
            boiler.AdvanceSoloLatch(dt);
            if (!boiler.SoloValveLatched)
            {
                RequireContinuousCommand(boiler.SetRelief(0, true), "solo model relief hold");
                if (boiler.InRepairBand)
                {
                    // Same declared 1x hotel-time action budget as staff travel/bed work;
                    // this is not a claim about real-time physical input validation.
                    var hold = boiler.HoldSoloValve(0, dt);
                    RequireContinuousCommand(hold, "solo model catch charge");
                    continuousRepairHold += dt;
                }
                if (!boiler.SoloValveLatched) return;
                RequireContinuousCommand(boiler.SetRelief(0, false), "release secured solo valve");
            }
            continuousRepair.Refresh();
            switch (continuousRepair.Step)
            {
                case RepairStep.Panel: RequireContinuousCommand(continuousRepair.Press(RepairControlKind.Panel, 0), "panel"); break;
                case RepairStep.Breaker: RequireContinuousCommand(continuousRepair.Press(RepairControlKind.Breaker, 0), "isolate breaker"); break;
                case RepairStep.LatchA:
                    RequireContinuousCommand(continuousRepair.Press(RepairControlKind.LatchA, 0), "start latch A");
                    RequireContinuousCommand(continuousRepair.HoldLatch(RepairControlKind.LatchA, 0, dt), "seat latch A"); break;
                case RepairStep.LatchB:
                    RequireContinuousCommand(continuousRepair.Press(RepairControlKind.LatchB, 0), "start latch B");
                    RequireContinuousCommand(continuousRepair.HoldLatch(RepairControlKind.LatchB, 0, dt), "seat latch B"); break;
                case RepairStep.Restart:
                    if (continuousModel.Economy.Cash < session.Economy.CheapPatchCost)
                    { RestoreContinuousRepairController(); continuousJob = null; return; }
                    int before = continuousModel.Economy.Cash;
                    RequireContinuousCommand(continuousRepair.Press(RepairControlKind.Restart, 0), "paid emergency patch");
                    Require(before - continuousModel.Economy.Cash == session.Economy.CheapPatchCost && !boiler.Failed,
                        "natural repair debits exactly one production patch cost");
                    continuousPatches++; RestoreContinuousRepairController(); continuousPreviousDoor = 35; continuousJob = null;
                    Debug.Log("VERIFY CONTINUOUS: paid patch=" + continuousPatches + " cash=" + continuousModel.Economy.Cash);
                    break;
            }
        }

        void RestoreContinuousRepairController()
        {
            if (!continuousRepairControllerSuspended) return;
            continuousModel?.Boiler.SetRelief(0, false);
            if (continuousRepairController) continuousRepairController.enabled = true;
            continuousRepairControllerSuspended = false;
        }

        static void RequireContinuousCommand(CommandResult result, string action) => Require(result.Success, action + ": " + result.Message);
    }
}
#endif
