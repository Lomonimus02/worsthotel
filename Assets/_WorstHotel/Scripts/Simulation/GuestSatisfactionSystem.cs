using System;

namespace WorstHotel
{
    public sealed class GuestSatisfactionSystem
    {
        private readonly EconomySettings settings;
        public GuestSatisfactionSystem(EconomySettings settings) => this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public float PriceExpectation(int price, int referencePrice)
        {
            if (price <= 0 || referencePrice <= 0) throw new ArgumentOutOfRangeException(nameof(price));
            return Number.Clamp(1 + settings.ExpectationSlope * ((float)price / referencePrice - 1), settings.MinExpectation, settings.MaxExpectation);
        }

        public float QualityDeficit(RoomState room, GuestProfile guest)
        {
            if (room == null || guest == null) throw new ArgumentNullException("Room and guest are required.");
            if (!Number.IsFinite(room.Temperature) || !Number.IsFinite(room.Noise)) throw new ArgumentException("Room quality must contain finite values.");
            float cold = Number.Clamp((guest.ColdThreshold - room.Temperature) / settings.ColdSeverityDegrees, 0, 1);
            float noise = Math.Max(0, Number.Clamp(room.Noise, 0, 1) - guest.NoiseTolerance);
            float fixture = room.RepairState == RepairState.Broken ? settings.BrokenSeverity :
                room.RepairState == RepairState.Degraded ? settings.DegradedSeverity : 0;
            float dirty = room.Cleanliness == Cleanliness.Dirty ? settings.DirtySeverity : 0;
            return cold * guest.ColdPenaltyWeight + noise + fixture + dirty;
        }

        public void Accumulate(GuestStay stay, RoomState room, float dt, bool expiredComplaint = false)
        {
            if (stay != null && stay.IsReplica) return;
            if (stay == null) throw new ArgumentNullException(nameof(stay));
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0) return;
            stay.QualityIntegral += QualityDeficit(room, stay.Application.Archetype) * dt;
            stay.Elapsed += dt;
            if (room.Temperature < stay.Application.Archetype.ColdThreshold) stay.ColdExposureSeconds += dt;
            if (room.Noise > stay.Application.Archetype.NoiseTolerance) stay.NoiseExposureSeconds += dt;
            if (room.Cleanliness == Cleanliness.Dirty) stay.DirtyExposureSeconds += dt;
            if (room.RepairState != RepairState.Working) stay.FixtureExposureSeconds += dt;
            // One boolean per guest prevents several simultaneous symptoms multiplying the patience penalty.
            if (expiredComplaint) stay.ExpiredComplaintSeconds = Math.Min(stay.Elapsed, stay.ExpiredComplaintSeconds + dt);
        }

        public float Evaluate(GuestStay stay)
        {
            if (stay == null) throw new ArgumentNullException(nameof(stay));
            if (stay.Needs != null) return EvaluateLiving(stay);
            float averageDeficit = stay.Elapsed > 0 ? stay.QualityIntegral / stay.Elapsed : 0;
            float expiredFraction = stay.Elapsed > 0 ? Number.Clamp(stay.ExpiredComplaintSeconds / stay.Elapsed, 0, 1) : 0;
            float premium = Math.Max(0, (float)stay.Price / stay.Application.ReferencePrice - 1);
            float score = 100 - settings.QualityPenaltyScale * PriceExpectation(stay.Price, stay.Application.ReferencePrice) * averageDeficit
                - stay.Application.Archetype.PriceSensitivity * premium - settings.PatiencePenalty * expiredFraction
                + (stay.Compensated ? settings.CompensationGoodwill : 0);
            score -= settings.PatiencePenalty * Number.Clamp(stay.CheckInDelayPenaltySeconds /
                Math.Max(1, stay.CheckInWaitingSeconds + stay.Elapsed), 0, 1);
            return Number.Clamp(score, 0, 100);
        }

        public void AccumulateLiving(GuestStay stay, RoomState room, float dt)
        {
            if (stay != null && stay.IsReplica) return;
            if (stay == null || room == null) throw new ArgumentNullException("A guest and room are required.");
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0 || stay.Agent == null || !stay.Agent.InAssignedRoom) return;
            if (stay.Needs == null) throw new InvalidOperationException("Evaluate living guest needs before accumulating satisfaction.");
            if (!Number.IsFinite(room.Temperature) || !Number.IsFinite(room.Noise) || !Number.IsFinite(stay.Needs.CombinedRoomDeficit))
                throw new ArgumentException("Living guest measurements must be finite before satisfaction is accumulated.");
            var profile = stay.Application.Archetype.Needs;
            // Needs own the service integral. This is the only writer of room quality/time, so deficits count once.
            stay.QualityIntegral = AddFinite(stay.QualityIntegral, (double)stay.Needs.CombinedRoomDeficit * dt);
            stay.Elapsed = AddFinite(stay.Elapsed, dt);
            if (stay.Perception.PerceivedTemperature < profile.PreferredTemperatureMin) stay.ColdExposureSeconds = AddFinite(stay.ColdExposureSeconds, dt);
            if (room.Temperature > profile.PreferredTemperatureMax) stay.HotExposureSeconds = AddFinite(stay.HotExposureSeconds, dt);
            if (stay.Perception.Noise > profile.PreferredNoise) stay.NoiseExposureSeconds = AddFinite(stay.NoiseExposureSeconds, dt);
            if (room.Cleanliness == Cleanliness.Dirty) stay.DirtyExposureSeconds = AddFinite(stay.DirtyExposureSeconds, dt);
            // Only the supported physical lamp contributes fixture history; cosmetic wear stays irrelevant.
            if (room.LampBroken) stay.FixtureExposureSeconds = AddFinite(stay.FixtureExposureSeconds, dt);
            if (!room.HasPower) stay.PowerLossExposureSeconds = AddFinite(stay.PowerLossExposureSeconds, dt);
            if (stay.Needs.ExpiredRoomComplaint) stay.ExpiredComplaintSeconds = Math.Min(stay.Elapsed, stay.ExpiredComplaintSeconds + dt);
        }

        private float EvaluateLiving(GuestStay stay)
        {
            float averageDeficit = stay.Elapsed > 0 ? stay.QualityIntegral / stay.Elapsed : 0;
            float serviceFraction = Number.Clamp(stay.Needs.ServiceIntegral / Math.Max(1, stay.Elapsed + stay.CheckInWaitingSeconds), 0, 1);
            float premium = Math.Max(0, (float)stay.Price / stay.Application.ReferencePrice - 1);
            float score = 100 - settings.QualityPenaltyScale * PriceExpectation(stay.Price, stay.Application.ReferencePrice) * averageDeficit
                - stay.Application.Archetype.PriceSensitivity * premium - settings.PatiencePenalty * serviceFraction
                + (stay.Compensated ? settings.CompensationGoodwill : 0) + stay.ServiceSatisfactionAdjustment;
            return Number.Clamp(score, 0, 100);
        }
        private static float AddFinite(float current, double addition) => (float)Math.Min(float.MaxValue, current + addition);
    }
}
