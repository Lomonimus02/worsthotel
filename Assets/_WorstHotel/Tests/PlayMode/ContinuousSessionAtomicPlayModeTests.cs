using System;
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
        public IEnumerator InvalidContinuousHostFramesAreAtomicAndDoNotConsumeTheNextSequence()
        {
            var session = GameSession.Instance;
            Assert.That(session.config.continuousOperations, Is.True);
            var offer = session.Simulation.BookingOffers.First(item => item.ArrivalDay == 2);
            Assert.That(session.AcceptBooking(0, offer.Id, 101, session.Economy.MinPrice).Success, Is.True);
            string firstJson = JsonUtility.ToJson(session.CaptureLanFrame(811, 1));
            string nextJson = JsonUtility.ToJson(session.CaptureLanFrame(811, 2));
            session.PrepareLanReplica();
            var initial = session.ApplyLanFrame(JsonUtility.FromJson<LanHotelFrame>(firstJson));
            Assert.That(initial.Success, Is.True, initial.Message);
            var mirror = session.Simulation;
            var rooms = session.Rooms;
            var plan = session.Plan;
            var firstRoom = rooms[0];
            string before = JsonUtility.ToJson(mirror.CaptureSnapshot(811, 1));
            string message = session.LastMessage;
            ManagementUI.Instance.Open(0);
            Assert.That(ManagementUI.Instance.IsOperationsOpen, Is.True);
            var corruptions = new Action<LanHotelFrame>[]
            {
                frame => frame.day++,
                frame => frame.model.Operations = null,
                frame => { frame.model.HasOperations = false; frame.model.Operations = null; },
                frame => frame.model.Operations.SecondsPerDay *= 2,
                frame => frame.model.Operations.Reservations[0].RoomId = 999
            };
            foreach (var corrupt in corruptions)
            {
                var invalid = JsonUtility.FromJson<LanHotelFrame>(nextJson);
                corrupt(invalid);
                var rejected = session.ApplyLanFrame(invalid);
                Assert.That(rejected.Success, Is.False, "Malformed calendar/header/reservation must fail validation.");
                Assert.That(session.Simulation, Is.SameAs(mirror));
                Assert.That(session.Rooms, Is.SameAs(rooms));
                Assert.That(session.Rooms[0], Is.SameAs(firstRoom));
                Assert.That(session.Plan, Is.SameAs(plan), "A rejected host frame must not replace the compatibility planning view.");
                Assert.That(JsonUtility.ToJson(mirror.CaptureSnapshot(811, 1)), Is.EqualTo(before),
                    "Rejected state cannot partially change clock, cash, reservations, rooms or other subsystems.");
                Assert.That(session.LastMessage, Is.EqualTo(message));
                Assert.That(session.Day, Is.EqualTo(1));
                Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
                Assert.That(ManagementUI.Instance.IsOperationsOpen && ManagementUI.Instance.Owner == 0, Is.True,
                    "A rejected packet cannot close or steal the client's local journal.");
            }
            var accepted = session.ApplyLanFrame(JsonUtility.FromJson<LanHotelFrame>(nextJson));
            Assert.That(accepted.Success, Is.True, accepted.Message);
            Assert.That(session.Simulation, Is.SameAs(mirror));
            Assert.That(mirror.FindReservation(offer.Id).Status, Is.EqualTo(ReservationStatus.Reserved));
            Assert.That(session.ApplyLanFrame(JsonUtility.FromJson<LanHotelFrame>(nextJson)).Success, Is.False,
                "Only the successful frame consumes its sequence.");
            ManagementUI.Instance.Close();
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
