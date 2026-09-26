using System;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        // Extends this single-night booking only. The existing schedule, physical exit,
        // key return and dirty-bed turnover remain authoritative.
        internal CommandResult ApplyServiceLateCheckout(GuestStay guest, float requestedTime)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!Running || guest?.Agent == null || !guest.Agent.CheckedIn || !guest.Agent.HasReachedRoom ||
                guest.Agent.State == GuestAgentState.CheckingOut || guest.Agent.State == GuestAgentState.Leaving ||
                guest.Agent.State == GuestAgentState.Left || !Number.IsFinite(requestedTime))
                return CommandResult.Fail("Late checkout needs a current checked-in stay.");
            float limit = ContinuousOperations ? LatestCheckoutForRoom(guest.RoomId, guest.GuestId) : settings.ServiceSeconds - 3;
            float checkout = Math.Min(limit, requestedTime);
            if (checkout <= guest.Agent.CheckoutTime || checkout <= Elapsed)
                return CommandResult.Fail(ContinuousOperations ? "There is no further checkout window before the next arrival needs this room." :
                    "There is no further checkout window before this shift closes.");
            guest.Agent.Schedule.CheckoutTime = checkout;
            return CommandResult.Ok("Late checkout agreed. The room remains occupied until " + checkout.ToString("F0") + " hotel seconds; prepare its used linen after the guest leaves.");
        }
    }
}
