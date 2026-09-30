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
            yield return ReadPhysicalBook(HotelBook.Accounts);
            Assert.That(ui.BookOptionTitles, Does.Contain("Ownership contract"));
            yield return CaptureContractFrame("01-contract-book");
            ui.Close();

            int opening = model.Economy.Cash;
            session.AdvanceTime(model.NextReportAt - model.Elapsed + .5f);
            Assert.That(model.ReportSequence, Is.EqualTo(1));
            Assert.That(model.OwnershipLost, Is.False);
            Assert.That(model.LastReport.ContractPayment.PaidAmount, Is.EqualTo(250));
            Assert.That(model.Economy.Cash, Is.EqualTo(opening - session.Economy.DailyOperatingCost - 250));
            Assert.That(model.LastReport.Net, Is.EqualTo(-session.Economy.DailyOperatingCost));
            Assert.That(model.ContractDue, Is.EqualTo(275));
            yield return ReadPhysicalBook(HotelBook.Accounts);
            yield return ChooseBook("Daily reports");
            yield return CaptureContractFrame("02-paid-report-book");

            yield return ReadPhysicalBook(HotelBook.Renovation);
            int beforePurchase = model.Economy.Cash;
            yield return ChooseBook("Install new burner");
            Assert.That(model.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(model.Economy.Cash, Is.EqualTo(beforePurchase - session.Economy.BoilerUpgradeCost));
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(session.Economy.BoilerUpgradeCost));
            ui.Close();
            // No checkout has been physically served by this fixture. The real purchase spends the reserve.
            session.AdvanceTime(model.NextReportAt - model.Elapsed + .5f);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(model.OwnershipLost, Is.True);
            Assert.That(model.Running, Is.False);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Results));
            Assert.That(model.LastReport.ContractPayment.PaidAmount, Is.Zero);
            Assert.That(model.LastReport.ContractPayment.Shortfall, Is.GreaterThan(0));
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
            session.RestartSession(0);
            yield return null;
            Assert.That(session.Simulation, Is.Not.SameAs(model));
            Assert.That(session.Simulation.OwnershipLost, Is.False);
            Assert.That(session.Simulation.ContractDue, Is.EqualTo(250));
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(session.Economy.StartingCash));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));

            // In-process wire-frame application, not a two-PC/LAN playtest.
            session.PrepareLanReplica();
            var received = session.ApplyLanFrame(failedFrame);
            Assert.That(received.Success, Is.True, received.Message);
            Assert.That(session.Simulation.OwnershipLost, Is.True);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Results));
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(endedCash));
            Assert.That(session.ApplyLanFrame(failedFrame).Success, Is.False, "Duplicate failure packet must not debit again.");
            LogAssert.NoUnexpectedReceived();
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
