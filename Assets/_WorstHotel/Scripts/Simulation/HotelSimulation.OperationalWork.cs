using System;
using System.Linq;

namespace WorstHotel
{
    [Flags] public enum RoomDisorder { None = 0, Waste = 1, Towels = 2, Chair = 4 }

    public sealed partial class HotelSimulation
    {
        // Measurements belong to each actual room, so transfers cannot erase earlier use.
        void TickOperationalUse(float dt)
        {
            if (!ContinuousOperations || !LivingEnabled) return;
            float hours = dt * 24 / Operations.SecondsPerDay;
            foreach (var guest in guests)
            {
                var a = guest.Agent;
                if (guest.ReceiptPosted || a == null || !a.InAssignedRoom || !a.HasReachedRoom ||
                    rooms[guest.RoomId].GuestId != guest.GuestId) continue;
                var room = rooms[guest.RoomId];
                room.UsedHours = Math.Min(48, room.UsedHours + hours);
                if (Director?.Visitors.Any(v => v.HostGuestId == guest.GuestId && Director.VisitorUsingRoom(v)) == true)
                {
                    room.UsedHours = Math.Min(48, room.UsedHours + hours);
                    room.DisplacedHours = Math.Min(24, room.DisplacedHours + hours);
                    if (room.DisplacedHours >= .4f) room.Disorder |= RoomDisorder.Chair;
                }
                if (room.UsedHours >= 4) room.Disorder |= RoomDisorder.Waste;
                if (!a.ActivityStaged) continue;
                if (a.Activity == GuestActivity.Shower && a.State != GuestAgentState.Sleeping)
                {
                    room.ShowerHours = Math.Min(24, room.ShowerHours + hours);
                    if (room.ShowerHours >= .75f) room.Disorder |= RoomDisorder.Towels;
                }
                if (a.Activity == GuestActivity.Unpack && GuestServiceSystem.LuggageCount(guest.Application) >= 3 ||
                    a.Activity == GuestActivity.LoudRoom)
                {
                    room.DisplacedHours = Math.Min(24, room.DisplacedHours + hours);
                    if (room.DisplacedHours >= .4f) room.Disorder |= RoomDisorder.Chair;
                }
            }
        }

        void SeedStartingWork()
        {
            if (!LivingEnabled || Housekeeping == null) return;
            // A single previous occupant left a bed unfinished; no midnight/reset hook calls this.
            var room = rooms.Values.Where(r => r.Operational).OrderBy(r => r.Profile.Id).FirstOrDefault();
            if (room != null) { room.UsedHours = 2; Housekeeping.MarkDirty(room.Profile.Id); }
        }

        public float CheckInPatience(GuestStay guest) => Math.Max(35, guest.Agent.WaitingPatience);
        public string WaitingClue(GuestStay guest)
        {
            if (guest?.Agent == null) return "";
            if (guest.LockedOut) return "I left my key inside room " + guest.RoomId + ". Please use the STAFF key to open my door." +
                (guest.LockoutSeconds >= CheckInPatience(guest) * 3 ? " I will leave if I cannot get back in soon." : "");
            float ratio = guest.Agent.WaitingSeconds / CheckInPatience(guest);
            bool dirty = rooms[guest.RoomId].Cleanliness != Cleanliness.Clean;
            return ratio >= 2 ? "I cannot wait much longer. I will find another hotel." :
                ratio >= 1 ? (dirty ? "My room is still not ready. Please finish preparing it." : "I have been waiting for my key. Please check me in.") :
                "Hello. I have a reservation for room " + guest.RoomId + ". " +
                (dirty ? "Is it not ready yet? I can wait a little while you prepare it." : "May I have my room key, please?");
        }

        void TickOperationalConsequences(float dt)
        {
            if (!ContinuousOperations || !LivingEnabled) return;
            foreach (var guest in guests.ToArray())
            {
                var a = guest.Agent;
                if (guest.ReceiptPosted || a == null || a.State == GuestAgentState.CheckingOut ||
                    a.State == GuestAgentState.Leaving || a.State == GuestAgentState.Left) continue;
                float patience = CheckInPatience(guest);
                if (!a.CheckedIn && a.State == GuestAgentState.WaitingForCheckIn && a.WaitingSeconds >= patience * 3)
                {
                    guest.AbandonedCheckIn = true;
                    BeginGuestCheckout(guest, Elapsed + dt);
                    continue;
                }
                if (guest.LockedOut)
                {
                    guest.LockoutSeconds += dt;
                    if (guest.LockoutSeconds >= patience * 4)
                        DepartForServiceFailure(guest, "Left early after being unable to enter room " + guest.RoomId + ".", dt);
                    continue;
                }
                // A promised bag still physically outside the room is a continuing service failure.
                // Fixing it stops accumulation; old elapsed request timers never impose a cash fine.
                bool missingBags = a.HasReachedRoom && Services != null && Services.Items.Any(i =>
                    i.Kind == ServiceItemKind.Luggage && i.GuestId == guest.GuestId && i.StaffHandling &&
                    i.Location != ServiceItemLocation.Delivered);
                guest.LuggageDelaySeconds = missingBags ? guest.LuggageDelaySeconds + dt : 0;
                if (missingBags && guest.LuggageDelaySeconds >= patience * 5)
                    DepartForServiceFailure(guest, "Left early because the luggage entrusted to staff never reached the room.", dt);
            }
        }

        void DepartForServiceFailure(GuestStay guest, string reason, float dt)
        {
            if (NeedsSettings.EarlyCheckout.Enabled != true) return;
            if (Elapsed + dt >= guest.Agent.CheckoutTime) return;
            guest.EarlyCheckout.Reset();
            guest.EarlyCheckout.IncidentId = guest.GuestId + "/physical-service";
            guest.EarlyCheckout.IncidentEpisode = 1;
            guest.EarlyCheckout.WarningAt = Math.Max(guest.Agent.ArrivalTime, Elapsed - CheckInPatience(guest));
            guest.EarlyCheckout.State = EarlyCheckoutState.Committed;
            guest.EarlyCheckout.RoomId = guest.RoomId;
            guest.EarlyCheckout.Reason = IncidentReason.Service;
            guest.EarlyCheckout.CauseDescription = reason;
            guest.EarlyCheckout.CommittedAt = Elapsed + dt;
            BeginGuestCheckout(guest, Elapsed + dt);
        }

        // One deterministic opportunity per real stay, at a real departure. No occupancy roll.
        void TryLeaveRoomKeyBehind(GuestStay guest)
        {
            if (!ContinuousOperations || guest.KeyLossConsidered || !guest.Agent.HasReachedRoom) return;
            guest.KeyLossConsidered = true;
            uint hash = 2166136261;
            foreach (char c in guest.GuestId + "/forgot-key/" + LivingSettings.Seed)
                hash = unchecked((hash ^ c) * 16777619);
            if (hash % 9 != 0 || guest.Agent.CheckoutTime - Elapsed < CheckInPatience(guest) * 5) return;
            Keys.LeaveInside(guest.GuestId, guest.RoomId);
        }

        public bool CanUnlockForGuest(int playerId, int roomId) => !IsReadOnlyMirror && Running && !OwnershipLost &&
            Keys.Find(0)?.Location == RoomKeyLocation.HeldByPlayer && Keys.Find(0).PlayerId == playerId &&
            guests.Any(g => !g.ReceiptPosted && g.RoomId == roomId && g.LockedOut &&
                g.Agent.State == GuestAgentState.WaitingForCheckIn);

        // Invoked only by the reached physical door with the actual carried staff key.
        public CommandResult UnlockForGuest(int playerId, int roomId)
        {
            if (!CanUnlockForGuest(playerId, roomId)) return CommandResult.Fail("Bring the STAFF key to the locked-out guest's door.");
            var guest = guests.First(g => g.RoomId == roomId && g.LockedOut && !g.ReceiptPosted);
            guest.LockedOut = false;
            Transition(guest, GuestAgentState.ReturningToRoom, Elapsed, guest.Name + ": staff opened room " + roomId);
            return CommandResult.Ok("Door unlocked. The guest will return and collect their key inside.");
        }
    }
}
