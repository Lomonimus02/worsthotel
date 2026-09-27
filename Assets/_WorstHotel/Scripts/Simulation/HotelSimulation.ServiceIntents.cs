namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public CommandResult BeginCompensationDiscussion(int actorId, string guestId, string incidentId = null) =>
            Services?.BeginCompensationDiscussion(actorId, guestId, incidentId) ?? ServicesDisabled();

        public CommandResult EndCompensationDiscussion(int actorId, string guestId, string expectedIntentId, int expectedRevision) =>
            Services?.EndCompensationDiscussion(actorId, guestId, expectedIntentId, expectedRevision) ?? ServicesDisabled();

        public CommandResult DropOffBlanket(int actorId, string guestId, int roomId, int expectedIntentRevision, int expectedItemGeneration) =>
            Services?.DropOffBlanket(actorId, guestId, roomId, expectedIntentRevision, expectedItemGeneration) ?? ServicesDisabled();

        internal void HoldGuestForServiceIntent(GuestStay guest)
        {
            // A real reception/phone contact already has its physical anchor and versioned route.
            if (guest.Agent.ResponseActionId != null || !guest.Agent.InAssignedRoom) return;
            SetActivity(guest, GuestActivity.QuietRest, Elapsed, float.PositiveInfinity);
            RefreshGuestLoad(); RefreshElectrical();
        }

        internal void ResumeGuestAfterServiceIntent(GuestStay guest)
        {
            if (guest.Agent.ResponseActionId != null) ClearGuestResponseAction(guest, true);
            else if (guest.Agent.InAssignedRoom)
                SetActivity(guest, GuestActivity.QuietRest, Elapsed, LivingSettings.QuietDurationMin);
            RefreshGuestLoad(); RefreshElectrical();
        }
    }
}
