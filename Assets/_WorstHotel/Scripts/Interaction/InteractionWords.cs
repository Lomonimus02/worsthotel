namespace WorstHotel
{
    // Local and authoritative remote targets share the same short physical vocabulary.
    public static class InteractionWords
    {
        public static string Caption(FirstPersonController player, bool enhanced = false)
        {
            var actor = player.Interactor;
            var target = actor.Focused;
            string line = null;
            if (target && target.CanInteract(actor) && (!actor.HeldBody || target.AllowsHeldItem(actor)))
            {
                string detail = target.GetPrompt(actor);
                line = player.Input.PrimaryLabel + " — " + Verb(target, actor, detail);
                if (enhanced) line = target.displayName + "\n" + detail;
            }
            else if (!actor.HeldBody && actor.FocusedPickup) line = player.Input.PrimaryLabel + " — Pick up";
            if (!actor.HeldBody && actor.FocusedPickup && target && target.CanInteract(actor))
                line += "  ·  " + player.Input.GrabLabel + " — Pick up";
            if (target is RadiatorValveInteraction) line += "  ·  " + player.Input.SecondaryLabel + " — Turn down";
            if (target is LuggageStorageZone storage && storage.CanFileLostProperty(actor)) line += "  ·  " + player.Input.SecondaryLabel + " — File lost property";
            if (actor.HeldBody) line = (line == null ? "" : line + "\n") + player.Input.GrabLabel + " — Put down";
            return line;
        }
        static string Verb(HotelInteractable target, PlayerInteractor actor, string detail)
        {
            if (target is DiegeticBookInteraction book) return "Read " + book.ShortTitle;
            if (target is ReceptionTerminal) return "Read reservations";
            if (target is ReceptionServiceBoardInteraction) return "Read notes";
            if (target is ReceptionPhoneInteraction) return GameSession.Instance?.Simulation?.Services?.IncomingCall != null ? "Answer" : "Pick up handset";
            if (target is DoorInteractable || target is RoomNoiseInteraction)
            {
                if (detail.StartsWith("Unlock")) return "Unlock with STAFF key";
                if (detail.Contains("EMERGENCY ACCESS")) return "Hold to enter";
                if (detail.Contains("Knock") || detail.Contains("PRIVATE") || detail.Contains("Guest needs privacy")) return "Knock";
                if (detail.Contains("Guest answers")) return "Talk";
                if (detail.StartsWith("Close")) return "Close";
                if (detail.StartsWith("Open") || detail.StartsWith("Agreed luggage delivery")) return "Open";
                return "Knock";
            }
            if (target is GuestReceptionInteraction) return actor.HeldBody && !detail.StartsWith("Locked out") ? "Give key" : "Talk";
            if (target is RoomResetInteraction reset) return reset.element == RoomDisorder.Waste ? "Hold to empty basket" :
                reset.element == RoomDisorder.Towels ? "Hold to collect towels" : "Hold to straighten chair";
            if (target is ElectricalBreakerControl) return "Flip breaker";
            if (target is RadiatorValveInteraction) return "Turn up";
            if (target is RepairControl control) return control.kind switch {
                RepairControlKind.ReliefValve => "Hold relief valve", RepairControlKind.Panel => "Open service hatch",
                RepairControlKind.Breaker => "Cut power", RepairControlKind.LatchA => "Hold latch A",
                RepairControlKind.LatchB => "Hold latch B", _ => "Press restart" };
            if (target is BoilerServiceInteraction station) return station.TryGetSelection(actor.ActorId, out _, out _) ? "Hold to service" : "Inspect";
            var value = detail ?? "Use";
            foreach (var separator in new[] { " ·", "\n", " / ", " —", ";" })
            { int index = value.IndexOf(separator, System.StringComparison.Ordinal); if (index >= 0) value = value.Substring(0, index); }
            if (value.Length <= 38) return value;
            if (target is DoorInteractable) return value.ToLowerInvariant().Contains("knock") ? "Knock" : "Open";
            if (target is LuggageCart) return "Guide cart";
            if (target is PortableHeater) return "Switch heater";
            if (target is RoomLampInteraction) return "Use lamp";
            return "Use";
        }
    }
}
