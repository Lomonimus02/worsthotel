using System;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool CanAskToUnplugAmplifier(string guestId) => Services != null && Services.Items.Any(i => i.GuestId == guestId &&
            i.Payload == LuggagePayload.Amplifier && i.Location == ServiceItemLocation.Delivered && !i.EquipmentSwitchedOff);

        public CommandResult AskToUnplugAmplifier(int actorId, string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            var guest = guests.FirstOrDefault(g => g.GuestId == guestId);
            if (actorId < 0 || actorId > 1 || !Running || guest?.Agent == null || !guest.Agent.InAssignedRoom ||
                guest.Agent.State == GuestAgentState.Sleeping || guest.Agent.Activity == GuestActivity.Shower || !CanAskToUnplugAmplifier(guestId))
                return CommandResult.Fail("Speak to the guest while they are available in their room.");
            foreach (var item in Services.Items.Where(i => i.GuestId == guestId && i.Payload == LuggagePayload.Amplifier))
                item.EquipmentSwitchedOff = true;
            RefreshElectrical();
            return CommandResult.Ok("Of course. I'll leave the amplifier unplugged for the rest of my stay.");
        }

        public CommandResult RequestQuiet(int actorId, string guestId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0) return CommandResult.Fail("Unknown player identity.");
            if (!Running || !LivingEnabled) return CommandResult.Fail("Quiet requests are available during a living guest's stay.");
            var guest = guests.FirstOrDefault(g => g.GuestId == guestId);
            if (guest == null || !guest.Agent.InAssignedRoom || !guest.Agent.ActivityStaged ||
                !Noise.Sources.Any(source => source.SourceGuestId == guestId && source.Active &&
                    (source.Category == NoiseCategory.Amplifier || source.Category == NoiseCategory.Television || source.Category == NoiseCategory.PhoneCall)))
                return CommandResult.Fail("This guest is not making noise in their room.");
            if (guest.Agent.QuietUntil > Elapsed) return CommandResult.Fail("This guest has already agreed to keep it down for a while.");
            bool temporary = (guest.Application.Archetype.Traits & GuestTraits.Noisy) != 0;
            int previousWarnings = guest.Memory.PreviousNoiseWarnings;
            float duration = NoiseSettings.QuietRequestSeconds * Math.Max(NoiseSettings.MinimumWarningDurationMultiplier,
                1 - previousWarnings * NoiseSettings.RepeatedWarningDurationReduction);
            float until = temporary ? (float)Math.Min(guest.Agent.CheckoutTime, (double)Elapsed + duration) :
                guest.Agent.CheckoutTime;
            if (!Number.IsFinite(until) || until <= Elapsed) return CommandResult.Fail("This guest is already due to check out.");
            guest.Agent.QuietUntil = until;
            guest.Memory.PreviousNoiseWarnings = Math.Min(NeedsSettings.MemoryCountLimit, previousWarnings + 1);
            Incidents.RecordNoiseWarning(guest.GuestId);
            foreach (var incident in Incidents.Items.Where(i => i.Active && i.Reason == IncidentReason.Noise && i.Cause?.SourceGuestId == guestId))
                Services?.RecordStaffAction(incident.GuestId, IncidentReason.Noise, incident.Cause.SourceEntityId);
            RefreshElectrical();
            SignalEvent(guest.Name + (temporary ? " agreed to lower the volume temporarily" : " agreed to lower the volume for the rest of the stay"));
            string reply = previousWarnings == 0 ? "Sorry, I'll keep it down." : temporary ?
                "You've already told me. I'll lower it again for now." : "Understood. I'll keep it quiet.";
            return CommandResult.Ok("Room " + guest.RoomId + ": " + reply);
        }

        void UpdateQuietRequests(float now)
        {
            foreach (var guest in guests)
            {
                if (guest.Agent.QuietUntil <= 0 || guest.Agent.QuietUntil > now) continue;
                guest.Agent.QuietUntil = 0;
            }
        }

        public CommandResult ClearRoomNoiseOverride(int roomId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (!LivingEnabled) return CommandResult.Fail("The living noise system is not active.");
            var result = Noise.SetNoiseOverride(roomId, null);
            if (result.Success) RefreshElectrical();
            return result;
        }
    }
}

