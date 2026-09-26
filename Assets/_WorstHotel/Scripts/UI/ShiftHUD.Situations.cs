using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ShiftHUD
    {
        static void DrawSituations(GameSession session, float x, float y)
        {
            if (!session.Simulation.LivingEnabled) return;
            var active = session.Simulation.Incidents.Items.Where(GuestLabels.IsActionable)
                .OrderBy(s => s.AttentionAcknowledged || s.PausedForTransfer).ThenByDescending(s => s.Stage).ThenBy(s => s.RoomId).ToArray();
            int shown = Mathf.Min(3, active.Length);
            for (int i = 0; i < shown; i++)
            {
                var situation = active[i];
                var color = situation.Stage >= SituationStage.Escalated ? new Color(1, .57f, .38f) : Paper;
                Fill(new Rect(x + 14, y + i * 59, 730, 55), new Color(.10f, .14f, .12f, .90f));
                Label(new Rect(x + 25, y + i * 59 + 3, 708, 24), "ROOM " + situation.RoomId + "  /  " +
                    GuestLabels.Problem(situation.Reason) + "  ·  " + GuestLabels.Situation(situation.Stage) +
                    (situation.PausedForTransfer ? " / moving" : situation.ResponseAccepted ?
                        (situation.ResponseReliefRemainingSeconds > 0 ? " / credit relief " + Mathf.CeilToInt(situation.ResponseReliefRemainingSeconds) + "s" :
                            " / credit; cause remains") : situation.AttentionAcknowledged ? " / accepted loss" : ""), Small, color);
                Label(new Rect(x + 25, y + i * 59 + 28, 708, 24), GuestLabels.ComplaintClue(situation), Small, Paper);
            }
            if (active.Length > shown)
            {
                Fill(new Rect(x + 14, y + shown * 59, 730, 28), new Color(.10f, .14f, .12f, .90f));
                Label(new Rect(x + 25, y + shown * 59, 708, 25), "+ " + (active.Length - shown) + " other guest cases in the reception ledger", Small, Paper);
            }
        }
    }
}
