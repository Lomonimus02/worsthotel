using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("OperatingSupplies"), Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator PhysicalSupplyBookOrdersOnceAndDeliveryAppearsOnExistingShelf()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var model = session.Simulation; var ui = ManagementUI.Instance;
            ui.Close();
            Assert.That(session.OrderBulbs(0, model.SupplyRevision).Success, Is.False, "Orders require the physical ledger.");
            int starting = model.Economy.Cash;
            yield return ReadPhysicalBook(HotelBook.Supplies);
            Assert.That(ui.DisplayedBookText, Does.Contain("CLEAN SETS ON SHELF").And.Contain("AT LAUNDRY"));
            int revision = model.SupplyRevision;
            yield return ChooseBook("Order 3 bulbs");
            Assert.That(model.Economy.Cash, Is.EqualTo(starting - 45));
            Assert.That(model.PeriodBulbSpend, Is.EqualTo(45));
            Assert.That(model.BulbsInTransit, Is.EqualTo(3));
            Assert.That(model.Services.BulbsAvailable, Is.EqualTo(3));
            Assert.That(session.OrderBulbs(0, revision).Success, Is.False, "Replaying the observed revision cannot charge again.");
            Assert.That(model.Economy.Cash, Is.EqualTo(starting - 45));
            float delivery = model.NextSupplyDeliveryAt;
            Assert.That(delivery, Is.EqualTo(model.Calendar.At(2, 6)));
            ui.Close();
            AdvanceContractFixtureTo(session, delivery + .2f);
            Assert.That(model.BulbsInTransit, Is.Zero);
            Assert.That(model.Services.BulbsAvailable, Is.EqualTo(6));
            Assert.That(model.LastReport.BulbSpend, Is.EqualTo(45));
            yield return null; yield return null;
            var items = Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None)
                .Where(item => item.State?.Kind == ServiceItemKind.ReplacementBulb && item.State.Location == ServiceItemLocation.OnShelf).ToArray();
            Assert.That(items.Length, Is.EqualTo(6), "All six purchased/initial stock slots have real scene objects.");
            foreach (var item in items)
                Assert.That(item.GetComponentsInChildren<Renderer>().Any(renderer => renderer.enabled), Is.True,
                    "Delivered stock must be visible on the authored shelf.");
            yield return ReadPhysicalBook(HotelBook.Accounts);
            yield return ChooseBook("Daily reports");
            Assert.That(model.LastReport.LaundrySpend, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
