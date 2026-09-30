using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        bool CanRestartOwnedHotel => !Session.IsLanReplica && (!LanSession.Instance || !LanSession.Instance.IsClientReplica);
        void RestartOwnedHotel() => Session.RestartSession(owner);
        void LeaveOwnedHotel() { if (LanSession.Instance) LanSession.Instance.LeaveToMenu(); }

        void UpdateOwnershipLostChoices()
        {
            actions.Clear(); enabledActions.Clear();
            if (CanRestartOwnedHotel) { actions.Add(RestartOwnedHotel); enabledActions.Add(true); }
            actions.Add(LeaveOwnedHotel); enabledActions.Add(LanSession.Instance != null);
            focus = Mathf.Clamp(focus, 0, actions.Count - 1);
        }

        void DrawOwnershipLost()
        {
            var previous = GUI.matrix;
            GUI.depth = -500;
            Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(.05f, .06f, .05f, .97f));
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 600f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 960 * scale) / 2, (Screen.height - 600 * scale) / 2, 0),
                Quaternion.identity, Vector3.one * scale);
            Fill(new Rect(55, 32, 850, 536), Paper);
            Border(new Rect(65, 42, 830, 516), Brass, 2);
            Label(new Rect(88, 65, 784, 52), "OWNERSHIP REVOKED", Title, Wine);
            var model = Session.Simulation;
            var report = model.OwnershipLossReport;
            var payment = model.LastContractPayment;
            Label(new Rect(90, 124, 780, 50), "The contract payment " +
                (payment == null ? "at 22:00" : "due " + GuestLabels.HotelMoment(model, payment.DueAt)) +
                " could not be met.\nThe hotel is closed. This run has ended.", Body);
            if (payment != null)
            {
                Label(new Rect(90, 196, 390, 98), "CONTRACT DUE   $" + payment.Due + "\nFUNDS AVAILABLE   $" + payment.FundsBeforePayment +
                    "\nACTUALLY PAID   $" + payment.PaidAmount, Body);
                Label(new Rect(505, 196, 362, 98), "SHORTFALL   $" + payment.Shortfall + "\nROOMS ASSESSED   " + payment.AssessedRooms +
                    "\nFINAL CASH   $" + payment.CashAfterPayment, Body, Wine);
            }
            Fill(new Rect(90, 310, 780, 2), Brass);
            if (report != null)
                Label(new Rect(90, 325, 780, 92), "Final partial period · revenue $" + report.Gross +
                    " · refunds $" + report.Compensation + " · operations $" + report.OperatingCost +
                    "\nRepairs / maintenance $" + report.MaintenanceSpend + " · purchases / renovation $" + report.CapitalSpend +
                    "\nLaundry service $" + report.LaundrySpend + " · bulb orders $" + report.BulbSpend +
                    "\nResult before contract: $" + report.Net + ". Unpaid bookings are not cash.", Body);
            Label(new Rect(90, 427, 780, 42), CanRestartOwnedHotel ? "Start a fresh hotel, or return to the main menu." :
                "Only the host can start a fresh hotel. You may wait here or leave.", Body, Muted);
            actions.Clear(); enabledActions.Clear();
            if (CanRestartOwnedHotel)
                ButtonAt(new Rect(90, 478, 370, 54), "START NEW HOTEL", RestartOwnedHotel, important: true);
            ButtonAt(new Rect(CanRestartOwnedHotel ? 495 : 90, 478, 370, 54), "MAIN MENU", LeaveOwnedHotel, LanSession.Instance != null);
            GUI.matrix = previous; GUI.depth = 0;
        }

        void LedgerPage(string title, string subtitle)
        {
            Fill(new Rect(0, 0, 1600, 900), new Color(.08f, .09f, .07f, .92f));
            Fill(new Rect(70, 42, 1460, 816), Paper);
            Border(new Rect(82, 54, 1436, 792), Brass);
            Fill(new Rect(110, 73, 1370, 5), Wine);
            Label(new Rect(110, 97, 1350, 58), title, Title);
            Label(new Rect(112, 160, 1360, 58), subtitle, Body, Muted);
        }

        void DrawMaintenance()
        {
            var settings = Session.Economy;
            var boiler = Session.Simulation.Boiler;
            LedgerPage("TONIGHT'S REPAIR, TOMORROW'S TROUBLE", "Day " + Session.Day + " is over. Maintenance is paid now; the boiler's condition carries into the next shift.");
            Label(new Rect(112, 229, 660, 40), "AVAILABLE CASH   $" + Session.Cash.ToString("F0"), Heading);
            Label(new Rect(840, 229, 636, 40), "BOILER   " + boiler.Condition.ToString("F0") + "%   /   " + (boiler.Failed ? "SHUT DOWN" : "RUNNING"), Heading, boiler.Failed ? Red : Teal);
            Meter(new Rect(112, 287, 1363, 10), boiler.Condition / 100, boiler.Condition < 50 ? Red : Teal);
            MaintenanceCard(112, "01   CHEAP PATCH", "$" + settings.CheapPatchCost,
                "Stops the current fault. Restores " + settings.CheapPatchCondition.ToString("F0") + " condition points.\n\nOld wear stays in the system. Heavy demand can bring the fault back.",
                Mathf.Min(100, boiler.Condition + settings.CheapPatchCondition), MaintenanceChoice.CheapPatch, settings.CheapPatchCost);
            MaintenanceCard(578, "02   PROPER REPAIR", "$" + settings.ProperRepairCost,
                "Stops the current fault. Restores condition to at least " + settings.ProperRepairCondition.ToString("F0") + "%.\n\nCosts more tonight and leaves a stronger boiler for tomorrow's bookings.",
                Mathf.Max(boiler.Condition, settings.ProperRepairCondition), MaintenanceChoice.ProperRepair, settings.ProperRepairCost);
            MaintenanceCard(1044, "03   DEFER", "$0",
                "Keep the cash and the existing wear.\n\n" + (boiler.Failed ? "The boiler stays shut down until a cooperative restart or later paid maintenance. Guests still need warmth." : "The boiler starts tomorrow in its current condition. There is no free repair."),
                boiler.Condition, MaintenanceChoice.Defer, 0);
            Label(new Rect(112, 755, 1355, 50), "Between shifts, rooms approach the boiler's overnight temperature. The room cards show tomorrow's starting conditions.", Small, Muted);
            Label(new Rect(112, 806, 1355, 26), Session.LastMessage, Small, Wine);
        }

        void MaintenanceCard(float x, string title, string cost, string description, float condition, MaintenanceChoice choice, int price)
        {
            Fill(new Rect(x, 330, 433, 396), LightPaper);
            Border(new Rect(x, 330, 433, 396), Brass);
            Label(new Rect(x + 20, 354, 393, 35), title, Heading);
            Label(new Rect(x + 20, 404, 393, 58), cost, Title);
            Label(new Rect(x + 20, 477, 393, 146), description, Body);
            Label(new Rect(x + 20, 626, 393, 30), "Tomorrow: " + condition.ToString("F0") + "% condition", Small, Teal);
            bool affordable = Session.Cash >= price || choice == MaintenanceChoice.Defer;
            ButtonAt(new Rect(x + 20, 666, 393, 42), affordable ? "Choose & plan next day  ›" : "Insufficient cash", () => Session.ChooseMaintenance(owner, choice), affordable, false, true);
        }

        void DrawResults()
        {
            var reports = Session.Reports;
            var maintenance = Session.Simulation.MaintenanceDecisions;
            int gross = reports.Sum(r => r.Gross), refunds = reports.Sum(r => r.Compensation);
            int operating = reports.Sum(r => r.OperatingCost), repairCost = maintenance.Sum(m => m.Cost);
            float reputation = Session.Simulation.Economy.Reputation;
            string verdict = Session.Cash < 0 ? "The doors are open. The account is overdrawn." : reputation < 45 ? "The ledger survived. The reviews did not." : "Still open. Still standing. Somehow.";
            LedgerPage("THREE DAYS AT THE WORST HOTEL EVER", verdict);
            Label(new Rect(112, 240, 430, 60), "CASH   $" + Session.Cash.ToString("F0"), Title);
            Label(new Rect(590, 240, 410, 60), "REPUTATION   " + reputation.ToString("F0"), Title);
            Label(new Rect(1120, 240, 360, 60), "BOILER   " + Session.Simulation.Boiler.Condition.ToString("F0") + "%", Title);
            Label(new Rect(112, 315, 1360, 48), "Room revenue $" + gross + "   −   compensation $" + refunds + "   −   operations $" + operating + "   −   maintenance $" + repairCost, Body);
            Fill(new Rect(112, 385, 1360, 2), Brass);
            for (int i = 0; i < reports.Count; i++)
            {
                var day = reports[i];
                var decision = maintenance.FirstOrDefault(m => m.DayNumber == day.DayNumber);
                float y = 405 + i * 83;
                Label(new Rect(112, y, 140, 35), "DAY " + day.DayNumber, Heading);
                Label(new Rect(265, y, 740, 65), day.Receipts.Count + " guests  /  satisfaction " + day.AverageSatisfaction.ToString("F0") + "  /  net $" + day.Net + "\n" + (decision == null ? "Final shift" : "Maintenance: " + decision.Choice + "  −$" + decision.Cost), Body);
                Label(new Rect(1120, y, 360, 55), "Cash $" + day.Cash + "\nReputation " + day.Reputation.ToString("F0"), Body);
            }
            Label(new Rect(112, 678, 1360, 48), "More bookings brought more revenue and more load. Prices raised expectations; repair choices carried forward.", Body, Muted);
            ButtonAt(new Rect(112, 767, 540, 52), "START ANOTHER THREE DAYS", () => Session.RestartSession(owner), true, false, true);
            ButtonAt(new Rect(687, 767, 325, 52), "Walk the hotel", Close);
            Label(new Rect(1050, 768, 425, 51), "Prototype 0.3.1  /  GUEST SERVICE\nTHE WORST HOTEL EVER", Small, Muted);
        }
    }
}
