namespace WorstHotel
{
    public sealed partial class HotelSimulation
    {
        public bool BoilerFailureAcknowledged { get; private set; }

        void InitializeDecisionResponses()
        {
            Boiler.OnFailureStarted += () => BoilerFailureAcknowledged = false;
            Boiler.OnFailureResolved += () => BoilerFailureAcknowledged = false;
        }

        public CommandResult AcceptBoilerConsequences(int actorId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (!Running || !Boiler.Failed) return CommandResult.Fail("There is no active boiler failure to acknowledge.");
            if (BoilerFailureAcknowledged) return CommandResult.Fail("This failure has already been acknowledged.");
            BoilerFailureAcknowledged = true;
            SignalEvent("Management accepts the failed boiler's consequences");
            return CommandResult.Ok("Boiler left failed. Cold rooms and guest losses continue; both staff may choose WAIT.");
        }
    }
}

