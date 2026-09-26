using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        int guestLedgerPage;
        void DrawServiceLedger()
        {
            if (showingHousekeeping) { DrawHousekeeping(); return; }
            if (selectedServiceGuest != null && DrawServiceGuestDetail()) return;
            Fill(new Rect(15, 50, 770, 820), Paper);
            Border(new Rect(23, 58, 754, 804), Brass);
            Label(new Rect(42, 78, 700, 48), "GUEST RELATIONS", Title);
            Label(new Rect(42, 132, 448, 61), "Conversations and agreements with your guests. They may also call or come to reception.", Small, Muted);
            if (Session.Simulation.Boiler.Failed && !Session.Simulation.Boiler.MaintenanceInProgress)
                ButtonAt(new Rect(502, 139, 245, 48), Session.Simulation.BoilerFailureAcknowledged ? "Boiler loss accepted" : "Leave boiler failed",
                    () => Session.AcceptBoilerConsequences(owner), !Session.Simulation.BoilerFailureAcknowledged);
            int i = 0;
            var currentGuests = Session.Simulation.Guests.Where(guest => !Session.Simulation.ContinuousOperations ||
                guest.Agent == null || guest.Agent.State != GuestAgentState.Left && guest.Agent.State != GuestAgentState.Leaving).ToArray();
            guestLedgerPage = Mathf.Clamp(guestLedgerPage, 0, Mathf.Max(0, (currentGuests.Length - 1) / 6));
            foreach (var guest in currentGuests.Skip(guestLedgerPage * 6).Take(6))
            {
                float y = 204 + i * 78;
                var room = Session.Rooms.First(x => x.Profile.Id == guest.RoomId);
                string status = guest.Agent == null ? room.Temperature.ToString("F1") + "°C" :
                    (guest.Agent.CheckedIn ? GuestLabels.State(guest.Agent) + " / " + room.Temperature.ToString("F1") + "°C" : GuestLabels.State(guest.Agent));
                string id = guest.GuestId;
                ButtonAt(new Rect(42, y, 438, 44), guest.RoomId + "  " + guest.Name + "\n" + status + "   /   $" + guest.Price + (guest.Compensated ? " credit $" + guest.CompensationCredit : ""), () => { selectedServiceGuest = id; focus = 0; });
                var situation = Session.Simulation.Incidents.Items.Where(s => s.GuestId == id && GuestLabels.IsActionable(s)).OrderByDescending(s => s.Stage).FirstOrDefault();
                var service = Session.Simulation.Services?.Cases.FirstOrDefault(c => c.GuestId == id && GuestLabels.IsKnownOpenService(c));
                string complaint = situation != null ? GuestLabels.Problem(situation.Reason) + " / " + GuestLabels.Situation(situation.Stage) :
                    service != null ? "Request: " + GuestLabels.Service(service.Kind, Session.Simulation) : "No reported problem";
                Label(new Rect(42, y + 48, 438, 25), complaint, Small, situation?.Stage >= SituationStage.Escalated ? Red : Muted);
                ButtonAt(new Rect(502, y + 2, 245, 48), situation != null ? "Read concern / choices" : "Room / stay details",
                    () => { selectedServiceGuest = id; focus = 0; });
                i++;
            }
            ButtonAt(new Rect(42, 692, 342, 36), "Room preparation / linen", OpenHousekeeping);
            ButtonAt(new Rect(405, 692, 342, 36), "Service board / promises", () => ShowServices(), Session.Simulation.Services != null);
            if (Session.Simulation.ContinuousOperations)
                ButtonAt(new Rect(405, 746, 342, 38), "Hotel operations / bookings", ShowOperations);
            if (currentGuests.Length > 6)
                ButtonAt(new Rect(502, 80, 245, 38), "More guests ›", () => { guestLedgerPage = (guestLedgerPage + 1) % ((currentGuests.Length + 5) / 6); focus = 0; });
            Label(new Rect(42, 733, Session.Simulation.ContinuousOperations ? 342 : 700, 54), "Cash $" + Session.Cash.ToString("F0") + "  /  Credits $" + Session.Simulation.OutstandingCompensation + "\n" + Session.LastMessage, Small, Muted);
            ButtonAt(new Rect(42, 799, 705, 42), "Close ledger / keep working", Close);
        }

        void DrawSettlement()
        {
            var report = Session.Report;
            if (report == null) return;
            Fill(new Rect(0, 0, 1600, 900), new Color(.08f, .09f, .07f, .92f));
            Fill(new Rect(70, 42, 1460, 816), Paper);
            Border(new Rect(82, 54, 1436, 792), Brass);
            Label(new Rect(110, 78, 1250, 52), "DAY " + Session.Day + "  /  THE BILL COMES DUE", Title);
            Label(new Rect(112, 136, 1280, 30), "Guests pay the agreed rate, less compensation. Their experience stays with the hotel.", Body, Muted);
            Label(new Rect(115, 196, 650, 28), "ROOM / GUEST", Small, Muted);
            Label(new Rect(775, 196, 160, 28), "SATISFACTION", Small, Muted);
            Label(new Rect(960, 196, 140, 28), "ROOM BILL", Small, Muted);
            Label(new Rect(1110, 196, 160, 28), "COMPENSATION", Small, Muted);
            Label(new Rect(1320, 196, 155, 28), "NET", Small, Muted);
            int i = 0;
            foreach (var receipt in report.Receipts)
            {
                float y = 239 + i * 63;
                Fill(new Rect(110, y - 7, 1370, 1), new Color(.70f, .63f, .47f));
                var selected = receipt;
                ButtonAt(new Rect(115, y, 635, 28), receipt.RoomId + "  " + receipt.Name + "   /   Read review ›", () => { selectedReview = selected; focus = 0; });
                string excerpt = receipt.Review.Length > 76 ? receipt.Review.Substring(0, 73) + "…" : receipt.Review;
                Label(new Rect(115, y + 30, 635, 24), excerpt, Small, Muted);
                Label(new Rect(784, y + 4, 130, 36), receipt.Satisfaction.ToString("F0") + " / 100", Heading, receipt.Satisfaction < 60 ? Red : Teal);
                Label(new Rect(971, y + 4, 130, 36), "$" + receipt.Price, Heading);
                Label(new Rect(1154, y + 4, 130, 36), "−$" + receipt.Compensation, Heading, Wine);
                Label(new Rect(1320, y + 4, 130, 36), "$" + receipt.Net, Heading);
                i++;
            }
            Fill(new Rect(110, 641, 1370, 2), Brass);
            Label(new Rect(112, 665, 840, 80), "Gross $" + report.Gross + "  −  credits/refunds $" + report.Compensation + "  −  operations $" + report.OperatingCost + "\nNet today $" + report.Net + "     •     Reputation " + report.Reputation.ToString("F0") + "/100", Heading);
            Label(new Rect(1105, 665, 365, 60), "CASH  $" + report.Cash, Title);
            DrawSettlementActions();
        }

        void DrawSettlementActions()
        {
            ButtonAt(new Rect(112, 768, 450, 48), Session.Day >= Session.Settings.TotalDays ? "THREE-DAY RESULTS  ›" : "MAINTENANCE DECISION  ›", () => Session.ContinueAfterSettlement(owner), true, false, true);
            ButtonAt(new Rect(588, 768, 265, 48), "Inspect hotel", Close);
            Label(new Rect(878, 769, 590, 58), Session.LastMessage, Small, Muted);
        }

        void DrawReview()
        {
            LedgerPage("A NOTE FROM ROOM " + selectedReview.RoomId, selectedReview.Name + "  /  Satisfaction " + selectedReview.Satisfaction.ToString("F0") + "/100");
            Fill(new Rect(150, 270, 1300, 370), LightPaper);
            Border(new Rect(150, 270, 1300, 370), Brass);
            Label(new Rect(192, 312, 1216, 260), "“" + selectedReview.Review + "”", Heading);
            Label(new Rect(192, 589, 1216, 35), "Agreed rate $" + selectedReview.Price + "   −   compensation $" + selectedReview.Compensation + "   =   paid $" + selectedReview.Net, Body, Muted);
            ButtonAt(new Rect(150, 732, 560, 55), "BACK TO THE DAILY LEDGER", () => { selectedReview = null; focus = 0; }, true, false, true);
        }
    }
}
