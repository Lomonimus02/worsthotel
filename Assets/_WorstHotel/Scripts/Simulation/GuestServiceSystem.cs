using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    /// <summary>Finite, state-driven requests. Serious incidents continue to own escalation and world causes.</summary>
    public sealed partial class GuestServiceSystem
    {
        public GuestServiceSettings Settings { get; }
        public IReadOnlyList<ServiceCase> Cases => cases.AsReadOnly();
        public IReadOnlyList<PromiseWakeUp> Promises => promises.AsReadOnly();
        public IReadOnlyList<ServiceItemState> Items => items.AsReadOnly();
        public int BlanketsAvailable => items.Count(item => item.Kind == ServiceItemKind.Blanket && item.Location == ServiceItemLocation.OnShelf);
        public int BulbsAvailable => items.Count(item => item.Kind == ServiceItemKind.ReplacementBulb && item.Location == ServiceItemLocation.OnShelf);
        public int StaffCount { get; private set; } = 1;
        public int LastRefillDay { get; internal set; } = 1;
        public event Action<ServiceCase> Changed;
        public event Action<ServiceItemState> ItemChanged;
        internal readonly List<ServiceCase> cases = new List<ServiceCase>();
        internal readonly List<PromiseWakeUp> promises = new List<PromiseWakeUp>();
        internal readonly List<ServiceItemState> items = new List<ServiceItemState>();
        readonly HotelSimulation simulation;
        readonly Dictionary<int, RoomState> rooms;
        int day;
        float serviceEnd;
        int CaseCapacity => simulation.ContinuousOperations ? 256 : 32;
        int ResponseCapacity => simulation.ContinuousOperations ? 512 : 288;

        internal GuestServiceSystem(GuestServiceSettings settings, HotelSimulation simulation, IEnumerable<RoomState> rooms)
        { Settings = settings ?? throw new ArgumentNullException(nameof(settings)); this.simulation = simulation;
            this.rooms = rooms.ToDictionary(room => room.Profile.Id);
            AddStock(ServiceItemKind.Blanket, "blanket:", settings.BlanketStock);
            AddStock(ServiceItemKind.ReplacementBulb, "bulb:", settings.BulbStock);
            simulation.Incidents.RequirePhysicalCommunication = settings.NaturalCommunicationEnabled; }
        public ServiceCase FindCase(string id) => cases.FirstOrDefault(item => item.Id == id);
        public PromiseWakeUp FindPromise(string id) => promises.FirstOrDefault(item => item.Id == id);
        public ServiceItemState FindItem(string id) => items.FirstOrDefault(item => item.Id == id);
        public ServiceItemState HeldBy(int actor) => items.FirstOrDefault(item => item.PlayerId == actor && item.Location == ServiceItemLocation.HeldByPlayer);
        public void SetStaffCount(int count)
        { if (simulation.IsReadOnlyMirror) return; if (count < 1 || count > 2) throw new ArgumentOutOfRangeException(nameof(count)); StaffCount = count; }

        internal void StartDay(int dayNumber, float end)
        {
            if (simulation.IsReadOnlyMirror) return;
            day = dayNumber; serviceEnd = end; cases.Clear(); promises.Clear(); responses.Clear(); intents.Clear();
            items.RemoveAll(item => item.Kind == ServiceItemKind.Luggage);
            RefillForDay(dayNumber);
            foreach (var guest in simulation.Guests) EnsureLuggage(guest);
        }

        /// <summary>Advance the service allowance calendar without ending a stay or an agreement.</summary>
        public void BeginOperatingDay(int dayNumber, float end)
        {
            if (simulation.IsReadOnlyMirror) return;
            if (dayNumber < 1 || dayNumber < day || !Number.IsFinite(end) || end < 0)
                throw new ArgumentException("Operating services need a nondecreasing calendar date and a finite horizon.");
            day = dayNumber;
            serviceEnd = Math.Max(serviceEnd, end);
            RefillForDay(dayNumber);
            foreach (var guest in simulation.Guests)
                if (!Departed(guest)) EnsureLuggage(guest);
        }

        internal void RefillForDay(int dayNumber)
        {
            if (simulation.IsReadOnlyMirror) return;
            if (dayNumber < 1) throw new ArgumentOutOfRangeException(nameof(dayNumber));
            if (dayNumber <= LastRefillDay) return;
            RefillStock(ServiceItemKind.Blanket, "blanket:", Settings.BlanketStock);
            RefillStock(ServiceItemKind.ReplacementBulb, "bulb:", Settings.BulbStock);
            LastRefillDay = dayNumber;
        }

        void AddStock(ServiceItemKind kind, string prefix, int count)
        { for (int i = 0; i < count; i++) items.Add(new ServiceItemState(prefix + i, kind, day)); }
        void RefillStock(ServiceItemKind kind, string prefix, int count)
        {
            for (int index = 0; index < 6; index++)
            {
                var item = FindItem(prefix + index);
                if (item == null && index < count) { items.Add(new ServiceItemState(prefix + index, kind, day)); continue; }
                if (item == null || item.Location == ServiceItemLocation.HeldByPlayer || item.Location == ServiceItemLocation.Dropped ||
                    item.Location == ServiceItemLocation.AwaitingReceipt) continue;
                // A dated stay can cross a replenishment boundary. Its delivered blanket
                // remains the same physical item until that stay actually ends.
                if (simulation.ContinuousOperations && item.GuestId != null &&
                    Guest(item.GuestId) is GuestStay owner && !Departed(owner)) continue;
                bool stocked = index < count;
                if (simulation.ContinuousOperations && item.Location == (stocked ? ServiceItemLocation.OnShelf : ServiceItemLocation.Delivered) &&
                    item.GuestId == null && item.RoomId == null && item.PlayerId == null) continue;
                item.Location = index < count ? ServiceItemLocation.OnShelf : ServiceItemLocation.Delivered;
                item.GuestId = null; item.RoomId = null; item.PlayerId = null; item.LastPlayerId = null; item.Generation++;
            }
        }
        void EnsureLuggage(GuestStay guest)
        {
            if (simulation.ContinuousOperations && Departed(guest)) return;
            if (FindItem("luggage:" + guest.GuestId) == null)
                items.Add(new ServiceItemState("luggage:" + guest.GuestId, ServiceItemKind.Luggage, day, guest.GuestId));
        }
        GuestStay Guest(string id) => simulation.Guests.FirstOrDefault(guest => guest.GuestId == id);
        bool Departed(GuestStay guest) => guest.Agent == null || guest.Agent.State == GuestAgentState.CheckingOut ||
            guest.Agent.State == GuestAgentState.Leaving || guest.Agent.State == GuestAgentState.Left;

        internal void SyncGuestRoom(GuestStay guest)
        {
            if (simulation.IsReadOnlyMirror) return;
            foreach (var intent in intents.Where(item => item.GuestId == guest.GuestId && item.Active && item.RoomId != guest.RoomId).ToArray())
                if (intent.Kind == ServiceIntentKind.DropOff)
                {
                    CloseIntent(intent, ServiceIntentStatus.Cancelled, "Guest changed rooms");
                    Finish(FindCase(intent.CaseId), guest, ServiceStatus.Expired, 0);
                }
                else { intent.RoomId = guest.RoomId; intent.Revision++; }
            SyncResponseRoom(guest);
            foreach (var request in cases.Where(item => item.GuestId == guest.GuestId))
            {
                // A physical room move is a response to an existing environmental request.
                // Its original causal identity remains intact until measured recovery closes it.
                if (!NaturalCommunicationEnabled && request.RoomId != guest.RoomId && request.Status == ServiceStatus.Requested &&
                    (request.Kind == ServiceKind.ExtraBlanket || request.Kind == ServiceKind.AskNeighborsQuiet))
                    request.Status = ServiceStatus.Acknowledged;
                request.RoomId = guest.RoomId;
            }
            foreach (var promise in promises.Where(item => item.GuestId == guest.GuestId)) promise.RoomId = guest.RoomId;
            foreach (var blanket in items.Where(item => item.Kind == ServiceItemKind.Blanket &&
                item.Location == ServiceItemLocation.Delivered && item.GuestId == guest.GuestId)) blanket.RoomId = guest.RoomId;
        }

        internal void Tick(float now, float dt)
        {
            if (simulation.IsReadOnlyMirror) return;
            TickIntents(now);
            if (NaturalCommunicationEnabled) { TickNaturalResponses(now, dt); return; }
            TickPromises(now);
            foreach (var guest in simulation.Guests.OrderBy(guest => guest.GuestId, StringComparer.Ordinal))
            {
                EnsureLuggage(guest);
                SyncGuestRoom(guest);
                if (Departed(guest)) { EndGuestStay(guest); continue; }
                foreach (var item in cases.Where(item => item.GuestId == guest.GuestId && item.Active).ToArray())
                    UpdateCase(item, guest, now, dt);
                if (!BudgetAvailable(guest) ||
                    cases.Any(item => item.GuestId == guest.GuestId && item.Active)) continue;
                // Environmental needs take priority over a discretionary schedule preference.
                foreach (ServiceKind kind in new[] { ServiceKind.ExtraBlanket, ServiceKind.AskNeighborsQuiet,
                    ServiceKind.LuggageStorage, ServiceKind.WakeUpCall, ServiceKind.LateCheckout })
                {
                    if (!Eligible(guest, kind) || !TryCause(guest, kind, now, out string source, out int sourceRoom, out float due, out string reason)) continue;
                    if (TryCreate(guest, kind, now, due, source, sourceRoom, reason) != null) break;
                }
            }
        }

        bool Eligible(GuestStay guest, ServiceKind kind)
        {
            float probability = Settings.Eligibility * (StaffCount == 1 ? Settings.SoloFrequencyMultiplier : 1);
            var profile = guest.Application.Archetype;
            if (kind == ServiceKind.ExtraBlanket && (profile.Traits & GuestTraits.ColdSensitive) != 0) probability *= 1.6f;
            if ((kind == ServiceKind.WakeUpCall || kind == ServiceKind.LateCheckout) && profile.Kind == GuestKind.Business) probability *= 1.35f;
            if ((profile.Traits & GuestTraits.Patient) != 0) probability *= .8f;
            if ((profile.Traits & GuestTraits.Impatient) != 0) probability *= 1.15f;
            uint hash = 2166136261;
            unchecked
            {
                // A live guest does not get another eligibility roll when midnight passes.
                string identity = simulation.LivingSettings.Seed + "/" +
                    (simulation.ContinuousOperations ? "stay" : day.ToString()) + "/" + guest.GuestId + "/" + kind;
                foreach (char c in identity) hash = (hash ^ c) * 16777619;
                hash ^= hash >> 16; hash *= 0x7feb352d; hash ^= hash >> 15;
            }
            return (hash & 0x00ffffff) / 16777216f < Math.Min(1, probability);
        }

        bool TryCause(GuestStay guest, ServiceKind kind, float now, out string source, out int sourceRoom, out float due, out string reason, bool debug = false)
        {
            source = null; sourceRoom = guest.RoomId; due = Math.Min(guest.Agent.CheckoutTime, now + Settings.ReplySeconds); reason = null;
            if (!ContactWindowAvailable(guest, now)) return false;
            var room = rooms[guest.RoomId]; var agent = guest.Agent;
            bool seriousTemperature = HasComplaint(guest, IncidentReason.Temperature);
            switch (kind)
            {
                case ServiceKind.ExtraBlanket:
                    if (!agent.InAssignedRoom || guest.Memory.BlanketsDelivered > 0 || guest.Needs == null || seriousTemperature ||
                        room.Temperature >= guest.Application.Archetype.Needs.PreferredTemperatureMin ||
                        guest.Needs.Temperature.Severity < Settings.MildColdMinimum || guest.Needs.Temperature.Severity > Settings.MildColdMaximum ||
                        (!NaturalCommunicationEnabled && !debug && guest.Needs.Temperature.ExposureSeconds < Settings.ObservationSeconds)) return false;
                    source = "room/" + room.Profile.Id + "/temperature";
                    reason = IntentBehaviorEnabled ? "My room feels cold. Could you leave an extra blanket at my room's delivery point?" :
                        "It is still a little cold in my room."; return true;
                case ServiceKind.AskNeighborsQuiet:
                    if (!agent.InAssignedRoom || HasComplaint(guest, IncidentReason.Noise)) return false;
                    var noise = simulation.Incidents.Items.Where(item => item.GuestId == guest.GuestId && item.Active &&
                        item.Reason == IncidentReason.Noise && item.Stage == SituationStage.Observed && (NaturalCommunicationEnabled || debug || item.ExposureSeconds >= Settings.ObservationSeconds) &&
                        item.Cause != null && guest.Perception.NoiseSources.Any(emitter => emitter.SourceEntityId == item.Cause.SourceEntityId && emitter.ReceivedNoise > 0))
                        .OrderByDescending(item => item.Severity).ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
                    if (noise == null) return false;
                    source = noise.Cause.SourceEntityId; sourceRoom = noise.Cause.SourceRoomId;
                    reason = "I can hear noise nearby and cannot rest comfortably."; return true;
                case ServiceKind.LuggageStorage:
                    if (agent.State != GuestAgentState.WaitingForCheckIn || (!debug && agent.WaitingSeconds < Settings.ObservationSeconds) ||
                        (!room.Occupied && room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null &&
                        simulation.Housekeeping?.Find(room.Profile.Id) == null)) return false;
                    source = "room/" + room.Profile.Id + "/readiness";
                    reason = "While my room is being prepared, may I leave my luggage here?"; return true;
                case ServiceKind.WakeUpCall:
                    if (NaturalCommunicationEnabled && !ScheduleContext(guest, kind, now)) return false;
                    if (!agent.InAssignedRoom || (!debug && (agent.SleepStarted || guest.Application.Archetype.Kind != GuestKind.Business)) ||
                        (!debug && now < agent.Schedule.SleepTime - Settings.ReplySeconds) ||
                        agent.CheckoutTime - now < Settings.WakeLeadSeconds + Settings.ReplySeconds * .5f) return false;
                    due = agent.Schedule.HasDailyRhythm ? agent.Schedule.WakeTime : agent.CheckoutTime - Settings.WakeLeadSeconds;
                    if (due <= agent.Schedule.SleepTime || due <= now + Settings.WakeToleranceSeconds) return false;
                    source = "schedule/" + guest.GuestId + "/departure";
                    reason = "I have an early departure. Could you give me a wake-up call?"; return true;
                case ServiceKind.LateCheckout:
                    if (NaturalCommunicationEnabled && !ScheduleContext(guest, kind, now)) return false;
                    if (!agent.InAssignedRoom || (!debug && now < agent.CheckoutTime - Settings.LateCheckoutRequestLead) ||
                        now >= agent.CheckoutTime - Settings.ObservationSeconds || agent.State == GuestAgentState.Sleeping ||
                        (!debug && guest.Application.Archetype.Kind != GuestKind.Business && (guest.Application.Archetype.Traits & GuestTraits.Patient) == 0)) return false;
                    float limit = simulation.ContinuousOperations ? simulation.LatestCheckoutForRoom(guest.RoomId, guest.GuestId) : serviceEnd - 3;
                    due = Math.Min(limit, agent.CheckoutTime + Settings.LateCheckoutExtension);
                    if (due <= agent.CheckoutTime + 1) return false;
                    source = "schedule/" + guest.GuestId + "/checkout";
                    reason = "Could I check out later? I would appreciate the extra time; the room will need preparing later."; return true;
                default: return false;
            }
        }

        bool HasComplaint(GuestStay guest, IncidentReason reason) => simulation.Incidents.Items.Any(item => item.GuestId == guest.GuestId &&
            item.Reason == reason && item.Active && item.HasContactedStaff && item.Stage >= SituationStage.Complaint);

        ServiceCase TryCreate(GuestStay guest, ServiceKind kind, float now, float due, string source, int sourceRoom, string reason)
        {
            if (cases.Count >= CaseCapacity) return null;
            string id = guest.GuestId + "/service/" + kind + "/" + source;
            if (cases.Any(item => item.Id == id || item.GuestId == guest.GuestId && item.Kind == kind)) return null;
            if (NaturalCommunicationEnabled && (kind == ServiceKind.ExtraBlanket || kind == ServiceKind.AskNeighborsQuiet) &&
                !simulation.Incidents.Items.Any(item => item.GuestId == guest.GuestId && item.Active && item.Cause?.SourceEntityId == source)) return null;
            var result = new ServiceCase(id, guest.GuestId, guest.RoomId, kind, now, due, source, sourceRoom, reason);
            if (NaturalCommunicationEnabled && !BindCaseResponse(result, guest, now)) return null;
            cases.Add(result);
            if (!NaturalCommunicationEnabled) { ChargeBudget(result); guest.Memory.ServicesRequested = Count(guest.Memory.ServicesRequested); }
            Notify(result, "requested"); return result;
        }

        void UpdateCase(ServiceCase item, GuestStay guest, float now, float dt)
        {
            item.RoomId = guest.RoomId;
            if (IntentBehaviorEnabled && item.Kind == ServiceKind.ExtraBlanket)
            {
                // An explicit blanket promise is fulfilled by receipt of that item. A warmer
                // room can make the request unnecessary, but cannot manufacture a delivery reward.
                if (!guest.Agent.InAssignedRoom || guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower) return;
                bool noLongerCold = guest.Needs.Temperature.Severity <= simulation.NeedsSettings.RecoverySeverityThreshold;
                item.RecoverySeconds = noLongerCold ? item.RecoverySeconds + dt : 0;
                if (item.RecoverySeconds >= simulation.NeedsSettings.RecoverySeconds)
                { Finish(item, guest, ServiceStatus.Expired, 0); item.ResolutionReason = "Blanket no longer needed"; return; }
                if (now >= item.DueTime && item.Status != ServiceStatus.InProgress) Finish(item, guest, ServiceStatus.Expired, 0);
                return;
            }
            if (item.Kind == ServiceKind.AskNeighborsQuiet)
            {
                var source = guest.Perception.NoiseSources.FirstOrDefault(sound => sound.SourceEntityId == item.SourceEntityId);
                if (source != null) item.SourceRoomId = source.SourceRoomId;
            }
            if (item.Kind == ServiceKind.WakeUpCall && item.Status == ServiceStatus.InProgress) return;
            if (item.Kind == ServiceKind.ExtraBlanket || item.Kind == ServiceKind.AskNeighborsQuiet)
            {
                IncidentReason reason = item.Kind == ServiceKind.ExtraBlanket ? IncidentReason.Temperature : IncidentReason.Noise;
                bool escalated = NaturalCommunicationEnabled ? item.Response != null && Incident(item.Response)?.Active == true &&
                    Incident(item.Response).HasContactedStaff && Incident(item.Response).Stage >= SituationStage.Complaint : HasComplaint(guest, reason);
                if (escalated) { Finish(item, guest, ServiceStatus.Escalated, 0); return; }
                if (guest.Agent.InAssignedRoom)
                {
                    bool improved = item.Kind == ServiceKind.ExtraBlanket ? guest.Needs.Temperature.Severity <= simulation.NeedsSettings.RecoverySeverityThreshold :
                        guest.Needs.Noise.Severity <= simulation.NeedsSettings.RecoverySeverityThreshold ||
                        !guest.Perception.NoiseSources.Any(source => source.SourceEntityId == item.SourceEntityId && source.ReceivedNoise > 0);
                    item.RecoverySeconds = improved ? item.RecoverySeconds + dt : 0;
                    if (item.RecoverySeconds >= simulation.NeedsSettings.RecoverySeconds)
                    {
                        bool staffHelped = NaturalCommunicationEnabled ? item.Response?.StaffActionAt >= 0 && item.IsKnownToHotel :
                            item.Status != ServiceStatus.Requested || guest.BlanketComfortBonus > 0 ||
                            simulation.Guests.Any(source => item.SourceEntityId.StartsWith(source.GuestId + "/", StringComparison.Ordinal) && source.Agent.QuietUntil > now);
                        Finish(item, guest, ServiceStatus.Fulfilled, staffHelped ? Settings.FulfilledBonus : 0, staffHelped); return;
                    }
                }
            }
            if (item.Kind == ServiceKind.LuggageStorage && guest.Agent.CheckedIn)
            { Finish(item, guest, ServiceStatus.Expired, 0); return; }
            float expiry = item.Kind == ServiceKind.WakeUpCall ? item.DueTime :
                item.Kind == ServiceKind.LateCheckout ? guest.Agent.CheckoutTime : item.DueTime;
            if (now >= expiry) Finish(item, guest, ServiceStatus.Expired, 0);
        }

        int Count(int current) => Math.Min(simulation.NeedsSettings.MemoryCountLimit, current + 1);
        void Score(GuestStay guest, float delta) => guest.ServiceSatisfactionAdjustment = Number.Clamp(
            guest.ServiceSatisfactionAdjustment + delta, -Settings.MaximumScoreAdjustment, Settings.MaximumScoreAdjustment);
        void Finish(ServiceCase item, GuestStay guest, ServiceStatus status, float score, bool fulfilled = false)
        {
            if (item == null || !item.Active) return;
            item.Status = status; item.ResolutionAt = simulation.Elapsed; item.ResolutionReason = status.ToString();
            FinishCaseIntent(item, status);
            if (fulfilled) guest.Memory.ServicesFulfilled = Count(guest.Memory.ServicesFulfilled);
            if (status == ServiceStatus.Declined) guest.Memory.ServicesDeclined = Count(guest.Memory.ServicesDeclined);
            Score(guest, score); Notify(item, status.ToString().ToLowerInvariant());
        }
        void Notify(ServiceCase item, string verb)
        { Changed?.Invoke(item); if (item.IsKnownToHotel && !(NaturalCommunicationEnabled && item.Status == ServiceStatus.Escalated))
                simulation.SignalEvent("Room " + item.RoomId + ": " + item.Kind + " service " + verb); }

        internal void EndGuestStay(GuestStay guest)
        {
            if (simulation.IsReadOnlyMirror) return;
            EndGuestIntents(guest);
            EndGuestResponses(guest);
            foreach (var promise in promises.Where(item => item.GuestId == guest.GuestId && item.Status == PromiseStatus.Accepted).ToArray())
            {
                // Ending a stay before the call's opportunity expires is cancellation, not
                // a broken promise. Preserve the same strict deadline as the normal call tick.
                if (simulation.ContinuousOperations && simulation.Elapsed <= promise.DueTime + Settings.WakeMissSeconds)
                    promise.Status = PromiseStatus.Cancelled;
                else MissPromise(promise, guest);
            }
            foreach (var item in cases.Where(item => item.GuestId == guest.GuestId && item.Active).ToArray()) Finish(item, guest, ServiceStatus.Expired, 0);
        }
        internal void SettleShift()
        { if (!simulation.IsReadOnlyMirror) foreach (var guest in simulation.Guests) EndGuestStay(guest); }
    }
}
