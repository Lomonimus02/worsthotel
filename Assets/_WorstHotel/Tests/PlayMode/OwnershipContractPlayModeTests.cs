using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        // Reuses the existing physical-book fixture; no new runtime driver or automation framework.
        // Approach poses and bounded clock advances are explicit test adapters, not a human balance run.
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator OwnershipContractBookPaymentPurchaseAndTerminalNotice()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var model = session.Simulation; var ui = ManagementUI.Instance;
            Assert.That(model.ContractEnabled, Is.True);
            Assert.That(model.ContractDue, Is.EqualTo(250));
            Assert.That(model.FirstContractAt, Is.EqualTo(model.Calendar.At(2, 22)));
            Assert.That(model.FirstContractAt, Is.EqualTo(1140));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            Assert.That(ui.BookOptionTitles, Does.Contain("Ownership contract"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Due Day 2 · 22:00"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Next operating charge   $350"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Day 2 · 06:00"));
            Assert.That(ui.DisplayedBookText, Does.Contain("COVERED by current cash"));
            yield return CaptureContractFrame("01-contract-book");
            ui.Close();

            int opening = model.Economy.Cash;
            AdvanceContractFixtureTo(session, model.NextReportAt + .25f);
            Assert.That(model.ReportSequence, Is.EqualTo(1));
            Assert.That(model.ContractSequence, Is.Zero, "The first 06:00 report does not settle a contract.");
            Assert.That(model.LastContractPayment, Is.Null);
            Assert.That(model.LastReport.ContractPayment, Is.Null);
            Assert.That(model.OwnershipLost, Is.False);
            Assert.That(model.Economy.Cash, Is.EqualTo(opening - session.Economy.DailyOperatingCost));
            Assert.That(model.LastReport.Net, Is.EqualTo(-session.Economy.DailyOperatingCost));
            Assert.That(model.ContractDue, Is.EqualTo(250));
            var firstMorningReport = model.LastReport;
            var accountsTitle = DiegeticBookInteraction.Find(HotelBook.Accounts).GetComponentsInChildren<TextMesh>()
                .Single(t => t.name == "Printed book title");
            yield return null;
            Assert.That(accountsTitle.text, Is.EqualTo("ACCOUNTS\nNEW REPORT"));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            yield return ChooseBook("Daily reports");
            Assert.That(ui.DisplayedBookText, Does.Not.Contain("Cash after payment"));
            yield return CaptureContractFrame("01-morning-report-no-contract");
            ui.Close();

            AdvanceContractFixtureTo(session, model.FirstContractAt + .25f);
            Assert.That(model.ContractSequence, Is.EqualTo(1));
            Assert.That(model.ReportSequence, Is.EqualTo(1));
            Assert.That(model.LastReport, Is.SameAs(firstMorningReport));
            var receipt = model.LastContractPayment;
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.Period, Is.EqualTo(1));
            Assert.That(receipt.DueAt, Is.EqualTo(model.FirstContractAt));
            Assert.That(receipt.PaidAmount, Is.EqualTo(250));
            Assert.That(model.Economy.Cash, Is.EqualTo(opening - session.Economy.DailyOperatingCost - 250));
            Assert.That(model.ContractDue, Is.EqualTo(275));
            yield return null;
            Assert.That(accountsTitle.text, Is.EqualTo("ACCOUNTS\nNEW RECEIPT"));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            Assert.That(ui.BookOptionTitles, Does.Contain("Latest contract receipt"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Day 2 · 22:00"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Cash after payment   $1000"));
            yield return CaptureContractFrame("02-evening-contract-receipt");
            yield return null;
            Assert.That(accountsTitle.text, Is.EqualTo("ACCOUNTS"), "Reading the receipt clears only its own unread marker.");

            yield return ReadPhysicalBook(HotelBook.Renovation);
            int beforePurchase = model.Economy.Cash;
            yield return ChooseBook("Install new burner");
            Assert.That(model.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(model.Economy.Cash, Is.EqualTo(beforePurchase - session.Economy.BoilerUpgradeCost));
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(session.Economy.BoilerUpgradeCost));
            ui.Close();
            // Buy while the evening balance is $1000; the next morning charge leaves -$350.
            // No checkout has been physically served by this fixture.
            AdvanceContractFixtureTo(session, model.NextReportAt + .25f);
            Assert.That(model.OwnershipLost, Is.False, "A 06:00 operating charge is not a contract attempt.");
            Assert.That(model.ContractSequence, Is.EqualTo(1));
            Assert.That(model.ReportSequence, Is.EqualTo(2));
            Assert.That(model.LastReport.ContractPayment, Is.SameAs(receipt));
            Assert.That(model.LastReport.Cash, Is.EqualTo(-350));
            Assert.That(receipt.CashAfterPayment, Is.EqualTo(1000));
            Assert.That(model.PeriodContractPayment, Is.Null);
            var paidReport = model.LastReport;
            yield return null;
            Assert.That(accountsTitle.text, Is.EqualTo("ACCOUNTS\nNEW REPORT"));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            yield return ChooseBook("Daily reports");
            Assert.That(ui.DisplayedBookText, Does.Contain("Cash after payment   $1000"));
            Assert.That(ui.DisplayedBookText, Does.Contain("Cash at report   $-350"));
            yield return CaptureContractFrame("02-paid-report-book");
            ui.Close();

            float failureAt = model.NextContractAt;
            AdvanceContractFixtureTo(session, failureAt);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(model.OwnershipLost, Is.True);
            Assert.That(model.Running, Is.False);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Results));
            Assert.That(model.LastReport, Is.SameAs(paidReport), "The last regular report remains the earlier morning report.");
            Assert.That(model.ReportSequence, Is.EqualTo(2));
            Assert.That(model.ContractSequence, Is.EqualTo(2));
            Assert.That(model.LastContractPayment.Period, Is.EqualTo(2));
            Assert.That(model.LastContractPayment.DueAt, Is.EqualTo(failureAt));
            Assert.That(model.LastContractPayment.PaidAmount, Is.Zero);
            Assert.That(model.LastContractPayment.Shortfall, Is.EqualTo(625));
            Assert.That(model.OwnershipLossReport.ContractPayment, Is.SameAs(model.LastContractPayment));
            Assert.That(model.OwnershipLossReport.Cash, Is.EqualTo(model.Economy.Cash));
            Assert.That(session.Report, Is.SameAs(model.OwnershipLossReport));
            Assert.That(ui.IsOpen, Is.True);
            Assert.That(bootstrap.IsPaused, Is.True);
            yield return CaptureContractFrame("03-ownership-revoked");
            float endedAt = model.Elapsed; int endedCash = model.Economy.Cash;
            Vector3 endedPosition = bootstrap.Players[0].transform.position;
            ui.Close(); // Dismissing the notice must not reopen a lost run.
            InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
            yield return new WaitForSecondsRealtime(.2f);
            InputSystem.QueueStateEvent(padA, new GamepadState());
            session.AdvanceTime(60);
            Assert.That(model.Elapsed, Is.EqualTo(endedAt));
            Assert.That(model.Economy.Cash, Is.EqualTo(endedCash));
            Assert.That(Vector3.Distance(endedPosition, bootstrap.Players[0].transform.position), Is.LessThan(.02f));
            Assert.That(model.PurchaseElectricalUpgrade(0, "A").Success, Is.False);
            Assert.That(ui.IsOpen, Is.True);

            var failedFrame = session.CaptureLanFrame(902, 1);
            string failedJson = JsonUtility.ToJson(failedFrame);
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(failedJson), Is.LessThanOrEqualTo(LanProtocol.MaxSnapshotBytes),
                "The terminal notice must fit the existing model message bound.");
            failedFrame = JsonUtility.FromJson<LanHotelFrame>(failedJson);
            session.RestartSession(0);
            yield return null;
            Assert.That(session.Simulation, Is.Not.SameAs(model));
            Assert.That(session.Simulation.OwnershipLost, Is.False);
            Assert.That(session.Simulation.ContractDue, Is.EqualTo(250));
            Assert.That(session.Simulation.ContractSequence, Is.Zero);
            Assert.That(session.Simulation.LastContractPayment, Is.Null);
            Assert.That(session.Simulation.OwnershipLossReport, Is.Null);
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(session.Economy.StartingCash));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));

            // In-process wire-frame application, not a two-PC/LAN playtest.
            session.PrepareLanReplica();
            var received = session.ApplyLanFrame(failedFrame);
            Assert.That(received.Success, Is.True, received.Message);
            Assert.That(session.Simulation.OwnershipLost, Is.True);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Results));
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(endedCash));
            Assert.That(session.Report, Is.SameAs(session.Simulation.OwnershipLossReport));
            Assert.That(session.Report.ContractPayment.PaidAmount, Is.Zero);
            Assert.That(session.Simulation.LastReport.ContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(session.Simulation.LastContractPayment.DueAt, Is.EqualTo(failureAt));
            Assert.That(session.ApplyLanFrame(failedFrame).Success, Is.False, "Duplicate failure packet must not debit again.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator OwnershipContractCoverageUsesCurrentCash()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var model = session.Simulation;
            Assert.That(model.PurchaseInsulation(0).Success, Is.True);
            Assert.That(model.PurchaseElectricalUpgrade(0, "A").Success, Is.True);
            Assert.That(model.Economy.Cash, Is.EqualTo(350));
            Assert.That(model.Economy.Cash, Is.GreaterThanOrEqualTo(model.ContractDue));
            Assert.That(model.Economy.Cash - session.Economy.DailyOperatingCost, Is.LessThan(model.ContractDue));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            Assert.That(ManagementUI.Instance.DisplayedBookText, Does.Contain("COVERED by current cash"));
            Assert.That(ManagementUI.Instance.DisplayedBookText, Does.Contain("Next operating charge   $350"));
            LogAssert.NoUnexpectedReceived();
        }

        static void AdvanceContractFixtureTo(GameSession session, float target)
        {
            var model = session.Simulation;
            for (int i = 0; i < 8 && model.Running && model.Elapsed < target; i++)
            {
                float before = model.Elapsed;
                session.AdvanceTime(Mathf.Min(model.Operations.SecondsPerDay, Mathf.Max(1f / session.Settings.TickRate, target - before)));
                Assert.That(model.Elapsed, Is.GreaterThan(before), "The bounded contract fixture must advance its clock.");
            }
            Assert.That(model.Elapsed, Is.GreaterThanOrEqualTo(target), "Reach the requested deadline using one-day advances.");
        }

        IEnumerator CaptureContractFrame(string name)
        {
            for (int i = 0; i < 8; i++) yield return null;
            Directory.CreateDirectory("Logs/debt-pressure");
            var image = VerificationOffscreenCapture.Capture(bootstrap.Players.Where(p => p && p.PlayerCamera.enabled).ToArray());
            try
            {
                Assert.That(VerificationOffscreenCapture.LastOverlaySubmitted, Is.True);
                File.WriteAllBytes("Logs/debt-pressure/" + name + ".png", image.EncodeToPNG());
            }
            finally { Object.Destroy(image); }
        }
    }
}
