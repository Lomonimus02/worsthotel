using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousPhysicalLedgerUpgradesPreserveRealLoadAndNeedActualBreakerReset()
        {
            bootstrap.ConfigureSolo(); UnityEngine.InputSystem.InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            // LABELLED INITIAL MODEL FIXTURE: enough cash to buy both categories, one quiet
            // checked-in guest104. No guest navigation or natural earnings claim is made.
            // Production consumer demand, upgrade amounts, costs and circuit timers are unchanged.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
            maintenanceFixtureEconomy.startingCash = maintenanceFixtureEconomy.boilerUpgradeCost + 2 * maintenanceFixtureEconomy.electricalUpgradeCost + 500;
            waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.activityDurationMin = waitScenarioLivingConfig.activityDurationMax = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var model = session.Simulation;
            Assert.That(model.ContinuousOperations, Is.True);
            var offer = model.BookingOffers.First(value => value.ArrivalDay == 1);
            Assert.That(session.AcceptBooking(0, offer.Id, 104, session.Economy.MinPrice).Success, Is.True);
            session.AdvanceTime(offer.ArrivalAt - model.Elapsed + .2f);
            var guest = model.Guests.Single();
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            Assert.That(model.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            yield return null; yield return null;
            int cash = model.Economy.Cash;
            var heaters = Object.FindObjectsByType<PortableHeater>(FindObjectsSortMode.None).OrderBy(item => item.heaterId).ToArray();
            Assert.That(heaters.Select(item => item.heaterId), Is.EqualTo(new[] { "portable-heater-1", "portable-heater-2" }));
            // LABELLED WORLD PLACEMENT: these two existing rigidbodies start in room104's
            // clear lane. Subsequent switches, ledger purchase, cabinet and reset use real input.
            // This fixture makes no shelf-to-room carrying claim.
            for (int index = 0; index < heaters.Length; index++)
            {
                heaters[index].Body.position = new Vector3(3.85f, .04f, index == 0 ? 18.6f : 16.0f);
                heaters[index].Body.rotation = Quaternion.identity;
                heaters[index].Body.linearVelocity = heaters[index].Body.angularVelocity = Vector3.zero;
            }
            yield return WaitForCondition(() => heaters.All(item => item.State != null && item.State.RoomId == 104 &&
                Vector3.Distance(item.transform.TransformPoint(item.placementCollider.center), item.placementCollider.bounds.center) < .05f), 3,
                "Both real heater bodies must settle in their actual room before aiming.");
            foreach (var heater in heaters)
            {
                yield return UseElectricalServiceControl(heater, heater.placementCollider.bounds.center);
                Assert.That(heater.State.SwitchedOn, Is.True);
            }
            var circuit = model.Electrical.Find("B"); var untouched = model.Electrical.Find("A");
            float originalCapacity = circuit.Capacity, untouchedCapacity = untouched.Capacity;
            float demand = circuit.ActualRequestedLoad;
            Assert.That(circuit.LoadOverride, Is.Null);
            Assert.That(demand, Is.GreaterThan(originalCapacity));
            Assert.That(demand, Is.LessThan(originalCapacity + model.Electrical.Settings.CapacityUpgradeAmount));
            var beforeConsumers = model.Electrical.Consumers.Where(item => item.CircuitId == "B")
                .ToDictionary(item => item.Id, item => item.RequestedLoad);
            Assert.That(beforeConsumers.Keys.Count(id => id.StartsWith("heater:")), Is.EqualTo(2));
            // Normal bounded hotel ticks reach sustained overload; the electrical trip is not forced.
            session.AdvanceTime(model.Electrical.Settings.TripSeconds + .25f);
            yield return null; yield return null;
            Assert.That(circuit.Tripped && !circuit.HasPower, Is.True);
            Assert.That(heaters.All(item => item.State.SwitchedOn && !item.State.Powered), Is.True);
            // Explicit boiler failure separately proves a capacity purchase is not a free repair.
            model.Boiler.ForceFailure();
            float condition = model.Boiler.Condition, rated = model.Boiler.RatedCapacity;
            float effective = model.Boiler.EffectiveCapacity, boilerDemand = model.Boiler.Load;
            float stress = model.Boiler.Stress01;
            yield return ReadPhysicalBook(HotelBook.Renovation);
            yield return ChooseBook("Install new burner");
            Assert.That(model.Boiler.CapacityUpgradePurchased, Is.True);
            Assert.That(model.Boiler.RatedCapacity, Is.EqualTo(rated * session.BoilerSettings.Capacity.CapacityUpgradeMultiplier).Within(.0001f));
            Assert.That(model.Boiler.EffectiveCapacity, Is.EqualTo(effective * session.BoilerSettings.Capacity.CapacityUpgradeMultiplier).Within(.0001f));
            Assert.That(model.Boiler.Condition, Is.EqualTo(condition));
            Assert.That(model.Boiler.Stress01, Is.EqualTo(stress));
            Assert.That(model.Boiler.Failed, Is.True);
            Assert.That(model.Boiler.HeatingOutput, Is.EqualTo(session.BoilerSettings.FailedHeatOutput));
            Assert.That(model.Boiler.Load, Is.EqualTo(boilerDemand).Within(.0001f));
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(session.Economy.BoilerUpgradeCost));
            float stressBeforePurchase = circuit.Stress01, stressAtPurchase = float.NaN;
            System.Action<ElectricalCircuit, string> observePurchase = (changed, reason) =>
            {
                if (ReferenceEquals(changed, circuit) && reason == "capacity upgrade installed")
                    stressAtPurchase = changed.Stress01;
            };
            model.Electrical.Changed += observePurchase;
            try { yield return ChooseBook("Upgrade B"); }
            finally { model.Electrical.Changed -= observePurchase; }
            Assert.That(model.Electrical.IsCapacityUpgraded("B"), Is.True);
            Assert.That(circuit.Capacity, Is.EqualTo(originalCapacity + model.Electrical.Settings.CapacityUpgradeAmount));
            Assert.That(untouched.Capacity, Is.EqualTo(untouchedCapacity));
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(demand).Within(.0001f));
            Assert.That(circuit.Reserve, Is.GreaterThan(0));
            Assert.That(float.IsNaN(stressAtPurchase), Is.False, "The real UI purchase must publish its installed state.");
            Assert.That(stressAtPurchase, Is.EqualTo(stressBeforePurchase), "The purchase preserves stress atomically; later frames may recover it normally.");
            Assert.That(circuit.Tripped, Is.True, "Capacity installation cannot reset the physical breaker.");
            Assert.That(circuit.ActualDeliveredLoad, Is.Zero);
            Assert.That(heaters.All(item => item.State.SwitchedOn && !item.State.Powered), Is.True);
            int totalCost = session.Economy.BoilerUpgradeCost + session.Economy.ElectricalUpgradeCost;
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - totalCost));
            Assert.That(session.Cash, Is.EqualTo(model.Economy.Cash));
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(totalCost));
            Assert.That(session.PurchaseBoilerUpgrade(1).Success, Is.False);
            Assert.That(session.PurchaseElectricalUpgrade(1, "B").Success, Is.False);
            yield return ChooseBook("Upgrade A");
            totalCost += session.Economy.ElectricalUpgradeCost;
            Assert.That(model.Electrical.IsCapacityUpgraded("A"), Is.True);
            Assert.That(model.Electrical.IsCapacityUpgraded("B"), Is.True);
            Assert.That(untouched.Capacity, Is.EqualTo(untouchedCapacity + model.Electrical.Settings.CapacityUpgradeAmount));
            Assert.That(session.PurchaseElectricalUpgrade(1, "A").Success, Is.False);
            Assert.That(session.PurchaseElectricalUpgrade(0, "unknown").Success, Is.False);
            Assert.That(model.Economy.Cash, Is.EqualTo(cash - totalCost));
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(totalCost));
            var panel = Object.FindAnyObjectByType<ElectricalPanelPresentation>();
            var view = panel.circuits.Single(item => item.circuitId == "B");
            var otherView = panel.circuits.Single(item => item.circuitId == "A");
            yield return WaitForCondition(() => view.readout.text.Contains("UPGRADED") && view.readout.text.Contains("TRIPPED") &&
                view.readout.text.Contains(" / " + circuit.Capacity.ToString("F2") + " u") &&
                otherView.readout.text.Contains("UPGRADED") &&
                otherView.readout.text.Contains(" / " + untouched.Capacity.ToString("F2") + " u"), 2,
                "The actual panel must show both purchased capacities and preserve the still-tripped branch.");
            var gauge = Object.FindAnyObjectByType<BoilerReadout>();
            Assert.That(gauge.capacityReadout.text, Does.Contain("STOP"));
            var progression = Object.FindAnyObjectByType<HotelProgressionPresentation>();
            Assert.That(progression.boilerBurner.activeSelf && progression.electricalA.activeSelf && progression.electricalB.activeSelf, Is.True);
            yield return ChooseBook("Close book");
            Vector3 coverAim = panel.cover.transform.TransformPoint(new Vector3(1.10f, 0, 0));
            // The cabinet now faces into the service wing, not along world +Z.
            Vector3 panelApproach = coverAim - panel.transform.forward * 1.45f; panelApproach.y = .08f;
            yield return PositionEmptyActorForLinen(0, panelApproach, coverAim);
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => coverAim);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(panel.cover));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false);
            yield return WaitForCondition(() => panel.cover.IsPassageOpen, 2, "The actual cover must clear the upgraded circuit's breaker.");
            var breaker = panel.GetComponentsInChildren<ElectricalBreakerControl>().Single(item => item.circuitId == "B");
            Vector3 breakerApproach = breaker.transform.position - panel.transform.forward * 1.45f; breakerApproach.y = .08f;
            yield return PositionEmptyActorForLinen(0, breakerApproach, breaker.transform.position);
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => breaker.transform.position);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(breaker));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(circuit.HasPower && !circuit.Tripped, Is.True);
            int trips = circuit.TripCount;
            session.AdvanceTime(model.Electrical.Settings.TripSeconds * 2 + 1);
            yield return null; yield return null;
            Assert.That(circuit.TripCount, Is.EqualTo(trips), "The same real demand fits after the permanent capacity improvement.");
            Assert.That(circuit.HasPower && !circuit.Warning, Is.True);
            Assert.That(circuit.ActualRequestedLoad, Is.EqualTo(demand).Within(.0001f));
            Assert.That(circuit.ActualDeliveredLoad, Is.EqualTo(demand).Within(.0001f));
            foreach (var consumer in model.Electrical.Consumers.Where(item => item.CircuitId == "B"))
                Assert.That(consumer.RequestedLoad, Is.EqualTo(beforeConsumers[consumer.Id]), "The improvement cannot secretly remove a real consumer.");
            Assert.That(heaters.All(item => item.State.SwitchedOn && item.State.Powered && item.State.EffectiveHeatOutput > 0), Is.True);
            Assert.That(untouched.Capacity, Is.EqualTo(untouchedCapacity + model.Electrical.Settings.CapacityUpgradeAmount));
            Assert.That(model.Boiler.Failed, Is.True);
            Assert.That(model.PeriodCapitalSpend, Is.EqualTo(totalCost));
            Assert.That(model.PeriodMaintenanceSpend, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
