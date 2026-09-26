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
