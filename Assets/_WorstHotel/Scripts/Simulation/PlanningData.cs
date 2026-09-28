using System;
using System.Collections.Generic;

namespace WorstHotel
{
    public enum GuestKind { Budget, ColdSensitive, Business }
    [Flags]
    public enum GuestTraits { None = 0, ColdSensitive = 1, NoiseSensitive = 2, Noisy = 4, Patient = 8, Impatient = 16, PriceSensitive = 32 }
    public enum Cleanliness { Clean, Dirty }
    public enum RepairState { Working, Degraded, Broken }
    public enum RoomOccupancyState { Vacant, Occupied, GuestInside, GuestAway, CheckoutPending }
    public enum RoomPrivacyState { Public, SemiPrivate, Private }
    public enum RoomDoorState { Closed, Open, Locked }

    public sealed class GuestProfile
    {
        public GuestKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        public int ReferencePrice { get; }
        public float HeatingDemand { get; }
        public float ColdThreshold { get; }
        public float ColdPenaltyWeight { get; }
        public float PriceSensitivity { get; }
        public float Patience { get; }
        public float NoiseTolerance { get; }
        public GuestTraits Traits { get; }
        public NeedProfile Needs { get; }

        public GuestProfile(GuestKind kind, string label, string description, int referencePrice,
            float heatingDemand, float coldThreshold, float coldPenaltyWeight, float priceSensitivity,
            float patience, float noiseTolerance, GuestTraits? traits = null, NeedProfile needs = null)
        {
            if (referencePrice <= 0 || !Number.IsFinite(heatingDemand) || heatingDemand <= 0 ||
                !Number.IsFinite(coldThreshold) || !Number.IsFinite(coldPenaltyWeight) || coldPenaltyWeight < 0 ||
                !Number.IsFinite(priceSensitivity) || priceSensitivity < 0 || !Number.IsFinite(patience) || patience <= 0 ||
                !Number.IsFinite(noiseTolerance) || noiseTolerance < 0 || noiseTolerance > 1)
                throw new ArgumentException("Guest profile contains invalid price, demand or tolerance.");
            Kind = kind; Label = label; Description = description; ReferencePrice = referencePrice;
            HeatingDemand = heatingDemand; ColdThreshold = coldThreshold; ColdPenaltyWeight = coldPenaltyWeight;
            PriceSensitivity = priceSensitivity; Patience = patience; NoiseTolerance = noiseTolerance;
            Traits = traits ?? (kind == GuestKind.Budget ? GuestTraits.Noisy | GuestTraits.Patient | GuestTraits.PriceSensitive :
                kind == GuestKind.ColdSensitive ? GuestTraits.ColdSensitive : GuestTraits.NoiseSensitive | GuestTraits.Impatient);
            Needs = needs ?? NeedProfile.DefaultFor(coldThreshold, noiseTolerance, patience);
        }
    }

    public sealed class RoomProfile
    {
        public int Id { get; }
        public string Label { get; }
        public float HeatLoss { get; }
        public float Noise { get; }
        public Cleanliness Cleanliness { get; }
        public RepairState RepairState { get; }
        public float Temperature { get; }

        public RoomProfile(int id, string label, float heatLoss = 0, float noise = 0.1f,
            Cleanliness cleanliness = Cleanliness.Clean, RepairState repairState = RepairState.Working, float temperature = 21)
        {
            if (id <= 0 || !Number.IsFinite(heatLoss) || heatLoss < 0 || !Number.IsFinite(noise) || noise < 0 || noise > 1 ||
                !Number.IsFinite(temperature)) throw new ArgumentException("Room profile contains invalid values.");
            Id = id; Label = label; HeatLoss = heatLoss; Noise = noise;
            Cleanliness = cleanliness; RepairState = repairState; Temperature = temperature;
        }
    }

    public sealed class RoomState
    {
        public RoomProfile Profile { get; }
        public bool Operational { get; internal set; } = true;
        public bool WindowInsulated { get; internal set; }
        public float EffectiveHeatLoss => Profile.HeatLoss * (WindowInsulated ? .2f : 1);
        public float Temperature { get; set; }
        public float Noise { get; set; }
        public float SourceNoise { get; internal set; }
        public float ReceivedNoise { get; internal set; }
        public bool HasPower { get; internal set; } = true;
        public string CircuitId { get; internal set; }
        public float PowerLossConditionSeverity { get; internal set; }
        public int RadiatorSetting { get; internal set; } = 1;
        public float LampCondition { get; internal set; } = 100;
        public bool LampBroken { get; internal set; }
        public string DepartingGuestId { get; internal set; }
        public HousekeepingState TurnoverState { get; internal set; }
        public Cleanliness Cleanliness { get; set; }
        public RepairState RepairState { get; set; }
        public string GuestId { get; set; }
        public string ReservedGuestId { get; set; }
        public string AssignedGuest => GuestId;
        public RoomOccupancyState OccupancyState { get; internal set; }
        public RoomPrivacyState PrivacyState { get; internal set; }
        public RoomDoorState DoorState { get; internal set; }
        public bool Occupied => !string.IsNullOrEmpty(GuestId);
        public bool Reserved => !string.IsNullOrEmpty(ReservedGuestId);

        public RoomState(RoomProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            Temperature = profile.Temperature; Noise = profile.Noise;
            Cleanliness = profile.Cleanliness; RepairState = profile.RepairState;
            LampCondition = profile.RepairState == RepairState.Degraded ? 20 : 100;
        }
    }

    public sealed class BookingApplication
    {
        public string Id { get; }
        public string GuestName { get; }
        public GuestProfile Archetype { get; }
        public int ReferencePrice { get; }

        public SpecialGuestKind SpecialKind { get; }
        public SpecialGuestDefinition Special => SpecialGuestDefinition.For(SpecialKind);
        public BookingApplication(string id, string guestName, GuestProfile archetype, int referencePrice, SpecialGuestKind specialKind = SpecialGuestKind.None)
        {
            if (string.IsNullOrWhiteSpace(id) || referencePrice <= 0) throw new ArgumentException("Invalid booking application.");
            Id = id; GuestName = guestName; Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            ReferencePrice = referencePrice; SpecialKind = specialKind;
            if (!Enum.IsDefined(typeof(SpecialGuestKind), specialKind)) throw new ArgumentException("Invalid special guest kind.");
        }
    }

    public sealed class BookingAssignment
    {
        public int RoomId { get; }
        public string BookingId { get; }
        public int Price { get; }
        public int ActorId { get; }
        public BookingAssignment(int roomId, string bookingId, int price, int actorId)
        { RoomId = roomId; BookingId = bookingId; Price = price; ActorId = actorId; }
    }

    public readonly struct CommandResult
    {
        public bool Success { get; }
        public string Message { get; }
        public CommandResult(bool success, string message) { Success = success; Message = message; }
        public static CommandResult Ok(string message = "Accepted") => new CommandResult(true, message);
        public static CommandResult Fail(string message) => new CommandResult(false, message);
    }

    internal static class Number
    {
        internal static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static float Clamp(float value, float minimum, float maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
