using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public IReadOnlyList<GuestStay> Guests => guests.AsReadOnly();
        public bool Running { get; private set; }
        public HotelGameClock Clock { get; } = new HotelGameClock();
        public float Elapsed => Clock.SimulationTime;
        public int EventRevision { get; private set; }
        public string LastEvent { get; private set; } = "Hotel ready";
        public void SignalEvent(string description) {
            if (IsReadOnlyMirror) return; LastEvent = description; EventRevision++; }
        public float Remaining => Math.Max(0, (ContinuousOperations ? NextReportAt : settings.ServiceSeconds) - Elapsed);
        public bool IsServiceComplete => !ContinuousOperations && Running && Remaining <= 0;
        public EconomySystem Economy { get; }
        public GuestSatisfactionSystem Satisfaction { get; }
        public BoilerSystem Boiler { get; }
        public IncidentSystem Incidents { get; }
        public RequestSystem Requests { get; }
        public LivingHotelSettings LivingSettings { get; }
        public NeedSettings NeedsSettings { get; }
        public GuestNeedEvaluator NeedEvaluator { get; }
        public NoiseSettings NoiseSettings { get; }
        public HeaterSettings HeaterSettings { get; }
        public HeaterSystem Heaters { get; }
        public ElectricitySettings ElectricitySettings { get; }
        public ElectricalSystem Electrical { get; }
        public HousekeepingSettings HousekeepingSettings { get; }
        public HousekeepingSystem Housekeeping { get; }
        public NoiseSystem Noise { get; }
        public bool LivingEnabled => LivingSettings != null;
        public GuestScheduleSystem Schedules { get; }
        public int OutstandingCompensation => Running ? guests.Sum(guest => guest.CompensationCredit) : 0;
        public DayReport LastReport { get; private set; }
        public IReadOnlyList<DayReport> DayReports => reports.AsReadOnly();
        public IReadOnlyList<MaintenanceDecision> MaintenanceDecisions => maintenance.AsReadOnly();
        public bool MaintenanceRequired => !ContinuousOperations && !Running && LastReport != null && dayNumber < settings.TotalDays && lastMaintenanceDay < dayNumber;
        private readonly SessionSettings settings;
        private readonly Dictionary<int, RoomState> rooms;
        private readonly List<GuestStay> guests = new List<GuestStay>();
        private readonly RoomSystem roomSystem;
        private readonly Func<int, float> supplementalHeat;
        private readonly List<DayReport> reports = new List<DayReport>();
        private readonly List<MaintenanceDecision> maintenance = new List<MaintenanceDecision>();
        private int dayNumber;
        private int lastMaintenanceDay;
        private int debugGuestCounter;

        public HotelSimulation(SessionSettings settings, RoomState[] roomStates, LivingHotelSettings living = null, NeedSettings needs = null,
            NoiseSettings noise = null, HeaterSettings heater = null, ElectricitySettings electricity = null,
            HousekeepingSettings housekeeping = null, GuestServiceSettings services = null, RoomInfrastructureSettings infrastructure = null,
            OperationsSettings operations = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Operations = operations;
            if (operations != null) Calendar = new HotelCalendar(Clock, operations);
            LivingSettings = living;
            if (living != null) Schedules = new GuestScheduleSystem(living);
            NeedsSettings = living != null ? needs ?? new NeedSettings() : null;
            if (NeedsSettings != null) NeedEvaluator = new GuestNeedEvaluator(NeedsSettings);
            NoiseSettings = living != null ? noise ?? new NoiseSettings() : null;
            rooms = (roomStates ?? throw new ArgumentNullException(nameof(roomStates))).ToDictionary(room => room.Profile.Id);
            InitializeRoomKeys();
            HousekeepingSettings = living != null ? housekeeping ?? new HousekeepingSettings() : null;
            if (HousekeepingSettings != null)
            {
                Housekeeping = new HousekeepingSystem(HousekeepingSettings, rooms.Values);
                Housekeeping.Changed += (task, reason) => SignalEvent("Room " + task.RoomId + ": " + reason);
                Housekeeping.LinenChanged += (linen, reason) => SignalEvent("Linen " + linen.Id + ": " + reason);
            }
            HeaterSettings = heater ?? new HeaterSettings();
            Heaters = new HeaterSystem(HeaterSettings, rooms.Keys);
            supplementalHeat = Heaters.HeatForRoom;
            if (NoiseSettings != null) Noise = new NoiseSystem(NoiseSettings, RoomAdjacencyGraph.Prototype(rooms.Keys));
            ElectricitySettings = living != null ? electricity ?? new ElectricitySettings() : null;
            if (ElectricitySettings != null)
            {
                Electrical = new ElectricalSystem(ElectricitySettings, rooms.Keys);
                Electrical.Changed += (circuit, reason) => SignalEvent("Circuit " + circuit.Id + ": " + reason);
                RefreshElectrical();
            }
            Economy = new EconomySystem(settings.Economy);
            Satisfaction = new GuestSatisfactionSystem(settings.Economy);
            Boiler = new BoilerSystem(settings.Boiler);
            NeedEvaluator?.ConfigureEnvironment(Noise, Boiler);
            Incidents = new IncidentSystem(settings, NeedsSettings);
            Incidents.OnSituationChanged += incident =>
            {
                if (incident.HasContactedStaff && incident.Stage != SituationStage.Observed)
                    SignalEvent("Room " + incident.RoomId + ": " + incident.Stage);
            };
            Requests = new RequestSystem(Incidents);
            if (!LivingEnabled)
            {
                Requests.OnRequestCreated += request => SignalEvent("Room " + request.RoomId + ": " + request.Reason);
                Requests.OnRequestResolved += request => SignalEvent("Room " + request.RoomId + ": request closed");
            }
            Boiler.OnFailureStarted += () => SignalEvent("Boiler overpressure failure");
            Boiler.OnFailureResolved += () =>
            {
                foreach (var guest in guests) Services?.RecordStaffAction(guest.GuestId, IncidentReason.Temperature);
                SignalEvent("Boiler restarted");
            };
            InitializeDecisionResponses();
            roomSystem = new RoomSystem(settings, infrastructure);
            InitializeServices(services);
        }

        public CommandResult StartShift(IEnumerable<BookingAssignment> assignments, IEnumerable<BookingApplication> applications)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (ContinuousOperations) return CommandResult.Fail("The hotel uses continuous bookings; there is no Start Day.");
            if (Running) return CommandResult.Fail("A shift is already running.");
            if (dayNumber >= settings.TotalDays) return CommandResult.Fail("The three-day stay is complete.");
            if (dayNumber > 0 && lastMaintenanceDay < dayNumber) return CommandResult.Fail("Choose maintenance or explicitly defer it before the next day.");
            if (assignments == null || applications == null) return CommandResult.Fail("A committed plan is required.");
            var bookings = assignments.ToArray();
            if (bookings.Length == 0 || bookings.Any(booking => booking == null)) return CommandResult.Fail("Accept at least one valid booking.");
            var offers = applications.ToArray();
            if (offers.Any(offer => offer == null) || offers.Select(offer => offer.Id).Distinct().Count() != offers.Length)
                return CommandResult.Fail("Applications must be uniquely identified.");
            var offerLookup = offers.ToDictionary(offer => offer.Id);
            if (bookings.Select(booking => booking.RoomId).Distinct().Count() != bookings.Length ||
                bookings.Select(booking => booking.BookingId).Distinct().Count() != bookings.Length)
                return CommandResult.Fail("Every accepted guest needs a separate room.");
            foreach (var booking in bookings)
            {
                if (booking.ActorId < 0 || booking.ActorId > 1 || booking.BookingId == null || !offerLookup.ContainsKey(booking.BookingId) ||
                    !rooms.TryGetValue(booking.RoomId, out var room)) return CommandResult.Fail("The committed plan contains an invalid actor, room or guest.");
                if (room.Occupied && (LivingEnabled || room.GuestId != booking.BookingId)) return CommandResult.Fail("An accepted room is already occupied.");
                if (room.Reserved && room.ReservedGuestId != booking.BookingId) return CommandResult.Fail("An accepted room is already reserved.");
                if (booking.Price < settings.Economy.MinPrice || booking.Price > settings.Economy.MaxPrice ||
                    (booking.Price - settings.Economy.MinPrice) % settings.Economy.PriceStep != 0)
                    return CommandResult.Fail("The committed plan contains an invalid room price.");
            }
            guests.Clear();
            foreach (var booking in bookings)
            {
                guests.Add(new GuestStay(offerLookup[booking.BookingId], booking.RoomId, booking.Price));
                if (LivingEnabled) rooms[booking.RoomId].ReservedGuestId = booking.BookingId;
                else rooms[booking.RoomId].GuestId = booking.BookingId;
            }
            dayNumber++;
            if (LivingEnabled) Schedules.StartDay(dayNumber, guests, settings.ServiceSeconds);
            Incidents.Clear(); Requests.Clear();
            RefreshGuestLoad();
            Boiler.BeginService();
            Clock.Reset(); LastReport = null; Running = true;
            Services?.StartDay(dayNumber, settings.ServiceSeconds);
            RefreshRoomPresence();
            BoilerFailureAcknowledged = false;
            if (LivingEnabled) { Noise.ClearOverrides(); RefreshElectrical(); }
            SignalEvent("Shift started");
            return CommandResult.Ok(LivingEnabled ? "Rooms reserved. Guests will arrive during the shift." : "Guests checked in. The shift has started.");
        }

        public void Tick(float dt)
        {
            if (IsReadOnlyMirror) return;
            if (!Number.IsFinite(dt) || dt < 0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (!Running || dt == 0) return;
            if (ContinuousOperations) { TickOperations(dt); return; }
            if (Remaining <= 0) return;
            TickStep(Math.Min(dt, Remaining));
        }

        private void TickStep(float step)
        {
            if (LivingEnabled) TickLivingGuests(Elapsed + step, step);
            if (LivingEnabled)
            {
                Housekeeping.Tick(step);
                UpdateQuietRequests(Elapsed + step);
                Electrical.Tick(guests, rooms.Values, Heaters, step);
                Noise.Tick(guests, rooms.Values, Elapsed + step);
            }
            RefreshGuestLoad();
            bool wasWarning = Boiler.Pressure >= settings.Boiler.WarningPressure;
            Boiler.Tick(step);
            if (!wasWarning && Boiler.Pressure >= settings.Boiler.WarningPressure)
                SignalEvent("Boiler pressure warning");
            roomSystem.TickTemperature(rooms.Values, Boiler.HeatingOutput, step, supplementalHeat);
            if (LivingEnabled)
            {
                roomSystem.TickInfrastructure(rooms.Values, step);
                foreach (var guest in guests)
                    NeedEvaluator.Tick(guest, rooms[guest.RoomId], step, Requests.HasExpiredRoomRequest(guest.GuestId));
                Incidents.TickLiving(guests, rooms.Values, step);
                Requests.Tick();
                Services?.Tick(Elapsed + step, step);
                foreach (var guest in guests) Satisfaction.AccumulateLiving(guest, rooms[guest.RoomId], step);
            }
            else
            {
                Incidents.Tick(guests, rooms.Values, step);
                Requests.Tick();
                foreach (var guest in guests) Satisfaction.Accumulate(guest, rooms[guest.RoomId], step, Requests.HasExpiredRequest(guest.GuestId));
            }
            Clock.Advance(step);
            if (IsServiceComplete) SignalEvent("Shift complete");
        }

        public CommandResult OfferCompensation(string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Running) return CommandResult.Fail("Compensation can only be promised during a guest's stay.");
            var guest = guests.FirstOrDefault(stay => stay.GuestId == guestId);
            if (LivingEnabled && (guest == null || !guest.Agent.CheckedIn || !guest.Agent.InAssignedRoom))
                return CommandResult.Fail("Compensation is available while a checked-in guest is staying in their room.");
            var currentReasons = LivingEnabled ? Incidents.Items.Where(incident => incident.GuestId == guestId && incident.Active &&
                    (Services?.Settings.NaturalCommunicationEnabled != true || incident.HasContactedStaff))
                .Select(incident => incident.Reason).ToArray() : Array.Empty<IncidentReason>();
            if (LivingEnabled && currentReasons.Length == 0)
                return CommandResult.Fail("This guest has no current situation to compensate.");
            var result = Economy.ReserveCompensation(guest);
            if (result.Success)
            {
                Requests.SetCompensated(guestId, true);
                if (LivingEnabled)
                {
                    NeedEvaluator.ApplyCompensationRelief(guest, currentReasons);
                    Incidents.AcceptCompensationResponse(guest);
                    SignalEvent(guest.Name + " accepted a compensation credit and temporary relief; unchanged causes can return");
                }
            }
            return result;
        }

        public CommandResult SetRoomTemperature(int roomId, float temperature)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Number.IsFinite(temperature)) return CommandResult.Fail("Temperature must be finite.");
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Room not found.");
            room.Temperature = temperature;
            return CommandResult.Ok("Room " + roomId + " temperature changed by developer override.");
        }

        public CommandResult SetRoomNoise(int roomId, float noise)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Number.IsFinite(noise) || noise < 0 || noise > 1) return CommandResult.Fail("Noise must be between zero and one.");
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Room not found.");
            if (LivingEnabled)
            {
                var result = Noise.SetNoiseOverride(roomId, noise);
                if (result.Success) RefreshElectrical();
                return result;
            }
            room.Noise = noise;
            return CommandResult.Ok("Room " + roomId + " noise changed by developer override.");
        }

        public CommandResult ApplyMaintenance(int actorId, MaintenanceChoice choice)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (!Enum.IsDefined(typeof(MaintenanceChoice), choice)) return CommandResult.Fail("Unknown maintenance choice.");
            if (!MaintenanceRequired) return CommandResult.Fail("Maintenance is available once after day one and day two settle.");
            int cost = choice == MaintenanceChoice.CheapPatch ? settings.Economy.CheapPatchCost :
                choice == MaintenanceChoice.ProperRepair ? settings.Economy.ProperRepairCost : 0;
            if (choice != MaintenanceChoice.Defer)
            {
                var purchase = Economy.TrySpend(cost);
                if (!purchase.Success) return purchase;
            }
            float previousCondition = Boiler.Condition;
            if (choice == MaintenanceChoice.CheapPatch)
                Boiler.ApplyPaidMaintenance(Math.Min(100, Boiler.Condition + settings.Economy.CheapPatchCondition));
            else if (choice == MaintenanceChoice.ProperRepair)
                Boiler.ApplyPaidMaintenance(Math.Max(Boiler.Condition, settings.Economy.ProperRepairCondition));
            lastMaintenanceDay = dayNumber;
            Boiler.SetLoad(0);
            // Empty rooms relax toward the same heat model overnight. Paid maintenance does not alter noise or fixtures.
            // A deferred failed boiler still has failed output, so the hotel remains cold for the next booking decision.
            roomSystem.TickTemperature(rooms.Values, Boiler.HeatingOutput, settings.OvernightSeconds);
            maintenance.Add(new MaintenanceDecision(dayNumber, actorId, choice, cost, previousCondition, Boiler.Condition, Economy.Cash));
            // Replenish consumed slots before next-day preparation, even when all rooms still need linen.
            // StartShift never refills: held/dropped bundles and unfinished turnover survive the clock reset.
            Housekeeping?.RefillForDay(dayNumber + 1);
            Services?.RefillForDay(dayNumber + 1);
            return CommandResult.Ok(choice == MaintenanceChoice.Defer ? "Maintenance deferred. Wear and any failed boiler carry into tomorrow." :
                choice == MaintenanceChoice.CheapPatch ? "Cheap patch complete. Limited condition restored; tomorrow's load still matters." :
                "Proper repair complete. Heating restored overnight; prepare used beds with fresh linen before handing out room keys.");
        }

        public CommandResult DebugSetCash(float amount) => Economy.DebugSetCash(amount);

        public CommandResult DebugSpawnGuest(GuestKind kind, int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Running) return CommandResult.Fail("Developer check-in requires an active service.");
            if (!rooms.TryGetValue(roomId, out var room) || room.Occupied || room.Reserved) return CommandResult.Fail("Choose a real unreserved vacant room.");
            if (LivingEnabled && room.Cleanliness != Cleanliness.Clean) return CommandResult.Fail("A walk-in needs a clean room.");
            if (LivingEnabled && RoomTurnoverProtected(room)) return CommandResult.Fail("The previous guest must leave and any bed-making interaction must finish before a walk-in is assigned.");
            if (LivingEnabled && Elapsed >= settings.ServiceSeconds * LivingSettings.CheckoutFraction)
                return CommandResult.Fail("There is no time left for another arrival today.");
            var profile = settings.GuestArchetypes.FirstOrDefault(guest => guest.Kind == kind);
            if (profile == null) return CommandResult.Fail("Unknown guest archetype.");
            int reference = dayNumber == 3 && kind == GuestKind.Business ? settings.Day3BusinessReferencePrice : profile.ReferencePrice;
            int step = settings.Economy.PriceStep;
            int gridMaximum = settings.Economy.MinPrice + (settings.Economy.MaxPrice - settings.Economy.MinPrice) / step * step;
            int price = settings.Economy.MinPrice + (int)Math.Round((double)(reference - settings.Economy.MinPrice) / step, MidpointRounding.AwayFromZero) * step;
            price = Math.Max(settings.Economy.MinPrice, Math.Min(gridMaximum, price));
            string id = "day" + dayNumber + "-debug" + (++debugGuestCounter);
            var offer = new BookingApplication(id, profile.Label + " walk-in", profile, reference);
            var stay = new GuestStay(offer, roomId, price);
            guests.Add(stay);
            if (LivingEnabled)
            {
                room.ReservedGuestId = id;
                Schedules.AddWalkIn(stay, dayNumber, Elapsed, settings.ServiceSeconds);
                SignalEvent(stay.Name + " is arriving at reception");
            }
            else room.GuestId = id;
            RefreshGuestLoad();
            return CommandResult.Ok(LivingEnabled ? "Walk-in booked: the guest will approach reception for physical check-in." :
                "Developer check-in: a real " + profile.Label + " now occupies room " + roomId + " and adds heating demand.");
        }

        public DayReport EndShift()
        {
            if (IsReadOnlyMirror) throw new InvalidOperationException(MirrorMessage);
            if (ContinuousOperations) throw new InvalidOperationException("Daily reports are automatic and do not close the hotel.");
            if (!Running)
            {
                if (LastReport != null) return LastReport;
                throw new InvalidOperationException("There is no running shift to settle.");
            }
            if (LivingEnabled)
                foreach (var guest in guests) Incidents.RecordIgnored(guest);
            Services?.SettleShift();
            var receipts = guests.Select(guest => LivingEnabled && (!guest.Agent.HasReachedRoom || guest.Elapsed <= 0) ?
                new GuestReceipt(guest.GuestId, guest.Name, guest.RoomId, 0, guest.Agent.WaitingSeconds > 0 || guest.Agent.CheckedIn ? 0 : 75, 0,
                    guest.Agent.CheckedIn ? "Check-in started but I received no room time. No stay was charged." :
                    guest.Agent.WaitingSeconds > 0 ? "I reached reception but never received my room. No stay was charged." :
                    "I did not check in. No stay was charged and I cannot judge the room.") :
                Economy.CalculateReceipt(guest, Satisfaction.Evaluate(guest))).ToArray();
            LastReport = Economy.Settle(dayNumber, receipts, Elapsed);
            reports.Add(LastReport);
            Running = false;
            // Staff close portable devices for the night; maintenance's overnight model has no unattended electric heating.
            Heaters.SwitchAllOff();
            Clock.SetSpeed(1);
            SignalEvent("Daily accounts settled");
            if (Boiler.ReliefActorId >= 0) Boiler.SetRelief(Boiler.ReliefActorId, false);
            Boiler.SetLoad(0);
            foreach (var guest in guests)
            {
                bool alreadyOutside = LivingEnabled && guest.Agent.State == GuestAgentState.GuestAway;
                ReleaseRoom(guest);
                if (alreadyOutside) SignalGuestVacatedRoom(guest.GuestId, guest.RoomId);
                if (LivingEnabled)
                {
                    // Already visible guests finish their exit route after settlement using normal presentation time.
                    guest.Agent.State = guest.Agent.State == GuestAgentState.Scheduled || guest.Agent.State == GuestAgentState.Left || guest.Agent.State == GuestAgentState.GuestAway ?
                        GuestAgentState.Left : GuestAgentState.Leaving;
                    guest.Agent.StateChangedAt = Elapsed;
                    guest.Agent.HeatingDemandMultiplier = guest.Agent.NoiseOutput = 0;
                }
            }
            if (LivingEnabled) RefreshElectrical();
            return LastReport;
        }

    }
}

