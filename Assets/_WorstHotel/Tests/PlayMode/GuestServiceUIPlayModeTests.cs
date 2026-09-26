using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        GuestServiceConfig serviceUIConfig;

        [UnityTearDown]
        public IEnumerator DisposeServiceUIConfiguration()
        {
            if (serviceUIConfig) Object.Destroy(serviceUIConfig);
            serviceUIConfig = null;
            yield return null;
        }

        IEnumerator PrepareServiceGuestFixture()
        {
            var session = GameSession.Instance;
            // Explicit model arrival/key adapters isolate service input. Existing route tests
            // cover physical check-in; service actions below use real controller input.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0; // The tested requests are explicit scenario setup.
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            Assert.That(session.Simulation.Services, Is.Not.Null);
            var offer = session.Plan.Applications.First(o => o.Archetype.Kind == GuestKind.Business);
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int rate = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, 101, rate);
            session.CommitPlan(0);
            var guest = session.Simulation.Guests.Single();
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            session.RaiseChanged();
            yield return null; yield return null;
        }

        [UnityTest]
        public IEnumerator ServiceBoardControllerAcceptsBlanketWithoutCreatingOrDeliveringAnItem()
        {
            yield return PrepareServiceGuestFixture();
            var session = GameSession.Instance;
            var guest = session.Simulation.Guests.Single();
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            var item = session.Simulation.Services.Cases.Single(c => c.Active);
            int stock = session.Simulation.Services.BlanketsAvailable;
            int identities = session.Simulation.Services.Items.Count;
            // The board is a management surface; accepting here must never fulfill a delivery.
            ManagementUI.Instance.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            QueueUse(padA, true);
            yield return WaitForCondition(() => item.Status == ServiceStatus.InProgress, 2,
                "The shared visible/action collection must support controller acceptance without relying on an IMGUI repaint.");
            QueueUse(padA, false);
            Assert.That(session.Simulation.Services.BlanketsAvailable, Is.EqualTo(stock));
            Assert.That(session.Simulation.Services.Items.Count, Is.EqualTo(identities));
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator PhysicalPhoneControllerCallCanCancelThenCompleteExactlyOnce()
        {
            yield return PrepareServiceGuestFixture();
            var session = GameSession.Instance;
            var guest = session.Simulation.Guests.Single();
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.WakeUpCall).Success, Is.True);
            var item = session.Simulation.Services.Cases.Single(c => c.Active);
            Assert.That(session.RespondToService(0, item.Id, true).Success, Is.True);
            var promise = session.Simulation.Services.Promises.Single();
            Assert.That(session.CompleteWakeUpCall(0, promise.Id).Success, Is.False,
                "A management command without a physical phone interaction must not fulfill a call.");
            session.AdvanceToNextPromise();
            Assert.That(session.Simulation.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, Is.True);
            session.RaiseChanged();
            yield return null;
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            Assert.That(phone, Is.Not.Null);
            var collider = phone.GetComponent<Collider>();
            Assert.That(collider, Is.Not.Null);
            yield return FaceStation(bootstrap.Players[0], padA, phone, collider.bounds.center);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsWakePhoneOpen, 2, "The actual reception phone must open its call menu.");
            QueueUse(padA, false); yield return null; yield return null; yield return null;
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsWakeCallInProgress, 2, "Confirm starts a short call rather than instantly fulfilling it.");
            QueueUse(padA, false, true); yield return null; yield return null;
            QueueUse(padA, false); yield return new WaitForSecondsRealtime(1.3f);
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            Assert.That(promise.Status, Is.EqualTo(PromiseStatus.Accepted), "Cancelling the call must abort its delayed completion.");
            Assert.That(guest.Memory.PromisesKept, Is.Zero);
            QueueUse(padA, true);
            yield return WaitForCondition(() => ManagementUI.Instance.IsWakePhoneOpen, 2, "Reusing the real phone opens the remaining promise.");
            QueueUse(padA, false); yield return null; yield return null; yield return null;
            QueueUse(padA, true);
            yield return WaitForCondition(() => promise.Status == PromiseStatus.Completed, 3, "A completed phone conversation must fulfill the promise.");
            QueueUse(padA, false);
            Assert.That(guest.Memory.PromisesKept, Is.EqualTo(1));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            Assert.That(session.CompleteWakeUpCall(0, promise.Id).Success, Is.False);
            Assert.That(guest.Memory.PromisesKept, Is.EqualTo(1));
            ManagementUI.Instance.Close();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
