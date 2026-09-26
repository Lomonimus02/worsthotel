using UnityEngine;

namespace WorstHotel
{
    public sealed class ElectricalBreakerControl : HotelInteractable
    {
        public string circuitId = "A";
        public ElectricalPanelPresentation panel;
        public ElectricalCircuit Circuit => GameSession.Instance?.Simulation?.Electrical?.Find(circuitId);

        public override bool CanInteract(PlayerInteractor actor) => base.CanInteract(actor) && actor != null &&
            panel != null && panel.cover != null && panel.cover.IsPassageOpen && Circuit != null && Circuit.Tripped &&
            GameSession.Instance != null && (GameSession.Instance.Phase == DayPhase.Planning || GameSession.Instance.Phase == DayPhase.Service);

        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            GameSession.Instance.ResetCircuit(actor.ActorId, circuitId);
        }

        public override string GetPrompt(PlayerInteractor actor)
        {
            var circuit = Circuit;
            if (circuit == null) return "Circuit unavailable";
            if (circuit.Tripped && GameSession.Instance.Phase != DayPhase.Planning && GameSession.Instance.Phase != DayPhase.Service)
                return "Power off · Reset available during hotel preparation";
            string consumers = "\n" + ElectricalPanelPresentation.ConsumerBreakdown(GameSession.Instance.Simulation.Electrical, circuitId);
            if (GameSession.Instance.Simulation.ContinuousOperations)
                return (circuit.Tripped ? "Power off · Reset breaker" : "Power on · " + CapacityLabels.Band(circuit.CapacityBand)) +
                    "\nRequested " + circuit.RequestedLoad.ToString("F2") + " / " + circuit.Capacity.ToString("F2") + " u · reserve " + CapacityLabels.Reserve(circuit.Reserve) + " u" +
                    "\nDelivered " + circuit.ActualDeliveredLoad.ToString("F2") + " u" +
                    "\nLoad " + CapacityLabels.Percent(circuit.LoadRatio) + " · stress " + CapacityLabels.Percent(circuit.Stress01) + consumers;
            if (circuit.Tripped) return "Power off · Reset breaker\nRequested load " + circuit.RequestedLoad.ToString("F2") + " / " + circuit.Capacity.ToString("F2") + consumers;
            return (circuit.Warning ? "Overload warning" : "Power on") + " · Load " +
                circuit.RequestedLoad.ToString("F2") + " / " + circuit.Capacity.ToString("F2") + consumers;
        }
    }
}
