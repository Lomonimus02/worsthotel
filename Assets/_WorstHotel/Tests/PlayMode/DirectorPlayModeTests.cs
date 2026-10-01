using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator PendingEnquiryIsDiscoverableOnTheActualReservationsBook()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            AdvanceSpecialFixtureTo(session.Simulation.Calendar.At(2, 10) + .2f);
            ManagementUI.Instance.Close(); yield return null; yield return null;
            var book = DiegeticBookInteraction.Find(HotelBook.Reservations);
            var paper = book.transform.Find("Reservation enquiry letter");
            Assert.That(paper && paper.gameObject.activeSelf, Is.True);
            Assert.That(paper.GetComponent<Renderer>().sharedMaterial.shader.isSupported, Is.True);
            var actor = bootstrap.Players[0]; var pose = new GameObject("Labelled reservations letter viewing approach");
            pose.transform.position = new Vector3(-5.8f, .08f, .75f);
            actor.ResetToSpawn(pose.transform); Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => paper.position);
            Assert.That(book.GetPrompt(actor.Interactor), Does.Contain("new enquiry"));
            System.IO.Directory.CreateDirectory("Logs/director070-captures");
            var capture = VerificationOffscreenCapture.Capture(new[] { actor });
            System.IO.File.WriteAllBytes("Logs/director070-captures/reservation-letter.png", capture.EncodeToPNG()); Object.Destroy(capture);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales"), Timeout(180000)]
        public IEnumerator DirectorVisitorWalksActualRouteUsesRoomAndLeavesAfterPhysicalConversation()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            // Focused scene fixture: one manually booked guest, broad contact delay and
            // a one-card deck. Only NPC route callbacks remain entirely production.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.initiallyOpenRooms = 0; waitScenarioSessionConfig.openingHour = 16;
            waitScenarioSessionConfig.automaticBookings = false;
            waitScenarioSessionConfig.director = session.config.director.Copy();
            waitScenarioSessionConfig.director.Enabled = false;
            waitScenarioSessionConfig.director.QuietSeconds = 3;
            waitScenarioSessionConfig.director.Deck = new[] { HotelDirectorSettings.DefaultDeck().Single(d => d.Kind == HotelSituationKind.Visitor) };
            serviceUIConfig = Object.Instantiate(session.config.services); serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 10000;
            waitScenarioSessionConfig.services = serviceUIConfig; session.config = waitScenarioSessionConfig;
            session.NewGame(); ManagementUI.Instance.Close(); yield return null; yield return null;
            var h = session.Simulation;
            Assert.That(h.DebugSpawnGuest(GuestKind.Budget, 102).Success, Is.True); session.RaiseChanged();
            yield return WaitForCondition(() => h.Guests.Any(g => g.RoomId == 102 && g.Agent.State == GuestAgentState.WaitingForCheckIn), 25, "Real guest walks to desk.");
            var guest = h.Guests.Single(g => g.RoomId == 102);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            yield return WaitForCondition(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 45, "Real guest enters room and reaches activity anchor.");
            h.Director.Settings.Enabled = true;
            yield return WaitForCondition(() => h.Director.Visitors.Count == 1, 30, "Quiet occupied hotel selects an eligible visitor premise.");
            var visitor = h.Director.Visitors.Single();
            yield return WaitForCondition(() => visitor.State == HotelVisitorState.WaitingAtReception, 25, "Visitor must actually enter and walk to reception.");
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == visitor.Id), Is.False);
            var target = Object.FindObjectsByType<VisitorInteraction>(FindObjectsSortMode.None).Single(v => v.VisitorId == visitor.Id);
            var actor = bootstrap.Players[0];
            var approach = new GameObject("Labelled staff approach for visitor conversation");
            approach.transform.position = target.transform.position + new Vector3(0, .07f, -1.25f);
            actor.ResetToSpawn(approach.transform); Object.Destroy(approach);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => target.transform.position + Vector3.up * 1.2f);
            Assert.That(actor.Interactor.Focused, Is.SameAs(target));
            System.IO.Directory.CreateDirectory("Logs/director070-captures");
            var capture = VerificationOffscreenCapture.Capture(new[] { actor });
            System.IO.File.WriteAllBytes("Logs/director070-captures/visitor-reception.png", capture.EncodeToPNG()); Object.Destroy(capture);
            yield return DiegeticPress(GamepadButton.South);
            Assert.That(visitor.Allowed, Is.True, "Normal player input allows the actual visitor.");
            yield return WaitForCondition(() => visitor.State == HotelVisitorState.Visiting, 50, "Visitor physically reaches the occupied room.");
            Assert.That(AuthoredGuestRoute.IsOnRoomSide(target.transform.position, Object.FindAnyObjectByType<GuestPresentation>().roomMarkers.Single(r => r.roomId == 102)), Is.True);
            var pose = Object.FindAnyObjectByType<GuestPresentation>().CaptureLanGuests().Single(g => g.id == visitor.Id);
            Assert.That(Vector3.Distance(pose.position, target.transform.position), Is.LessThan(.01f), "LAN carries the physical visitor position.");
            Assert.That(h.Director.History.Count, Is.EqualTo(1), "No follow-up spam during the same opportunity.");
            // Staff approach adapter, then ordinary aimed Q input at the physical visitor.
            approach = new GameObject("Labelled room conversation approach");
            approach.transform.position = target.transform.position + new Vector3(0, .07f, -1.2f);
            actor.ResetToSpawn(approach.transform); Object.Destroy(approach);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => target.transform.position + Vector3.up * 1.2f);
            yield return DiegeticPress(GamepadButton.West);
            Assert.That(visitor.State, Is.EqualTo(HotelVisitorState.Leaving));
            yield return WaitForCondition(() => visitor.State == HotelVisitorState.Left, 45, "Dismissed visitor must physically reach the exit.");
            Assert.That(h.Electrical.Consumers.Any(c => c.Id == visitor.Id), Is.False);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
