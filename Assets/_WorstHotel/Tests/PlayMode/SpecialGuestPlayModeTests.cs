using System.Collections;
using System.Linq;
using System.Text;
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
        public IEnumerator SpecialGuestAuthoredWorldFitsExistingLanPacket()
        {
            // Tests suppress LanSession startup, so explicitly create its ordinary world serializer.
            var world = GameSession.Instance.gameObject.AddComponent<LanWorldReplicator>();
            yield return null;
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(world.Capture(52, 1)));
            TestContext.Out.WriteLine("Authored world decoded bytes: " + json.Length);
            Assert.That(json.Length, Is.LessThan(LanProtocol.MaxSnapshotBytes - 16384), "Reserve space for occupied rooms and guest names.");
            Assert.That(LanWorldPayload.TryDecode(LanWorldPayload.Encode(json, LanProtocol.MaxSnapshotBytes), LanProtocol.MaxSnapshotBytes, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(json));
            LogAssert.NoUnexpectedReceived();
        }

        void AdvanceSpecialFixtureTo(float target)
        {
            var session = GameSession.Instance;
            for (int guard = 0; target - session.Simulation.Elapsed > .01f && guard < 300; guard++)
                session.AdvanceTime(Mathf.Min(30, target - session.Simulation.Elapsed));
            Assert.That(session.Simulation.Elapsed, Is.EqualTo(target).Within(.21f), "Calendar setup may round within one ordinary 5 Hz tick.");
        }
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator SpecialGuestSceneEncounterUsesLedgerPhysicalEquipmentCartAndReplicaAppearance()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance;
            // Labelled pacing fixture: empty ordinary inventory and spare restoration funds.
            // Model tests retain ordinary automatic demand; this pass exercises the new scene objects.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            maintenanceFixtureEconomy = Object.Instantiate(session.config.economy);
            maintenanceFixtureEconomy.startingCash = 20000;
            waitScenarioSessionConfig.economy = maintenanceFixtureEconomy;
            waitScenarioSessionConfig.initiallyOpenRooms = 0;
            serviceUIConfig = Object.Instantiate(session.config.services); serviceUIConfig.eligibility = 0;
            serviceUIConfig.selfResponseObserveSeconds = serviceUIConfig.toleranceSeconds = 10000;
            waitScenarioSessionConfig.services = serviceUIConfig;
            session.config = waitScenarioSessionConfig;
            var presentation = Object.FindAnyObjectByType<GuestPresentation>();
            foreach (var kind in new[] { SpecialGuestKind.TouringMusician, SpecialGuestKind.Overpacker, SpecialGuestKind.NightOwl })
            {
                session.NewGame(); ManagementUI.Instance.Close();
                yield return null; yield return null;
                var model = session.Simulation;
                Assert.That(model.RestoreNorthWing(0).Success, Is.True);
                SpecialBookingEnquiry enquiry = null;
                for (int day = 2; day <= 6; day += 2)
                {
                    AdvanceSpecialFixtureTo(model.Calendar.At(day, 10) + .2f);
                    enquiry = model.SpecialEnquiries.Single(e => e.Status == SpecialOfferStatus.Pending);
                    if (enquiry.Definition.Kind == kind) break;
                    Assert.That(session.DecideSpecialBooking(0, enquiry.Offer.Id, 0, false, enquiry.Revision).Success, Is.True);
                }
                int room = kind == SpecialGuestKind.TouringMusician ? 108 : kind == SpecialGuestKind.Overpacker ? 105 : 106;
                yield return ReadPhysicalBook(HotelBook.Reservations);
                yield return ChooseBook("Special enquiries");
                if (!ManagementUI.Instance.BookOptionTitles.Any(t => t.StartsWith("Accept · room " + room)))
                    yield return ChooseBook("More rooms");
                yield return ChooseBook("Accept · room " + room);
                Assert.That(model.FindReservation(enquiry.Offer.Id)?.RoomId, Is.EqualTo(room));
                ManagementUI.Instance.Close();
                AdvanceSpecialFixtureTo(enquiry.Offer.ArrivalAt + .25f);
                var guest = model.Guests.Single(g => g.GuestId == enquiry.Offer.Id);
                yield return WaitForCondition(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 35, "Special guest must walk to reception.");
                yield return null;
                var bags = Object.FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Where(b => b.State?.GuestId == guest.GuestId).ToArray();
                Assert.That(bags.Length, Is.EqualTo(enquiry.Definition.Baggage.Count));
                Assert.That(bags.All(b => b.PlacementCollider.enabled && b.GetComponentsInChildren<Renderer>().Any(r => r.enabled)), Is.True);
                Assert.That(bags.Select(b => b.Body.position).Distinct().Count(), Is.EqualTo(bags.Length), "All baggage must have separate physical positions.");
                if (presentation.TryGetGuestTransform(guest.GuestId, out var person))
                {
                    yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => person.position + Vector3.up * .8f);
                    System.IO.Directory.CreateDirectory("docs/screenshots/strange061");
                    var capture = VerificationOffscreenCapture.Capture(new[] { bootstrap.Players[0] });
                    System.IO.File.WriteAllBytes("docs/screenshots/strange061/" + kind + ".png", capture.EncodeToPNG());
                    Object.Destroy(capture);
                }
                Assert.That(model.Services.OfferLuggage(0, guest.GuestId, false).Success, Is.True);
                yield return new WaitForSeconds(.25f);

                // A late join uses the same distinct outfit, without requiring a live Stay on the view.
                var pose = presentation.CaptureLanGuests().Single(g => g.id == guest.GuestId);
                Assert.That(pose.specialKind, Is.EqualTo(kind));
                var replicaRoot = new GameObject("Special guest replica fixture");
                var replica = presentation.CreateLanReplica(replicaRoot.transform, pose);
                Assert.That(replica.body.childCount, Is.GreaterThan(15));
                presentation.ReleaseLanReplica(pose.id, replica.root); Object.Destroy(replicaRoot);

                if (kind == SpecialGuestKind.TouringMusician)
                {
                    var instrument = bags.Single(b => b.State.Payload == LuggagePayload.InstrumentCase);
                    var amp = bags.Single(b => b.State.Payload == LuggagePayload.Amplifier);
                    Assert.That(instrument.Body.mass, Is.EqualTo(12)); Assert.That(amp.Body.mass, Is.EqualTo(10));
                    var cart = Object.FindAnyObjectByType<LuggageCart>(); var body = cart.GetComponent<Rigidbody>();
                    body.position = new Vector3(0, .05f, 8); body.rotation = Quaternion.identity;
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                    // Drop fixture above the real cart; attachment and transport are normal physics.
                    instrument.Body.position = body.position + new Vector3(0, 1, 0);
                    instrument.Body.rotation = Quaternion.identity;
                    instrument.Body.linearVelocity = instrument.Body.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    yield return WaitForCondition(() => instrument.OnCart, 8, "Heavy road case must secure to the normal cart.");
                    yield return ApproachCart(cart);
                    float before = body.position.z;
                    InputSystem.QueueStateEvent(padA, new GamepadState { leftStick = Vector2.up });
                    yield return new WaitForSeconds(1.5f);
                    QueueUse(padA, false); yield return null;
                    Assert.That(body.position.z - before, Is.GreaterThan(1)); Assert.That(instrument.OnCart, Is.True);
                    yield return AimAtKeyScenarioPoint(bootstrap.Players[0], padA, () => cart.handle.position);
                    yield return DiegeticPress(GamepadButton.South);
                    Assert.That(LuggageCart.IsGuiding(0), Is.False);
                    // End the transport fixture off the guest route; leaving a laden cart
                    // across the corridor would test obstacle avoidance rather than this stay.
                    var parkingOffset = new Vector3(4, 0, -8) - body.position;
                    body.position += parkingOffset; instrument.Body.position += parkingOffset;
                    body.linearVelocity = body.angularVelocity = Vector3.zero;
                    instrument.Body.linearVelocity = instrument.Body.angularVelocity = Vector3.zero;
                    Physics.SyncTransforms();
                    Assert.That(CheckInWithModelKeyFixture(session, 0, guest.GuestId).Success, Is.True);
                    yield return WaitForCondition(() => guest.Agent.InAssignedRoom, 65, "Musician must reach restored room 108 through the guest corridor.");
                    var zone = Object.FindObjectsByType<LuggageDeliveryZone>(FindObjectsSortMode.None).Single(z => z.roomId == room);
                    amp.Body.position = zone.transform.position + Vector3.up * .55f; amp.Body.rotation = Quaternion.identity;
                    amp.Body.linearVelocity = amp.Body.angularVelocity = Vector3.zero; Physics.SyncTransforms();
                    yield return WaitForCondition(() => amp.State.Location == ServiceItemLocation.Delivered, 8, "Actual amplifier body must settle in the room delivery zone.");
                    Assert.That(model.ForceActivity(guest.GuestId, GuestActivity.Rehearsal).Success, Is.True);
                    yield return WaitForCondition(() => model.Electrical.Consumers.Any(c => c.Id == "equipment:" + amp.ItemId && c.DeliveredLoad > 0), 18, "Staged rehearsal must use the delivered amplifier.");
                    Assert.That(model.Noise.Sources.Any(s => s.SourceEntityId == amp.ItemId), Is.True);
                    yield return WaitForCondition(() => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Any(s => s.clip && s.clip.name == "Original touring guitar rehearsal"), 2, "Rehearsal audio must follow the physical amplifier.");
                }
                var world = Object.FindAnyObjectByType<LanWorldReplicator>();
                if (world)
                {
                    byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(world.Capture(51, 1)));
                    Assert.That(json.Length, Is.LessThanOrEqualTo(LanProtocol.MaxSnapshotBytes), "Expanded equipment must fit the existing LAN world bound.");
                }
                TestContext.Out.WriteLine(kind + " physically arrived with " + bags.Length + " bags; accepted in ledger for room " + room);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
