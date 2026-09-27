using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    [CreateAssetMenu(menuName = "Worst Hotel/Session configuration")]
    public sealed class SessionConfig : ScriptableObject
    {
        public GuestArchetypeDefinition[] guestArchetypes = Array.Empty<GuestArchetypeDefinition>();
        public RoomDefinition[] rooms = Array.Empty<RoomDefinition>();
        public BoilerConfig boiler;
        public EconomyConfig economy;
        public LivingHotelConfig living;
        public NeedConfig needs;
        public NoiseConfig noise;
        public HeaterConfig heater;
        public ElectricityConfig electricity;
        public HousekeepingConfig housekeeping;
        public SoloAssistConfig soloAssist;
        public GuestServiceConfig services;
        public RoomInfrastructureConfig infrastructure;
        [Range(1, 20)] public float tickRate = 5;
        [Min(1)] public float serviceSeconds = 300;
        public int totalDays = 3;
        public int day3BusinessReferencePrice = 525;
        public float temperatureBase = 9;
        public float heatTemperatureGain = 13;
        public float temperatureTimeConstant = 45;
        public float incidentDelaySeconds = 15;
        public float resolutionDelaySeconds = 10;
        public float coldResolutionHysteresis = 0.5f;
        [Min(0)] public float overnightSeconds = 180;

        [Header("Continuous operations (opt-in)")]
        public bool continuousOperations;
        [Min(24)] public float hotelDaySeconds = 720;
        [Range(0, 23.99f)] public float openingHour = 8;
        [Range(0, 23.99f)] public float reportHour = 6;

        [Header("Ordinary room sales")]
        public bool automaticBookings = true;
        [Range(0, 6)] public int initiallyOpenRooms = 4;
        [Min(0)] public int roomSalePrice = 180;
        [Range(0, 1)] public float bookingBaseDemand = .9f;
        [Min(0)] public float bookingPriceElasticity = 1.5f;
        [Range(0, 23.99f)] public float firstDayBookingHour = 8.5f;
        [Range(0, 23.99f)] public float advanceBookingHour = 16;
        [Min(.01f)] public float bookingDecisionSpacingHours = .5f;
        public int bookingSeed = 73129;

        public OperationsSettings OperationsData() => continuousOperations ?
            new OperationsSettings(hotelDaySeconds, openingHour, reportHour, sales: new SalesSettings(automaticBookings,
                initiallyOpenRooms, roomSalePrice, bookingBaseDemand, bookingPriceElasticity, firstDayBookingHour,
                advanceBookingHour, bookingDecisionSpacingHours, bookingSeed)) : null;

        public SessionSettings ToData()
        {
            if (!boiler || !economy || guestArchetypes == null || rooms == null ||
                guestArchetypes.Any(guest => !guest) || rooms.Any(room => !room))
                throw new InvalidOperationException("SessionConfig must reference boiler, economy, all guest archetypes and rooms.");
            return new SessionSettings(guestArchetypes.Select(guest => guest.ToData()), rooms.Select(room => room.ToData()),
                boiler.ToData(), economy.ToData(), tickRate, serviceSeconds, totalDays, day3BusinessReferencePrice,
                temperatureBase, heatTemperatureGain, temperatureTimeConstant, incidentDelaySeconds,
                resolutionDelaySeconds, coldResolutionHysteresis, overnightSeconds);
        }
    }
}
