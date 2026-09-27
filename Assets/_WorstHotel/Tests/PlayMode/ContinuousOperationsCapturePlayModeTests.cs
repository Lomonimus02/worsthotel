using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    // Controller/state coverage only. Editor offscreen rendering omitted IMGUI; visual evidence
    // comes from the separate opt-in built-player operations UI verification.
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousOperationsControllerPagesReflectCurrentStatusAndDoNotRepeatCharges()
        {
            ManagementUI.Instance.Close(); bootstrap.ConfigureSolo();
            InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            // Explicit presentation fixture: spare cash permits showing the existing paid
            // controls; initial guest/key callbacks isolate UI from the already-tested routes.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
            maintenanceFixtureEconomy.startingCash = 4000; waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation;
            Assert.That(model.ContinuousOperations && bootstrap.IsSolo, Is.True);
            var today = model.BookingOffers.Where(offer => offer.ArrivalDay == 1).OrderBy(offer => offer.ArrivalAt).Take(2).ToArray();
            for (int index = 0; index < today.Length; index++)
            {
                Assert.That(session.AcceptBooking(0, today[index].Id, 101 + index, session.Economy.MinPrice).Success, Is.True);
                session.AdvanceTime(today[index].ArrivalAt - model.Elapsed + .1f);
                Assert.That(session.ReportGuestReachedReception(today[index].Id).Success, Is.True);
                Assert.That(CheckInWithModelKeyFixture(session, 0, today[index].Id).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(today[index].Id).Success, Is.True);
            }
            var tomorrow = model.BookingOffers.Where(offer => offer.ArrivalDay == 2).OrderBy(offer => offer.ArrivalAt).Take(2).ToArray();
            Assert.That(session.AcceptBooking(0, tomorrow[0].Id, 104, session.Economy.MinPrice).Success, Is.True);
            // These forced facts demonstrate labels, not natural wear or overload pacing.
            model.Boiler.SetCondition(45);
            Assert.That(model.Electrical.ForceTrip("B").Success, Is.True);
            Assert.That(model.DebugMarkRoomDirty(105).Success, Is.True);
            session.RaiseChanged(); yield return null; yield return null;
            var terminal = Object.FindAnyObjectByType<ReceptionTerminal>();
            yield return FaceStation(bootstrap.Players[0], padA, terminal, terminal.transform.position + Vector3.up * .35f);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsOperationsOpen, 2, "The actual reception interaction opens operations.");
            QueueUse(padA, false); yield return null; yield return null;
            var ui = ManagementUI.Instance;
            Assert.That(ui.DisplayedOperationsOverview, Does.Contain("2/6 occupied").And.Contain("3 vacant rooms ready")
                .And.Contain("105 · Needs linen").And.Contain("B: TRIPPED").And.Contain("Boiler: poor"));
            Assert.That(ui.DisplayedOperationsOverview, Does.Contain("OUT · 101").And.Contain("OUT · 102").And.Contain("IN · 104"));
            var departed = model.Guests.Single(guest => guest.GuestId == today[0].Id);
            Assert.That(model.DebugCheckoutGuest(departed.GuestId).Success, Is.True);
            session.AdvanceTime(.1f);
            Assert.That(departed.ReceiptPosted, Is.True);
            Assert.That(model.Guests.Contains(departed), Is.True, "The paid guest remains in retained history.");
            Assert.That(ui.DisplayedOperationsOverview, Does.Not.Contain("OUT · 101"));
            Assert.That(ui.DisplayedOperationsOverview, Does.Contain("OUT · 102"));
            yield return ChooseOperationsOption("Tomorrow's bookings");
            yield return ChooseOperationsOption(tomorrow[1].Application.GuestName + " · ");
            yield return ChooseOperationsOption("Room 106 · ");
            yield return ChooseOperationsOption("Forecast · ");
            Assert.That(ui.DisplayedForecastCircuitState, Does.Contain("TRIPPED · reset required"));
            Assert.That(ui.DisplayedBookingForecast.CircuitReserve, Is.GreaterThan(0), "Positive future reserve must not conceal actual loss of power.");
            yield return ChooseOperationsOption("Back to booking");
            yield return ChooseOperationsOption("Back to bookings");
            yield return ChooseOperationsOption("Back to operations");
            yield return ChooseOperationsOption("Boiler maintenance");
            yield return ChooseOperationsOption("Start proper maintenance");
            Assert.That(model.Boiler.MaintenanceInProgress, Is.True);
            Assert.That(model.PeriodMaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            yield return ChooseOperationsOption("Back to operations");
            Assert.That(ui.DisplayedOperationsOverview, Does.Contain("OFF · maintenance until"));
            yield return ChooseOperationsOption("Capacity upgrades");
            yield return ChooseOperationsOption("Upgrade circuit B");
            Assert.That(model.Electrical.UpgradedCircuitId, Is.EqualTo("B"));
            Assert.That(model.Electrical.Find("B").Tripped, Is.True);
            yield return ChooseOperationsOption("Back to operations");
            yield return ChooseOperationsOption("Daily reports");
            int cash = model.Economy.Cash;
            Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            session.AdvanceTime(model.NextReportAt - model.Elapsed + .1f);
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(session.Report.MaintenanceSpend, Is.EqualTo(session.Economy.ProperRepairCost));
            Assert.That(session.Report.CapitalSpend, Is.EqualTo(session.Economy.ElectricalUpgradeCost));
            cash = model.Economy.Cash;
            yield return ChooseOperationsOption("Operating report 1");
            Assert.That(model.Economy.Cash, Is.EqualTo(cash));
            Assert.That(model.Economy.Cash, Is.EqualTo(session.Report.Cash));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            ui.Close();
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            yield return FaceStation(bootstrap.Players[0], padA, phone, phone.GetComponent<Collider>().bounds.center);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ui.IsWakePhoneOpen, 2, "The actual telephone keeps its existing wake-call interface.");
            QueueUse(padA, false); yield return null; yield return null;
            ui.Close();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
