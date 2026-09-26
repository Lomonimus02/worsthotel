using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ShiftHUD
    {
        void DrawServices(GameSession session, float x)
        {
            var services = session.Simulation.Services;
            if (services == null) return;
            var cases = services.Cases.Where(GuestLabels.IsKnownOpenService).OrderBy(c => c.CreatedAt).ToArray();
            var promise = services.Promises.Where(p => p.Status == PromiseStatus.Accepted).OrderBy(p => p.DueTime).FirstOrDefault();
            string contact = GuestLabels.ContactCue(session.Simulation);
            if (cases.Length == 0 && promise == null && contact == null) return;
            const float y = 485;
            Fill(new Rect(x, y, 600, 121), new Color(.12f, .22f, .20f, .87f));
            Label(new Rect(x + 14, y + 5, 572, 23), contact ?? "RECEPTION NOTES · " + cases.Length + " open", Small, new Color(.72f, .87f, .77f));
            int i = 0;
            foreach (var item in cases.Take(2))
                Label(new Rect(x + 14, y + 30 + i++ * 22, 572, 23), item.RoomId + " · " + GuestLabels.Service(item.Kind) +
                    " · " + GuestLabels.ServiceState(item.Status), Small, Paper);
            if (promise != null)
            {
                float remaining = promise.DueTime - session.Simulation.Elapsed;
                Label(new Rect(x + 14, y + 90, 572, 22), "PHONE · Room " + promise.RoomId + (remaining > 0 ? " at " + GuestLabels.HotelTime(promise.DueTime) : " · wake-up call due"),
                    Small, remaining > 0 ? Paper : new Color(1, .87f, .58f));
            }
        }
    }
}
