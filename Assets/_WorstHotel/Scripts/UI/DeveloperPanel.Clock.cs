#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace WorstHotel
{
    public sealed partial class DeveloperPanel
    {
        float requestedClockSpeed = 1;
        void DrawClockDebug()
        {
            GUILayout.Label("Hotel clock: " + Session.Simulation.Clock.SimulationTime.ToString("F1") +
                "s / " + Session.Simulation.Clock.Speed + "x. Last event: " + Session.Simulation.LastEvent, body);
            GUILayout.BeginHorizontal();
            foreach (float speed in new[] { 1f, 4f, 8f })
                if (Button((requestedClockSpeed == speed ? "● " : "") + speed + "x after closing")) requestedClockSpeed = speed;
            GUILayout.EndHorizontal();
            GUILayout.Label("Developer override only. Gameplay WAIT requires both staff; physics stays at normal speed.", body);
            GUILayout.Label("Event advance steps the model while paused. Close F2 to let guests finish physical travel. Linen preparation requires player actions.", body);
            if (Button("Advance to next meaningful event", Session.Phase == DayPhase.Service))
                Apply(() => { Session.AdvanceToNextEvent(); return CommandResult.Ok(Session.Simulation.LastEvent); });
        }
    }
}
#endif
