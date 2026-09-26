using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public sealed class GuestNeedEvaluator
    {
        public NeedSettings Settings { get; }
        public GuestNeedEvaluator(NeedSettings settings) => Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        private NoiseSystem noiseSystem;
        private BoilerSystem boiler;
        public void ConfigureEnvironment(NoiseSystem noise, BoilerSystem heating)
        { noiseSystem = noise; boiler = heating; }

        /// <summary>One accepted response eases only its current needs, without rewriting physical measurements or lifetime history.</summary>
        public void ApplyCompensationRelief(GuestStay stay, IEnumerable<IncidentReason> reasons)
        {
            if (stay != null && stay.IsReplica) return;
            if (stay == null || reasons == null) throw new ArgumentNullException("A guest and current situation reasons are required.");
            var unique = new HashSet<IncidentReason>();
            foreach (var reason in reasons)
            {
                if (reason != IncidentReason.Temperature && reason != IncidentReason.Noise &&
                    reason != IncidentReason.RoomCondition && reason != IncidentReason.Service)
                    throw new ArgumentException("Compensation relief requires living need reasons.", nameof(reasons));
                unique.Add(reason);
            }
            if (unique.Count == 0) return;
            if (stay.Needs == null) throw new InvalidOperationException("A current need snapshot is required before relieving a situation.");
            foreach (var reason in unique)
            {
                float reliefMultiplier = Math.Max(Settings.MinimumRepeatPatienceMultiplier,
                    1 - Math.Max(0, stay.Memory.ComplaintsInCategory(reason) - 1) * Settings.RepeatPatienceReduction);
                switch (reason)
                {
                    case IncidentReason.Temperature: stay.Needs.Temperature = Relieve(stay.Needs.Temperature, reliefMultiplier); break;
                    case IncidentReason.Noise: stay.Needs.Noise = Relieve(stay.Needs.Noise, reliefMultiplier); break;
                    case IncidentReason.RoomCondition: stay.Needs.RoomCondition = Relieve(stay.Needs.RoomCondition, reliefMultiplier); break;
                    case IncidentReason.Service: stay.Needs.Service = Relieve(stay.Needs.Service, reliefMultiplier); break;
                }
            }
        }

        private GuestNeedSnapshot Relieve(GuestNeedSnapshot previous, float multiplier) => new GuestNeedSnapshot(previous.Severity,
            previous.ExposureSeconds, Number.Clamp(previous.Dissatisfaction - Settings.CompensationDissatisfactionReduction * multiplier, 0, 1));

        public float TemperatureSeverity(float temperature, NeedProfile profile)
        {
            if (profile == null || !Number.IsFinite(temperature)) throw new ArgumentException("Temperature and need profile must be valid.");
            if (temperature >= profile.PreferredTemperatureMin && temperature <= profile.PreferredTemperatureMax) return 0;
            bool low = temperature < profile.PreferredTemperatureMin;
            float distance = low ? profile.PreferredTemperatureMin - temperature : temperature - profile.PreferredTemperatureMax;
            float toleranceSpan = low ? profile.PreferredTemperatureMin - profile.ToleranceTemperatureMin :
                profile.ToleranceTemperatureMax - profile.PreferredTemperatureMax;
            if (distance <= toleranceSpan && toleranceSpan > 0) return Settings.TolerableSeverity * distance / toleranceSpan;
            return Number.Clamp(Settings.TolerableSeverity + (distance - toleranceSpan) / Settings.TemperatureSevereDelta *
                (1 - Settings.TolerableSeverity), 0, 1);
        }

        public float NoiseSeverity(float noise, NeedProfile profile)
        {
            if (profile == null || !Number.IsFinite(noise)) throw new ArgumentException("Noise and need profile must be valid.");
            noise = Number.Clamp(noise, 0, 1);
            if (noise <= profile.PreferredNoise) return 0;
            float preferredToTolerance = profile.NoiseTolerance - profile.PreferredNoise;
            if (noise <= profile.NoiseTolerance && preferredToTolerance > 0)
                return Settings.TolerableSeverity * (noise - profile.PreferredNoise) / preferredToTolerance;
            return Number.Clamp(Settings.TolerableSeverity + (noise - profile.NoiseTolerance) /
                Math.Max(0.0001f, 1 - profile.NoiseTolerance) * (1 - Settings.TolerableSeverity), 0, 1);
        }

        public void Tick(GuestStay stay, RoomState room, float dt, bool expiredRoomComplaint = false)
        {
            if (stay != null && stay.IsReplica) return;
            if (stay == null || room == null) throw new ArgumentNullException("A guest and assigned room are required.");
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            // Validate before changing any guest state, including a newly created snapshot.
            if (!Number.IsFinite(room.Temperature) || !Number.IsFinite(room.Noise) ||
                !Number.IsFinite(room.PowerLossConditionSeverity) || room.PowerLossConditionSeverity < 0 || room.PowerLossConditionSeverity > 1)
                throw new ArgumentException("Room needs require finite measurements and normalized power-loss severity.");
            if (stay.Needs == null) stay.Needs = new GuestNeeds();
            if (dt == 0 || stay.Agent == null) return;
            var agent = stay.Agent;
            bool inRoom = agent.InAssignedRoom;
            bool reception = agent.State == GuestAgentState.WaitingForCheckIn;
            UpdatePerception(stay, room, inRoom);
            if (!inRoom && !reception) { ClearInstantaneous(stay); return; }
            var needs = stay.Needs;
            var profile = stay.Application.Archetype.Needs;
            float temperature = inRoom ? TemperatureSeverity(stay.Perception.PerceivedTemperature, profile) : 0;
            float noise = inRoom ? NoiseSeverity(stay.Perception.Noise, profile) : 0;
            float condition = inRoom ? Number.Clamp((room.Cleanliness == Cleanliness.Dirty ? Settings.DirtySeverity : 0) +
                (!room.HasPower ? room.PowerLossConditionSeverity : 0) + (room.LampBroken ? Settings.DegradedSeverity : 0), 0, 1) : 0;
            needs.Temperature = Advance(needs.Temperature, temperature, dt);
            needs.Noise = Advance(needs.Noise, noise, dt, Settings.NoiseBuildupMultiplier);
            needs.RoomCondition = Advance(needs.RoomCondition, condition, dt);
            needs.CombinedRoomDeficit = (float)Math.Min(float.MaxValue,
                (double)temperature * stay.Application.Archetype.ColdPenaltyWeight + noise + condition);

            // The two service causes are mutually exclusive physical states. Never stack them or add a second legacy penalty.
            needs.ExpiredRoomComplaint = inRoom && expiredRoomComplaint;
            float expiredWaiting = reception ? Math.Max(0, agent.WaitingSeconds - agent.WaitingPatience) : 0;
            float service = needs.ExpiredRoomComplaint || expiredWaiting > 0 ? Settings.ServiceExpiredSeverity : 0;
            float serviceStep = reception && expiredWaiting > 0 ? Math.Min(dt, expiredWaiting) : dt;
            needs.Service = Advance(needs.Service, service, serviceStep);
            needs.ServiceIntegral = (float)Math.Min(float.MaxValue, (double)needs.ServiceIntegral + service * (double)serviceStep);
        }

        public void ClearInstantaneous(GuestStay stay)
        {
            if (stay != null && stay.IsReplica) return;
            if (stay == null) throw new ArgumentNullException(nameof(stay));
            var needs = stay.Needs;
            stay.Perception.InAssignedRoom = false;
            stay.Perception.Noise = 0;
            stay.Perception.PerceivedTemperature = 0;
            stay.Perception.NoiseSources = Array.Empty<RoomNoiseSource>();
            stay.Perception.TemperatureCause = null;
            stay.Perception.ConditionCauses = Array.Empty<SituationCause>();
            if (needs == null) return;
            needs.Temperature = ClearSeverity(needs.Temperature);
            needs.Noise = ClearSeverity(needs.Noise);
            needs.RoomCondition = ClearSeverity(needs.RoomCondition);
            needs.Service = ClearSeverity(needs.Service);
            needs.CombinedRoomDeficit = 0;
            needs.ExpiredRoomComplaint = false;
        }

        private void UpdatePerception(GuestStay stay, RoomState room, bool inRoom)
        {
            var perception = stay.Perception;
            perception.InAssignedRoom = inRoom; perception.RoomId = room.Profile.Id;
            perception.Temperature = inRoom ? room.Temperature : 0;
            perception.PerceivedTemperature = inRoom ? (room.Temperature < stay.Application.Archetype.Needs.PreferredTemperatureMin ?
                Math.Min(stay.Application.Archetype.Needs.PreferredTemperatureMin, room.Temperature + stay.BlanketComfortBonus) : room.Temperature) : 0;
            perception.NoiseSources = inRoom && noiseSystem != null ? noiseSystem.GetContributions(room.Profile.Id) : Array.Empty<RoomNoiseSource>();
            double measured = 0;
            foreach (var source in perception.NoiseSources) measured += source.ReceivedNoise;
            perception.Noise = (float)Math.Min(1, measured);
            perception.TemperatureCause = null;
            var conditions = new List<SituationCause>();
            if (inRoom)
            {
                var profile = stay.Application.Archetype.Needs;
                string heating = boiler == null ? "Measured room thermometer" : boiler.Failed ? "Boiler failed" :
                    boiler.Overload > 0 ? "Boiler demand exceeds capacity" : boiler.HeatingOutput < .75f ? "Boiler heating output is reduced" : "Room heat balance";
                string detail = heating + "; room " + room.Profile.Id + ": " + room.Temperature.ToString("F1") + " C; preferred " +
                    profile.PreferredTemperatureMin.ToString("F1") + "-" + profile.PreferredTemperatureMax.ToString("F1") + " C" +
                    (boiler != null ? "; boiler output " + (boiler.HeatingOutput * 100).ToString("F0") + "%, load " + boiler.Load.ToString("F2") : "") +
                    (!room.HasPower ? "; room circuit OFF, portable heater cannot heat" : "") +
                    (stay.BlanketComfortBonus > 0 ? "; extra blanket gives personal cold comfort up to +" + stay.BlanketComfortBonus.ToString("F1") +
                    " C equivalent; perceived " + perception.PerceivedTemperature.ToString("F1") + " C (room thermometer unchanged)" : "") + ".";
                perception.TemperatureCause = new SituationCause("Room temperature", "room/" + room.Profile.Id + "/temperature", null,
                    room.Profile.Id, room.Temperature, perception.PerceivedTemperature, profile.PreferredTemperatureMin, detail);
                if (room.Cleanliness == Cleanliness.Dirty)
                    conditions.Add(new SituationCause("Dirty linen", "room/" + room.Profile.Id + "/linen", null, room.Profile.Id,
                        Settings.DirtySeverity, Settings.DirtySeverity, 0, "The bed has not been changed: dirty linen remains in room " + room.Profile.Id + "."));
                if (!room.HasPower && room.PowerLossConditionSeverity > 0)
                    conditions.Add(new SituationCause("Power loss", "circuit/" + (room.CircuitId ?? room.Profile.Id.ToString()) + "/power", null, room.Profile.Id,
                        room.PowerLossConditionSeverity, room.PowerLossConditionSeverity, 0, "Room lights are off: circuit " + (room.CircuitId ?? "unassigned") + " has no power."));
                if (room.LampBroken)
                    conditions.Add(new SituationCause("Burnt-out lamp", "room/" + room.Profile.Id + "/lamp", null, room.Profile.Id,
                        Settings.DegradedSeverity, Settings.DegradedSeverity, 0, "The bedside lamp in room " + room.Profile.Id + " has a burnt-out bulb."));
            }
            perception.ConditionCauses = conditions.AsReadOnly();
        }

        private static GuestNeedSnapshot ClearSeverity(GuestNeedSnapshot previous) =>
            new GuestNeedSnapshot(0, previous.ExposureSeconds, previous.Dissatisfaction);

        private GuestNeedSnapshot Advance(GuestNeedSnapshot previous, float severity, float dt, float buildupMultiplier = 1)
        {
            bool exposed = severity > Settings.RecoverySeverityThreshold;
            float dissatisfaction = Number.Clamp(previous.Dissatisfaction +
                (exposed ? severity * Settings.BuildupPerSecond * buildupMultiplier : -Settings.RecoveryPerSecond) * dt, 0, 1);
            float exposure = (float)Math.Min(float.MaxValue, (double)previous.ExposureSeconds + (exposed ? dt : 0));
            return new GuestNeedSnapshot(severity, exposure, dissatisfaction);
        }
    }
}
