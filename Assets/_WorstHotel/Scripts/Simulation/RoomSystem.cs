using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed class RoomSystem
    {
        private readonly SessionSettings settings;
        public RoomInfrastructureSettings Infrastructure { get; }
        public RoomSystem(SessionSettings settings, RoomInfrastructureSettings infrastructure = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Infrastructure = infrastructure ?? new RoomInfrastructureSettings();
        }

        public float ExtraBoilerDemand(IEnumerable<RoomState> rooms)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            float demand = 0;
            foreach (var room in rooms) demand += (room.RadiatorSetting - 1) * Infrastructure.RadiatorDemandStep;
            return demand;
        }

        public RoomHeatingDemand HeatingDemandForRoom(RoomState room, GuestStay occupant, LivingHotelSettings living)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            if (living == null) throw new ArgumentNullException(nameof(living));
            var agent = occupant?.Agent;
            // A reservation, an old departure body, or a duplicated/stale room reference does
            // not make another consumer. An away owner still heats their assigned room.
            bool ownsRoom = occupant != null && room.Occupied && room.GuestId == occupant.GuestId &&
                occupant.RoomId == room.Profile.Id && !occupant.ReceiptPosted && agent != null && agent.CheckedIn &&
                agent.State != GuestAgentState.CheckingOut && agent.State != GuestAgentState.Leaving && agent.State != GuestAgentState.Left;
            float space = TypicalSpaceHeating(room, ownsRoom ? occupant.Application.Archetype : null, living);
            bool runningShower = ownsRoom && agent.HasReachedRoom && agent.InAssignedRoom &&
                agent.State == GuestAgentState.PerformingActivity && agent.Activity == GuestActivity.Shower && agent.ActivityStaged;
            // Hot water has its own tap: a radiator valve cannot subtract from this room's
            // shower or any other room. Walking to a shower is not running it yet.
            float water = runningShower ? ShowerHotWaterDemand(occupant.Application.Archetype, living) : 0;
            return new RoomHeatingDemand(room.Profile.Id, ownsRoom ? occupant.GuestId : null, space, water);
        }

        // Forecast and live room consumers use exactly the same radiator/loss arithmetic.
        // A null profile explicitly means a vacant room, not a zero-load closed hotel.
        internal float TypicalSpaceHeating(RoomState room, GuestProfile profile, LivingHotelSettings living)
        {
            double spaceBase = profile != null ? (double)profile.HeatingDemand * living.QuietDemandMultiplier : Infrastructure.VacantRadiatorDemand;
            if (spaceBase <= 0 || room.RadiatorSetting <= 0) return 0;
            return (float)Math.Min(float.MaxValue, spaceBase * Infrastructure.DemandMultiplier(room.RadiatorSetting) *
                (1 + Math.Max(0d, room.Profile.HeatLoss) * Infrastructure.HeatLossDemandFactor));
        }

        internal float ShowerHotWaterDemand(GuestProfile profile, LivingHotelSettings living) =>
            (float)Math.Min(float.MaxValue, (double)profile.HeatingDemand * Math.Max(0d, (double)living.ShowerDemandMultiplier - living.QuietDemandMultiplier));

        public void TickInfrastructure(IEnumerable<RoomState> rooms, float dt)
        {
            if (rooms == null || !Number.IsFinite(dt) || dt < 0) throw new ArgumentException("Valid rooms and elapsed time required.");
            foreach (var room in rooms)
            {
                if (room.LampBroken || !room.Occupied || !room.HasPower) continue;
                room.LampCondition = Math.Max(0, room.LampCondition - Infrastructure.LampWearPerSecond * dt);
                if (room.LampCondition <= 0) room.LampBroken = true;
            }
        }

        public void TickTemperature(IEnumerable<RoomState> rooms, float heatingOutput, float dt, Func<int, float> supplementalHeat = null)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (!Number.IsFinite(dt) || dt < 0 || !Number.IsFinite(heatingOutput)) throw new ArgumentOutOfRangeException(nameof(dt));
            float blend = 1 - (float)Math.Exp(-dt / settings.TemperatureTimeConstant);
            foreach (var room in rooms)
            {
                if (room == null || !Number.IsFinite(room.Temperature)) throw new ArgumentException("Thermal state must contain real rooms with finite temperatures.");
                float supplement = supplementalHeat != null ? supplementalHeat(room.Profile.Id) : 0;
                if (!Number.IsFinite(supplement) || supplement < 0) throw new ArgumentException("Supplemental room heat must be finite and nonnegative.");
                float baselineTarget = settings.TemperatureBase + settings.HeatTemperatureGain * Number.Clamp(heatingOutput, 0, 1) *
                    Infrastructure.HeatMultiplier(room.RadiatorSetting) - room.Profile.HeatLoss;
                if (supplement == 0) { room.Temperature += (baselineTarget - room.Temperature) * blend; continue; }
                double target = baselineTarget + (double)supplement;
                double temperature = room.Temperature + (target - room.Temperature) * blend;
                room.Temperature = (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, temperature));
            }
        }
    }
}
