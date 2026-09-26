using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        HousekeepingConfig housekeepingScenarioConfig;

        void PrepareHousekeepingScenario(float makeBedSeconds)
        {
            var session = GameSession.Instance;
            Assert.That(session.config.housekeeping, Is.Not.Null);
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioLivingConfig = Object.Instantiate(session.config.living);
            housekeepingScenarioConfig = Object.Instantiate(session.config.housekeeping);
            housekeepingScenarioConfig.makeBedSeconds = makeBedSeconds;
            waitScenarioLivingConfig.firstArrivalSeconds = .2f;
            waitScenarioLivingConfig.arrivalJitterSeconds = 0;
            waitScenarioLivingConfig.firstActivityDelay = 1000;
            waitScenarioSessionConfig.living = waitScenarioLivingConfig;
            waitScenarioSessionConfig.housekeeping = housekeepingScenarioConfig;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
        }

        void BookOneHousekeepingGuest(int roomId)
        {
            var session = GameSession.Instance;
            var offer = session.Plan.Applications.First();
            int min = session.Economy.MinPrice, step = session.Economy.PriceStep;
            int price = min + Mathf.RoundToInt((offer.ReferencePrice - min) / (float)step) * step;
            session.Assign(0, offer.Id, roomId, price);
        }

        [UnityTearDown]
        public IEnumerator DestroyHousekeepingScenarioConfiguration()
        {
            if (housekeepingScenarioConfig != null) Object.Destroy(housekeepingScenarioConfig);
            housekeepingScenarioConfig = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator OldGuestPhysicallyVacatesAfterNextDayStartsBeforeOwnersCanRemoveDirtyLinen()
        {
            PrepareHousekeepingScenario(1.5f);
            var session = GameSession.Instance;
            BookOneHousekeepingGuest(105); session.CommitPlan(0);
            var simulation = session.Simulation; var guest = simulation.Guests.Single();
            // Explicit served-room setup. The existing body, real doorway and actual departure
            // acknowledgement remain the evidence under test across daily roster replacement.
            session.AdvanceTime(guest.Agent.ArrivalTime + .2f);
            Assert.That(session.ReportGuestReachedReception(guest.GuestId).Success, Is.True);
            Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
            Assert.That(session.ReportGuestReachedRoom(guest.GuestId).Success, Is.True);
            yield return null; yield return null;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            Assert.That(Object.FindObjectsByType<HousekeeperPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .All(worker => !worker.isActiveAndEnabled), Is.True, "The early hotel must have no automatic physical employee.");
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var visual), Is.True);
            var markers = presentation.roomMarkers.Single(room => room.roomId == 105);
            var roomState = session.Rooms.Single(room => room.Profile.Id == 105);
            Assert.That(AuthoredGuestRoute.IsOnRoomSide(visual.position, markers), Is.True);
            session.EndShift();
            Assert.That(roomState.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(roomState.DepartingGuestId, Is.EqualTo(guest.GuestId));
            string dirtyId = simulation.Housekeeping.Find(105).DirtyLinenId;
            Assert.That(simulation.PickUpLinen(0, dirtyId).Success, Is.False);
            presentation.enabled = false;
            Assert.That(visual.gameObject.activeInHierarchy, Is.False);
            session.ContinueAfterSettlement(0); session.ChooseMaintenance(0, MaintenanceChoice.Defer);
            BookOneHousekeepingGuest(102); session.CommitPlan(0);
            Assert.That(simulation.Guests.Any(current => current.GuestId == guest.GuestId), Is.False);
            yield return new WaitForSecondsRealtime(.35f);
            Assert.That(roomState.DepartingGuestId, Is.EqualTo(guest.GuestId));
            Assert.That(simulation.PickUpLinen(0, dirtyId).Success, Is.False, "Hiding an old guest cannot unlock their dirty linen.");
            presentation.enabled = true; yield return null;
            Assert.That(presentation.TryGetGuestTransform(guest.GuestId, out var retained), Is.True);
            Assert.That(retained, Is.SameAs(visual));
            bool crossed = false; Vector3 previous = visual.position;
            float deadline = Time.realtimeSinceStartup + 16;
            while (roomState.DepartingGuestId != null && Time.realtimeSinceStartup < deadline)
            {
                yield return null; Vector3 current = visual.position;
                if (previous.x < markers.door.transform.position.x && current.x >= markers.door.transform.position.x)
                {
                    crossed = true;
                    Assert.That(Mathf.Abs(current.z - markers.door.transform.position.z), Is.LessThan(.12f));
                    Assert.That(markers.door.IsPassageOpen, Is.True);
                }
                if (current.x < markers.door.transform.position.x + .50f)
                {
                    Assert.That(roomState.DepartingGuestId, Is.EqualTo(guest.GuestId));
                    Assert.That(simulation.PickUpLinen(0, dirtyId).Success, Is.False, "The complete body must clear the door first.");
                }
                previous = current;
            }
            Assert.That(crossed, Is.True); Assert.That(roomState.DepartingGuestId, Is.Null);
            Assert.That(roomState.Cleanliness, Is.EqualTo(Cleanliness.Dirty), "Vacancy does not prepare a room automatically.");
            var dirty = Object.FindObjectsByType<LinenBundleItem>(FindObjectsSortMode.None).Single(item => item.itemId == dirtyId);
            yield return PositionEmptyActorForLinen(0, new Vector3(-4.65f, .08f, dirty.sourceAnchor.position.z - .05f), dirty.sourceAnchor.position);
            yield return GrabPhysicalLinen(0, dirty);
            Assert.That(dirty.State.Location, Is.EqualTo(LinenLocation.HeldByPlayer));
            Assert.That(simulation.Housekeeping.Find(105).Step, Is.EqualTo(RoomPreparationStep.DeliverDirtyLinen));
            yield return WaitForCondition(() => !presentation.TryGetGuestTransform(guest.GuestId, out _), 25,
                "The previous day's body did not finish its actual exit after roster replacement.");
            Assert.That(session.LastMessage, Does.Not.Contain("Unknown guest"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator OwnersCarryDirtyLinenToHamperFetchFiniteCleanStockAndMakeBedBeforeKeyHandoff()
        {
            PrepareHousekeepingScenario(1.5f);
            var session = GameSession.Instance; var simulation = session.Simulation;
            // Dirty-room diagnostic setup isolates this full physical route. The preceding lifecycle
            // test proves ordinary served checkout produces the same dirty linen and departure guard.
            Assert.That(simulation.DebugMarkRoomDirty(101).Success, Is.True);
            BookOneHousekeepingGuest(101); session.CommitPlan(0);
            var guest = simulation.Guests.Single();
            yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 18, "Guest did not reach reception.");
            var recipient = Object.FindObjectsByType<GuestReceptionInteraction>(FindObjectsSortMode.None).Single(item => item.GuestId == guest.GuestId);
            var room = session.Rooms.Single(item => item.Profile.Id == 101);
            var bed = Object.FindObjectsByType<LinenBedInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 101);
            var task = simulation.Housekeeping.Find(101);
            var dirty = bed.dirtyBundle;
            var storage = Object.FindAnyObjectByType<LinenStorage>();
            var hamper = Object.FindAnyObjectByType<LaundryHamperInteraction>();
            Assert.That(storage.cleanStock.Length, Is.EqualTo(6));
            Assert.That(storage.cleanStock.All(item => item.State.Location == LinenLocation.OnShelf), Is.True);
            Assert.That(Object.FindObjectsByType<HousekeeperPresentation>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .All(worker => !worker.isActiveAndEnabled), Is.True);
            Assert.That(bed.madeBedPieces.All(piece => !piece.activeSelf), Is.True);
            Assert.That(task.RequiredSeconds, Is.InRange(1, 2));
            yield return PositionEmptyActorForLinen(0, new Vector3(-4.65f, .08f, 10.10f), dirty.sourceAnchor.position);
            float physicalStart = Time.realtimeSinceStartup;
            yield return GrabPhysicalLinen(0, dirty);
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.DeliverDirtyLinen));
            Assert.That(simulation.BeginMakeBed(0, 101, "clean:0").Success, Is.False);
            // The second owner can fetch the reserved room key while the first carries dirty linen.
            yield return TakeRoomKeyFromActualRack(1, 101);
            yield return CarryRackKeyToReceptionGuest(1, 101, recipient);
            QueueUse(padB, true); yield return null; yield return null;
            QueueUse(padB, false); yield return null; yield return null;
            Assert.That(guest.Agent.CheckedIn, Is.False, "The correct key cannot sell an unprepared room.");
            Assert.That(bootstrap.Players[1].Interactor.HeldBody, Is.SameAs(PhysicalKey(101).Body));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.SameAs(dirty.Body));

            yield return CarryLinenTo(0, dirty, new Vector3(-4.18f, 0, 10));
            yield return CarryLinenTo(0, dirty, new Vector3(-3.25f, 0, 10));
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            if (!door.IsPassageOpen)
            {
                var leaf = door.GetComponentsInChildren<Collider>().First(shape => shape.enabled && !shape.isTrigger);
                yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => leaf.bounds.center);
                Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(door));
                QueueUse(padA, true); yield return null; yield return null;
                QueueUse(padA, false);
                yield return WaitForCondition(() => door.IsPassageOpen, 3, "A player carrying linen must be able to open the actual door.");
            }
            yield return CarryLinenTo(0, dirty, new Vector3(-1.15f, 0, 10));
            Assert.That(dirty.Body.position.x, Is.GreaterThan(door.transform.position.x), "The dirty bundle itself must pass through the real doorway.");
            yield return CarryLinenTo(0, dirty, new Vector3(-.32f, 0, 10));
            yield return CarryLinenTo(0, dirty, new Vector3(-.32f, 0, 30.1f));
            yield return CarryLinenTo(0, dirty, new Vector3(-4.45f, 0, 30.0f));
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => new Vector3(-4.45f, .75f, 30.29f));
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(hamper));
            Assert.That(session.CanDepositLinen(bootstrap.Players[0].Interactor, hamper), Is.True);
            QueueUse(padA, true); yield return null; yield return null;
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(dirty.State.Location, Is.EqualTo(LinenLocation.InHamper));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
            Assert.That(dirty.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            Assert.That(task.Step, Is.EqualTo(RoomPreparationStep.NeedsCleanLinen));
            Assert.That(room.Cleanliness, Is.EqualTo(Cleanliness.Dirty));
            Assert.That(hamper.statusLabel.text, Does.Contain("1 bundles"));

            yield return CarryLinenTo(0, null, new Vector3(-3.36f, 0, 29.75f));
            var clean = storage.cleanStock.Single(item => item.itemId == "clean:0");
            Assert.That(Vector3.Distance(clean.Body.position, clean.sourceAnchor.position), Is.LessThan(.04f));
            yield return GrabPhysicalLinen(0, clean);
            Assert.That(clean.State.PlayerId, Is.EqualTo(0));
            Assert.That(storage.cleanStock.Count(item => item.State.Location == LinenLocation.OnShelf), Is.EqualTo(5));
            yield return CarryLinenTo(0, clean, new Vector3(-.32f, 0, 30.1f));
            yield return CarryLinenTo(0, clean, new Vector3(-.32f, 0, 10));
            yield return CarryLinenTo(0, clean, new Vector3(-1.15f, 0, 10));
            Assert.That(door.IsPassageOpen, Is.True);
            yield return CarryLinenTo(0, clean, new Vector3(-3.25f, 0, 10));
            Assert.That(clean.Body.position.x, Is.LessThan(door.transform.position.x), "The clean bundle must physically cross back into the room.");
            yield return CarryLinenTo(0, clean, new Vector3(-4.18f, 0, 10));
            yield return CarryLinenTo(0, clean, new Vector3(-4.65f, 0, 10.10f));
            yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => new Vector3(-5.56f, .84f, 10.15f));
            Assert.That(bootstrap.Players[0].Interactor.Focused, Is.SameAs(bed));
            Assert.That(session.CanMakeBed(bootstrap.Players[0].Interactor, bed), Is.True);
            QueueUse(padA, true);
            yield return new WaitForSecondsRealtime(.4f);
            Assert.That(bed.OwnerPlayerId, Is.EqualTo(0));
            Assert.That(task.ProgressSeconds, Is.GreaterThan(0).And.LessThan(task.RequiredSeconds));
            Assert.That(session.BeginLinenBed(1, bed).Success, Is.False,
                "A remote command from the owner standing at reception must not steal or finish this physical bed action.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(task.ProgressSeconds, Is.Zero);
            Assert.That(task.WorkingPlayerId, Is.Null);
            Assert.That(bed.OwnerPlayerId, Is.EqualTo(-1));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.SameAs(clean.Body));
            Assert.That(clean.State.Location, Is.EqualTo(LinenLocation.HeldByPlayer));
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(task.ProgressSeconds, Is.Zero, "Cancelled bed work cannot advance in the background.");
            QueueUse(padA, true);
            yield return WaitForCondition(() => room.Cleanliness == Cleanliness.Clean, task.RequiredSeconds + 1,
                "The real short contextual bed action did not fit the carried clean linen.");
            QueueUse(padA, false); yield return null; yield return null;
            Assert.That(clean.State.Location, Is.EqualTo(LinenLocation.Consumed));
            Assert.That(bootstrap.Players[0].Interactor.HeldBody, Is.Null);
            Assert.That(clean.Body.GetComponent<ConfigurableJoint>(), Is.Null);
            Assert.That(bed.madeBedPieces.All(piece => piece.activeSelf), Is.True);
            Assert.That(simulation.Housekeeping.Find(101), Is.Null);
            Assert.That(room.ReservedGuestId, Is.EqualTo(guest.GuestId));
            TestContext.WriteLine("Actual linen route plus concurrent reception and cancellation: " +
                (Time.realtimeSinceStartup - physicalStart).ToString("F2") + " real seconds; this automated route is not a human pacing/fun measurement.");
            yield return GiveHeldKeyByInstantUse(1, guest, 101);
            Assert.That(room.GuestId, Is.EqualTo(guest.GuestId));
            Assert.That(guest.Agent.CheckedIn, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            LogAssert.NoUnexpectedReceived();
        }
    }
}