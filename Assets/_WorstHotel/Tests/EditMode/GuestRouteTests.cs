using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WorstHotel.Tests
{
    public sealed class GuestRouteTests
    {
        private const string ScenePath = "Assets/_WorstHotel/Scenes/PrototypeHotel.unity";
        private Scene scene;
        private Scene previousActive;
        private bool openedByTest;

        [OneTimeSetUp]
        public void OpenGeneratedScene()
        {
            Assert.That(File.Exists(ScenePath), Is.True, "Generate PrototypeHotel before testing its authored guest routes.");
            previousActive = SceneManager.GetActiveScene();
            scene = SceneManager.GetSceneByPath(ScenePath);
            openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (openedByTest) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        }

        [OneTimeTearDown]
        public void RestoreSceneSetup()
        {
            if (openedByTest && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        }

        [Test]
        public void EveryRoomHasExplicitActivityAndDoorAnchorsAndBedPoseIsAboveMattress()
        {
            var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            foreach (var room in presentation.roomMarkers)
            {
                foreach (var anchor in new[] { room.bedAnchor, room.bedApproach, room.shower, room.rest,
                    room.deskAnchor, room.unpackAnchor, room.phoneAnchor, room.roomPhoneAnchor, room.roomPhoneTarget,
                    room.radiatorAnchor, room.radiatorTarget, room.doorInsideAnchor, room.doorOutsideAnchor })
                    Assert.That(anchor, Is.Not.Null, "Missing activity anchor in room " + room.roomId);
                Assert.That(room.bedAnchor.position.y, Is.GreaterThan(1.1f));
                Assert.That(Mathf.Abs(Vector3.Dot(room.bedAnchor.up, Vector3.up)), Is.LessThan(.01f));
                Assert.That(AuthoredGuestRoute.IsOnRoomSide(room.doorInsideAnchor.position, room), Is.True);
                Assert.That(AuthoredGuestRoute.IsOnRoomSide(room.doorOutsideAnchor.position, room), Is.False);
                var sleep = AuthoredGuestRoute.Activity(room.roomTarget.position, room, GuestActivity.QuietRest, true);
                Assert.That(Vector3.Distance(sleep.Points.Last(), room.bedApproach.position), Is.LessThan(.001f));
                Assert.That(room.showerCurtain, Is.Not.Null);
            }
        }

        [Test]
        public void ActivityApproachesSkirtSolidRoomFurnitureAndKeepSleepOnTheFloorUntilPoseTransition()
        {
            var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            Physics.SyncTransforms();
            foreach (var room in presentation.roomMarkers)
            foreach (var start in new[] { room.roomTarget, room.rest, room.shower, room.bedApproach, room.deskAnchor, room.unpackAnchor, room.phoneAnchor,
                room.radiatorAnchor, room.roomPhoneAnchor })
            foreach (var target in new[] { GuestActivity.QuietRest, GuestActivity.Shower, GuestActivity.Work,
                GuestActivity.Unpack, GuestActivity.PhoneCall, GuestActivity.Pack, GuestActivity.WatchTV,
                GuestActivity.AdjustRadiator, GuestActivity.CallReception })
            foreach (bool sleeping in new[] { false, true })
            {
                if (sleeping && target != GuestActivity.QuietRest) continue;
                var route = AuthoredGuestRoute.Activity(start.position, room, target, sleeping);
                Vector3 from = start.position; from.y = .01f;
                foreach (var to in route.Points)
                {
                    Assert.That(to.y, Is.EqualTo(.01f).Within(.001f), "Walking must remain on the floor before settling into the bed.");
                    var delta = to - from;
                    if (delta.magnitude > .001f)
                    foreach (var hit in Physics.CapsuleCastAll(from + Vector3.up * .45f, from + Vector3.up * 1.60f,
                        .32f, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        var shape = hit.collider;
                        if (shape.attachedRigidbody || shape.GetComponentInParent<DoorInteractable>() ||
                            shape.GetComponentInParent<FirstPersonController>()) continue;
                        Assert.Fail("Room " + room.roomId + " route from " + start.name + " target " + target +
                            " intersects " + shape.name + " between " + from + " and " + to);
                    }
                    from = to;
                }
            }
        }

        [Test]
        public void TemporaryHotelTripsUseTheExteriorExitAndReturnViaLobbyBeforeCrossingRoomDoor()
        {
            var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            foreach (var room in presentation.roomMarkers)
            {
                var exit = AuthoredGuestRoute.GuestAway(room.rest.position, room, presentation.arrivalSpawn.position);
                Assert.That(exit.Door, Is.SameAs(room.door));
                Assert.That(exit.DoorCrossing, Is.GreaterThanOrEqualTo(0));
                Assert.That(exit.Points.Last().z, Is.LessThan(0), "Away begins outside the front door, never at a hallway parking point.");
                Assert.That(exit.Points.Last().x, Is.EqualTo(presentation.arrivalSpawn.position.x));
                var returned = AuthoredGuestRoute.ToRoom(exit.Points.Last(), room, false);
                Assert.That(returned.Points.Take(returned.DoorCrossing).Any(point => point.z < 1), Is.True);
                Assert.That(returned.DoorCrossing, Is.GreaterThanOrEqualTo(0));
                var final = returned.Points.Last();
                Assert.That(Vector2.Distance(new Vector2(final.x, final.z),
                    new Vector2(room.roomTarget.position.x, room.roomTarget.position.z)), Is.LessThan(.001f));
            }
        }

        [Test]
        public void ServiceReceptionRoutesKeepTheirDoorGateAndFinishAtDeskInsteadOfExterior()
        {
            var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            foreach (var room in presentation.roomMarkers)
            foreach (var reception in presentation.receptionPlaces)
            {
                var visit = AuthoredGuestRoute.ToServiceReception(room.radiatorAnchor.position, room, reception.position, true);
                Assert.That(visit.Door, Is.SameAs(room.door));
                Assert.That(visit.DoorCrossing, Is.GreaterThanOrEqualTo(0));
                Assert.That(visit.Points.All(point => point.z > presentation.arrivalSpawn.position.z + 1), Is.True,
                    "The incoming service lane remains inside the lobby, not at the exterior spawn.");
                Assert.That(Vector3.Distance(visit.Points.Last(), reception.position), Is.LessThan(.001f));
                var returned = AuthoredGuestRoute.ToRoom(visit.Points.Last(), room, false);
                Assert.That(returned.Door, Is.SameAs(room.door));
                Assert.That(returned.DoorCrossing, Is.GreaterThanOrEqualTo(0));
                Assert.That(AuthoredGuestRoute.IsOnRoomSide(returned.Points.Last(), room), Is.True);
            }
        }

        [Test]
        public void ExpandedReceptionAndCorridorTrafficHasSeparateApproachesAndClearAuthoredSegments()
        {
            var presentation = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            Assert.That(presentation.roomMarkers.Select(room => room.roomId), Is.EquivalentTo(Enumerable.Range(101, 10)));
            Assert.That(presentation.receptionPlaces.Length, Is.EqualTo(6), "Queue capacity is not hotel capacity.");
            Physics.SyncTransforms();
            foreach (var reception in presentation.receptionPlaces)
            {
                var arrival = AuthoredGuestRoute.Arrival(reception.position);
                Assert.That(arrival.Points[1].z, Is.LessThan(0));
                foreach (var room in presentation.roomMarkers)
                {
                    var entry = AuthoredGuestRoute.ToRoom(reception.position, room, false);
                    var exit = AuthoredGuestRoute.Exit(room.roomTarget.position, room, presentation.arrivalSpawn.position, true);
                    Assert.That(entry.Points.Any(point => point.x == .55f && point.z >= 6), Is.True);
                    Assert.That(exit.Points.Any(point => point.x == -.55f && point.z >= 6), Is.True);
                    AssertClearPublicSegments(presentation.arrivalSpawn.position, arrival);
                    AssertClearPublicSegments(reception.position, entry);
                    AssertClearPublicSegments(room.roomTarget.position, exit);
                }
            }
        }

        static void AssertClearPublicSegments(Vector3 from, AuthoredGuestRoute route)
        {
            from.y = .01f;
            foreach (var to in route.Points)
            {
                var delta = to - from;
                if (delta.magnitude > .001f)
                    foreach (var hit in Physics.CapsuleCastAll(from + Vector3.up * .42f, from + Vector3.up * 1.65f,
                        .32f, delta.normalized, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    {
                        var shape = hit.collider;
                        if (shape.attachedRigidbody || shape.GetComponentInParent<DoorInteractable>() ||
                            shape.GetComponentInParent<FirstPersonController>()) continue;
                        // This route describes the restored wing; its construction barrier is
                        // removed by HotelProgressionPresentation when restoration is purchased.
                        if (shape.transform.parent && shape.transform.parent.name == "North Wing locked construction barrier") continue;
                        Assert.Fail("Public route intersects " + shape.name + " between " + from + " and " + to);
                    }
                from = to;
            }
        }

        [Test]
        public void ExitAfterInterruptedRoomEntryAlignsWithAndGatesEachActualDoorwayDespiteStaleInsideFlag()
        {
            var presentation = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GuestPresentation>(true)).Single();
            Assert.That(presentation.roomMarkers.Length, Is.EqualTo(10));
            foreach (var room in presentation.roomMarkers)
            {
                var doorway = room.door.transform.position;
                float side = Mathf.Sign(doorway.x);
                // The guest crossed the threshold, but has not yet reached RoomTarget (door Z + .7).
                var current = new Vector3(room.roomTarget.position.x, 0.01f, doorway.z + 0.6f);
                var route = AuthoredGuestRoute.Exit(current, room, presentation.arrivalSpawn.position, false);
                Assert.That(AuthoredGuestRoute.IsOnRoomSide(current, room), Is.True);
                Assert.That(route.Door, Is.SameAs(room.door));
                Assert.That(route.DoorCrossing, Is.GreaterThanOrEqualTo(0), "Interrupted entry still needs the physical door: " + room.roomId);
                int planeCrossings = 0;
                Vector3 from = current;
                for (int i = 0; i < route.Points.Count; i++)
                {
                    Vector3 to = route.Points[i];
                    bool startsInside = from.x * side > Mathf.Abs(doorway.x);
                    bool endsInside = to.x * side > Mathf.Abs(doorway.x);
                    if (startsInside != endsInside)
                    {
                        planeCrossings++;
                        Assert.That(i, Is.EqualTo(route.DoorCrossing), "Every real threshold crossing must wait for the hinged door: " + room.roomId);
                        Assert.That(from.z, Is.EqualTo(doorway.z).Within(0.001f));
                        Assert.That(to.z, Is.EqualTo(doorway.z).Within(0.001f), "Exit must cross at the clear door centre, not clip its frame.");
                    }
                    from = to;
                }
                Assert.That(planeCrossings, Is.EqualTo(1), "An exit must leave the room once without re-entering it.");
            }
        }
    }
}
