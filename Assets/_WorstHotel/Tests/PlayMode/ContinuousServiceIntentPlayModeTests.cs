using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        string BlanketPointRayDiagnostic(RoomBlanketDropOffInteraction point, ServiceSupplyItem item)
        {
            var actor = bootstrap.Players[0]; var camera = actor.PlayerCamera.transform;
            var collider = point.GetComponent<BoxCollider>();
            string Hits(Vector3 direction) => string.Join(" | ", Physics.RaycastAll(camera.position, direction,
                actor.Interactor.reach, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => !hit.collider.transform.IsChildOf(actor.transform) &&
                    (!actor.Interactor.HeldBody || hit.rigidbody != actor.Interactor.HeldBody))
                .OrderBy(hit => hit.distance).Take(6).Select(hit => hit.collider.name + " @" + hit.distance.ToString("F3") +
                    " hit=" + hit.point.ToString("F3") + " bounds=" + hit.collider.bounds.min.ToString("F3") + ".." +
                    hit.collider.bounds.max.ToString("F3") + " interactable=" + hit.collider.GetComponentInParent<HotelInteractable>()?.name));
            return "Exterior delivery ray: actor=" + actor.transform.position.ToString("F3") +
                " camera=" + camera.position.ToString("F3") + " forward=" + camera.forward.ToString("F3") +
                " reach=" + actor.Interactor.reach + " canAct=" + actor.Interactor.CanAct +
                " point=" + point.transform.position.ToString("F3") + " aim=" + collider.bounds.center.ToString("F3") +
                " targetBounds=" + collider.bounds.min.ToString("F3") + ".." + collider.bounds.max.ToString("F3") +
                " targetEnabled=" + collider.enabled + "/" + point.isActiveAndEnabled +
                " angle=" + Vector3.Angle(camera.forward, collider.bounds.center - camera.position).ToString("F3") +
                " body=" + item.Body.position.ToString("F3") + " bodyBounds=" + item.PlacementCollider.bounds.min.ToString("F3") +
                ".." + item.PlacementCollider.bounds.max.ToString("F3") + " held=" + actor.Interactor.HeldBody?.name +
                " actualRay=[" + Hits(camera.forward) + "] centreRay=[" + Hits((collider.bounds.center - camera.position).normalized) + "]";
        }

        IEnumerator PrepareContinuousIntentGuest(bool checkedIn)
        {
            var session = GameSession.Instance;
            // Labelled model guest setup: these tests measure staff input/physical parcels,
            // not guest navigation. Separate route tests retain real guest callbacks.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.continuousOperations = true;
            waitScenarioSessionConfig.hotelDaySeconds = 720;
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioLivingConfig.awayDurationMin = waitScenarioLivingConfig.awayDurationMax = 600;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            serviceUIConfig = Object.Instantiate(session.config.services);
            serviceUIConfig.eligibility = 0;
            serviceUIConfig.naturalCommunicationEnabled = true;
            serviceUIConfig.observationSeconds = .1f;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 1000;
            serviceUIConfig.replySeconds = 500;
            serviceUIConfig.directWaitSeconds = 180;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig; session.NewGame(); ManagementUI.Instance.Close();
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            var offer = session.Simulation.BookingOffers.First(value => value.ArrivalDay == 1);
            Assert.That(session.AcceptBooking(0, offer.Id, 106, session.Economy.MinPrice).Success, Is.True);
            session.AdvanceTime(offer.ArrivalAt - session.Simulation.Elapsed + .2f);
            var guest = session.Simulation.Guests.Single();
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            if (checkedIn)
            {
                Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            }
            else Assert.That(session.Simulation.DebugMarkRoomDirty(106).Success, Is.True);
            yield return null; yield return null;
        }

        IEnumerator PrepareAndCarryRequestedBlanket()
        {
            yield return PrepareContinuousIntentGuest(true);
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            Assert.That(session.DebugSetMildCold(guest.GuestId).Success, Is.True);
            session.AdvanceTime(.2f);
            Assert.That(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, Is.True);
            var request = model.Services.Cases.Single(value => value.Kind == ServiceKind.ExtraBlanket);
            Assert.That(model.Services.DropOffIntent(guest.GuestId), Is.Null, "Private cold cannot advertise a delivery agreement.");
            session.AdvanceTime(.2f);
            // Explicit model disclosure adapter; subsequent acceptance uses the real shared UI actions.
            var disclosed = model.DiscussRoomConcern(0, guest.GuestId, request.Response.Id);
            Assert.That(disclosed.Success, Is.True, disclosed.Message);
            Assert.That(GuestLabels.ServiceClue(request, model), Does.Contain("blanket outside"));
            var ui = ManagementUI.Instance; ui.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            Assert.That(ui.ServiceOptionTitles.First(), Does.Contain("blanket"));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ui.ServiceOptionTitles.First(), Does.Contain("leave a blanket"));
            QueueUse(padA, true);
            yield return WaitForCondition(() => request.Status == ServiceStatus.InProgress, 2, "Controller must agree to a delivery.");
            QueueUse(padA, false); ui.Close();
            Assert.That(model.ForceLeaveRoom(guest.GuestId).Success, Is.True);
            Assert.That(model.SignalGuestLeftRoom(guest.GuestId).Success, Is.True, "Labelled absent-guest adapter, not a physical travel assertion.");
            var item = PhysicalSupply("blanket:0");
            yield return PositionEmptyActorForLinen(0, new Vector3(item.SourceAnchor.position.x, .08f, 29.5f), item.SourceAnchor.position);
            yield return GrabServiceSupply(item);
            Assert.That(model.Services.BlanketsAvailable, Is.EqualTo(2));
            var points = Object.FindObjectsByType<RoomBlanketDropOffInteraction>(FindObjectsSortMode.None);
            Assert.That(points.Select(value => value.roomId).OrderBy(value => value), Is.EqualTo(new[] {101,102,103,104,105,106}));
            var point = points.Single(value => value.roomId == 106);
            Assert.That(session.DropOffBlanket(1, point).Success, Is.False, "Another actor cannot place the held body.");
            yield return CarryServiceSupply(item, new Vector3(.25f, 0, 29.5f));
            yield return CarryServiceSupply(item, new Vector3(.25f, 0, point.transform.position.z));
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => point.GetComponent<BoxCollider>().bounds.center);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(point), BlanketPointRayDiagnostic(point, item));
            Assert.That(point.CanInteract(bootstrap.Players[0].Interactor), Is.True);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(item.State.Location, Is.EqualTo(ServiceItemLocation.AwaitingReceipt));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
            Assert.That(Vector3.Distance(item.Body.position, point.deliveryAnchor.position), Is.LessThan(.04f));
            Assert.That(item.GetComponentsInChildren<Renderer>().All(value => value.enabled), Is.True);
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            Assert.That(guest.Memory.BlanketsDelivered, Is.Zero);
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            Assert.That(GuestLabels.ServiceProgress(request, model), Does.Contain("not yet received"));
            var bed = Object.FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(value => value.roomId == 106);
            Assert.That(bed.deliveredBlanket.activeSelf, Is.False);
            Assert.That(GameObject.Find("Door106").GetComponent<DoorInteractable>().IsOpen, Is.False,
                "Leaving a parcel cannot open the private room.");
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ActualExteriorBlanketCarryWaitsForAbsentGuestThenReceivesTheSameParcelOnce()
        {
            yield return PrepareAndCarryRequestedBlanket();
            var model = GameSession.Instance.Simulation; var guest = model.Guests.Single(); var item = PhysicalSupply("blanket:0");
            var intent = model.Services.DropOffIntent(guest.GuestId);
            int generation = item.State.Generation;
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.AwaitingReceipt));
            // The guest return is an explicit model adapter. The blanket above was physically
            // picked up, carried and left through normal input, never teleported by the fixture.
            Assert.That(model.ForceReturnRoom(guest.GuestId).Success, Is.True);
            Assert.That(model.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
            yield return WaitForCondition(() => item.State.Location == ServiceItemLocation.Delivered, 2, "An available owner must receive their parcel.");
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(intent.ReceivedAt, Is.GreaterThanOrEqualTo(intent.DeliveredAt));
            Assert.That(item.State.Generation, Is.EqualTo(generation));
            Assert.That(guest.BlanketComfortBonus, Is.GreaterThan(0));
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(guest.Memory.ServicesFulfilled, Is.EqualTo(1));
            var bed = Object.FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(value => value.roomId == 106);
            // Model receipt happens during Update; both physical presentation components consume
            // it in LateUpdate. Wait for those actual visuals, rather than observing half a frame.
            yield return WaitForCondition(() => item.GetComponentsInChildren<Renderer>().All(value => !value.enabled) &&
                bed.deliveredBlanket.activeSelf, 2, "Receipt must hide the exterior parcel and show the blanket on the occupied bed.");
            Assert.That(item.GetComponentsInChildren<Renderer>().All(value => !value.enabled), Is.True);
            Assert.That(bed.deliveredBlanket.activeSelf, Is.True);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(guest.Memory.BlanketsDelivered, Is.EqualTo(1));
            Assert.That(model.Services.BlanketsAvailable, Is.EqualTo(2));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator CancelledUnreceivedBlanketRemainsAtItsPointAndCanBePhysicallyReclaimedAndReturned()
        {
            yield return PrepareAndCarryRequestedBlanket();
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            var item = PhysicalSupply("blanket:0"); var intent = model.Services.DropOffIntent(guest.GuestId);
            Vector3 leftAt = item.Body.position; int generation = item.State.Generation;
            var ui = ManagementUI.Instance; ui.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ui.ServiceOptionTitles.First(), Does.Contain("Cancel blanket delivery"));
            QueueUse(padA, true);
            yield return WaitForCondition(() => intent.Status == ServiceIntentStatus.Cancelled, 2, "The existing board action must cancel the pending agreement.");
            QueueUse(padA, false); ui.Close(); yield return null; yield return null;
            Assert.That(item.State.Location, Is.EqualTo(ServiceItemLocation.Dropped));
            Assert.That(item.State.Generation, Is.EqualTo(generation + 1));
            Assert.That(item.State.GuestId, Is.Null);
            Assert.That(Vector3.Distance(item.Body.position, leftAt), Is.LessThan(.15f), "Cancellation cannot send a parcel back to storage.");
            Assert.That(guest.BlanketComfortBonus, Is.Zero);
            yield return GrabServiceSupply(item);
            yield return CarryServiceSupply(item, new Vector3(.25f, 0, 29.5f));
            yield return CarryServiceSupply(item, new Vector3(item.SourceAnchor.position.x, 0, 29.5f));
            var shelf = Object.FindObjectsByType<ServiceStockShelfInteraction>(FindObjectsSortMode.None).Single(value => value.kind == ServiceItemKind.Blanket);
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => shelf.stockLabel.transform.position);
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(shelf));
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(item.State.Location, Is.EqualTo(ServiceItemLocation.OnShelf));
            Assert.That(model.Services.BlanketsAvailable, Is.EqualTo(3));
            Assert.That(Vector3.Distance(item.Body.position, item.SourceAnchor.position), Is.LessThan(.04f));
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator DelayedPartnerCancellationCannotCancelANewerRoomMoveForTheSameGuest()
        {
            yield return PrepareContinuousIntentGuest(true);
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            Assert.That(session.MoveGuest(0, guest.GuestId, 101).Success, Is.True);
            var first = model.Services.DirectIntent(guest.GuestId);
            var delayed = new LanCommand { epoch = 99, sequence = 1, day = session.Day, phase = DayPhase.Service,
                kind = LanCommandKind.CancelMove, subject = guest.GuestId,
                expectedDirectIntentId = first.Id, expectedDirectIntentRevision = first.Revision };
            Assert.That(session.CancelGuestMove(0, guest.GuestId).Success, Is.True);
            Assert.That(session.MoveGuest(0, guest.GuestId, 103).Success, Is.True);
            var current = model.Services.DirectIntent(guest.GuestId);
            Assert.That(current.Id, Is.Not.EqualTo(delayed.expectedDirectIntentId));
            var decoded = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(delayed));
            Assert.That(LanProtocol.ValidCommand(decoded, 99, 0, session.Day, DayPhase.Service, true), Is.True,
                "A delayed but well-formed envelope reaches the authoritative semantic guard.");
            string before = JsonUtility.ToJson(model.CaptureSnapshot(99, 1));
            session.ExecuteLanCommand(1, decoded);
            Assert.That(session.LastMessage, Does.Contain("proposal has changed"));
            Assert.That(JsonUtility.ToJson(model.CaptureSnapshot(99, 1)), Is.EqualTo(before),
                "Rejected cancellation must preserve the newer intent, reservation and key states atomically.");
            Assert.That(guest.Agent.PendingMoveRoomId, Is.EqualTo(103));
            decoded.expectedDirectIntentId = current.Id; decoded.expectedDirectIntentRevision = current.Revision + 1;
            session.ExecuteLanCommand(1, decoded);
            Assert.That(model.Services.DirectIntent(guest.GuestId), Is.SameAs(current));
            Assert.That(current.Active, Is.True);
            decoded.expectedDirectIntentRevision = current.Revision;
            session.ExecuteLanCommand(1, decoded);
            Assert.That(guest.Agent.PendingMoveRoomId, Is.Null);
            Assert.That(model.Services.DirectIntent(guest.GuestId), Is.Null);
            Assert.That(current.Status, Is.EqualTo(ServiceIntentStatus.Cancelled));
            Assert.That(session.Rooms.Single(room => room.Profile.Id == 103).ReservedGuestId, Is.Null);
            Assert.That(guest.RoomId, Is.EqualTo(106));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ClosingServiceBoardPreservesDirectDecisionAndControllerAcceptanceReleasesOnlyTheWait()
        {
            yield return PrepareContinuousIntentGuest(false);
            var session = GameSession.Instance; var model = session.Simulation; var guest = model.Guests.Single();
            Assert.That(model.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage).Success, Is.True);
            var request = model.Services.Cases.Single(value => value.Kind == ServiceKind.LuggageStorage);
            // Explicit model front-desk disclosure isolates the persistent UI decision subject.
            Assert.That(model.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Reception).Success, Is.True);
            Assert.That(model.TalkToServiceGuest(0, guest.GuestId, request.Response.Id).Success, Is.True);
            var intent = model.Services.DirectIntent(guest.GuestId);
            Assert.That(intent, Is.Not.Null); float deadline = intent.Deadline;
            Assert.That(GuestLabels.ServiceProgress(request, model), Does.Contain(GuestLabels.HotelMoment(model, deadline)));
            var ui = ManagementUI.Instance; ui.OpenReceptionServiceBoard(0);
            yield return null; yield return null; yield return null;
            QueueUse(padA, false, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(ui.IsOpen, Is.False);
            Assert.That(model.Services.DirectIntent(guest.GuestId), Is.SameAs(intent));
            Assert.That(intent.Deadline, Is.EqualTo(deadline), "Closing a menu cannot restart the direct wait timer.");
            Assert.That(guest.Agent.DirectServiceIntentId, Is.EqualTo(intent.Id));
            ui.OpenReceptionServiceBoard(0); yield return null; yield return null; yield return null;
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            QueueUse(padA, true);
            yield return WaitForCondition(() => request.Status == ServiceStatus.InProgress, 2, "The controller records the luggage decision.");
            QueueUse(padA, false); ui.Close();
            Assert.That(model.Services.DirectIntent(guest.GuestId), Is.Null);
            Assert.That(guest.Agent.DirectServiceIntentId, Is.Null);
            Assert.That(intent.Status, Is.EqualTo(ServiceIntentStatus.Completed));
            Assert.That(model.Services.FindItem("luggage:" + guest.GuestId).Location, Is.EqualTo(ServiceItemLocation.OnShelf),
                "A decision does not carry or store the suitcase.");
            Assert.That(guest.Memory.ServicesFulfilled, Is.Zero);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
