using System;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class LanProtocolTests
    {
        static LanCommand Valid() => new LanCommand
        { epoch = 41, sequence = 3, day = 1, phase = DayPhase.Planning, kind = LanCommandKind.Assign, roomId = 101, subject = "day1-guest1", amount = 180 };

        [TestCase(LanCommandKind.OrderLaundry)]
        [TestCase(LanCommandKind.OrderBulbs)]
        public void SupplyOrdersNeedExactRevisionAndCannotSupplyTheirOwnPrice(LanCommandKind kind)
        {
            var command = new LanCommand { epoch = 41, sequence = 3, day = 1, phase = DayPhase.Service,
                kind = kind, expectedSupplyRevision = 5 };
            var copy = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(command));
            Assert.That(LanProtocol.ValidCommand(copy, 41, 2, 2, DayPhase.Service, true), Is.True);
            Assert.That(copy.expectedSupplyRevision, Is.EqualTo(5));
            Assert.That(LanProtocol.ValidCommand(copy, 41, 3, 2, DayPhase.Service, true), Is.False);
            copy.amount = 1;
            Assert.That(LanProtocol.ValidCommand(copy, 41, 2, 2, DayPhase.Service, true), Is.False);
            copy.amount = 0; copy.expectedSupplyRevision = -1;
            Assert.That(LanProtocol.ValidCommand(copy, 41, 2, 2, DayPhase.Service, true), Is.False);
            copy.expectedSupplyRevision = 5; copy.kind = LanCommandKind.RequestQuiet;
            Assert.That(LanProtocol.ValidCommand(copy, 41, 2, 2, DayPhase.Service, true), Is.False);
        }

        [Test]
        public void CommandEnvelopeRejectsStaleCrossDayCrossPhaseUnknownAndOversizedIntents()
        {
            Assert.That(LanProtocol.ValidCommand(Valid(), 41, 2, 1, DayPhase.Planning), Is.True);
            var corruptions = new Action<LanCommand>[]
            {
                c => c.version++, c => c.version--, c => c.epoch = 40, c => c.sequence = 2, c => c.sequence = -1,
                c => c.day = 2, c => c.phase = DayPhase.Service, c => c.kind = (LanCommandKind)999,
                c => c.roomId = 999, c => c.subject = new string('x', 129), c => c.amount = -1, c => c.amount = 100001
            };
            foreach (var corrupt in corruptions)
            {
                var command = Valid(); corrupt(command);
                Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Planning), Is.False);
            }
            Assert.That(LanProtocol.ValidCommand(null, 41, 2, 1, DayPhase.Planning), Is.False);
            Assert.That(LanProtocol.ValidCommand(Valid(), 0, 2, 1, DayPhase.Planning), Is.False);
            Assert.That(typeof(LanCommand).GetField("actorId"), Is.Null,
                "Transport binds its authenticated sender; a payload must not select another player's identity.");
        }

        [Test]
        public void ContinuousCommandCanCrossMidnightButNotEpochPhaseOrFutureDate()
        {
            var command = Valid(); command.phase = DayPhase.Service; command.kind = LanCommandKind.AcceptBooking;
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 2, DayPhase.Service, true), Is.True,
                "A valid enquiry does not become stale just because midnight passed in transit.");
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 2, DayPhase.Service), Is.False,
                "Historical shifts retain their exact-day gate.");
            Assert.That(LanProtocol.ValidCommand(command, 42, 2, 2, DayPhase.Service, true), Is.False);
            Assert.That(LanProtocol.ValidCommand(command, 41, 3, 2, DayPhase.Service, true), Is.False);
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 2, DayPhase.Planning, true), Is.False);
            command.day = 3;
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 2, DayPhase.Service, true), Is.False);
        }

        [TestCase(LanCommandKind.CancelBooking)]
        [TestCase(LanCommandKind.SetBookingPrice)]
        public void ReservationEditsCarryAnExplicitRevisionThroughJson(LanCommandKind kind)
        {
            var command = Valid(); command.kind = kind; command.phase = DayPhase.Service;
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service, true), Is.False);
            command.expectedReservationRevision = 4;
            var decoded = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(command));
            Assert.That(decoded.expectedReservationRevision, Is.EqualTo(4));
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.True);
            decoded.kind = LanCommandKind.RequestQuiet;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False,
                "Unrelated commands cannot smuggle booking revision fields.");
        }

        [TestCase(LanCommandKind.CancelMove)]
        [TestCase(LanCommandKind.OfferCredit)]
        [TestCase(LanCommandKind.AcceptConsequences)]
        public void ContinuousDirectDecisionRequiresBoundedExactIntentExpectationWhileLegacyKeepsItsEnvelope(LanCommandKind kind)
        {
            var ordinary = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(Valid()));
            Assert.That(LanProtocol.ValidCommand(ordinary, 41, 2, 1, DayPhase.Planning), Is.True,
                "An ordinary command must survive Unity JSON's null-to-empty string normalization.");
            ordinary.expectedDirectIntentId = "";
            Assert.That(LanProtocol.ValidCommand(ordinary, 41, 2, 1, DayPhase.Planning), Is.True);
            var command = Valid(); command.kind = kind; command.phase = DayPhase.Service;
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service), Is.True);
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service, true), Is.False);
            command.expectedDirectIntentId = "guest/move/7/103"; command.expectedDirectIntentRevision = 1;
            var decoded = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(command));
            Assert.That(decoded.expectedDirectIntentId, Is.EqualTo(command.expectedDirectIntentId));
            Assert.That(decoded.expectedDirectIntentRevision, Is.EqualTo(1));
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.True);
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 1, DayPhase.Service), Is.False);
            decoded.expectedDirectIntentRevision = 0;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.expectedDirectIntentRevision = 1; decoded.expectedDirectIntentId = new string('x', 513);
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.expectedDirectIntentId = " ";
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.expectedDirectIntentId = command.expectedDirectIntentId; decoded.kind = LanCommandKind.RequestQuiet;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False,
                "Unrelated commands cannot carry another service's expectation.");
        }

        [Test]
        public void DirectIpAndInputRejectInvalidBoundaryValuesWithoutRequiringNetworkingRuntime()
        {
            foreach (var address in new[] { "127.0.0.1", "192.168.1.10" }) Assert.That(LanProtocol.ValidAddress(address), Is.True);
            foreach (var address in new[] { "", "0.0.0.0", "255.255.255.255", "224.0.0.1", "::1", "example.com", "999.1.1.1" })
                Assert.That(LanProtocol.ValidAddress(address), Is.False);
            Assert.That(new LanInputFrame { move = Vector2.one, lookDegrees = new Vector2(10, -10) }.IsFinite, Is.True);
            Assert.That(new LanInputFrame { move = new Vector2(float.NaN, 0) }.IsFinite, Is.False);
            Assert.That(new LanInputFrame { lookDegrees = new Vector2(0, float.PositiveInfinity) }.IsFinite, Is.False);
            Assert.That(new LanInputFrame { navigate = new Vector2(float.NegativeInfinity, 0) }.IsFinite, Is.False);
            Assert.That(typeof(LanInputFrame).GetField("deltaTime"), Is.Null,
                "The host supplies elapsed work time; input packets cannot complete a bed with an invented time delta.");
        }

        [TestCase(BoilerServiceKind.Basic)]
        [TestCase(BoilerServiceKind.Full)]
        public void BoilerSelectionCarriesCurrentRevisionButCompletedWorkCannotBeSubmittedOverTheWire(BoilerServiceKind kind)
        {
            var command = Valid(); command.phase = DayPhase.Service; command.kind = LanCommandKind.SelectBoilerService;
            command.roomId = 0; command.subject = null; command.amount = (int)kind;
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service, true), Is.False);
            command.expectedMaintenanceRevision = 7;
            var decoded = JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(command));
            Assert.That(decoded.expectedMaintenanceRevision, Is.EqualTo(7));
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.True);
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 1, DayPhase.Service), Is.False);
            decoded.amount = (int)BoilerServiceKind.None;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.amount = 99;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.amount = (int)kind; decoded.kind = LanCommandKind.BeginBoilerMaintenance;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
            decoded.expectedMaintenanceRevision = -1;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False,
                "Only the host's physical interaction can finish setup; the old instant-start command stays forbidden.");
            decoded.kind = LanCommandKind.RequestQuiet; decoded.expectedMaintenanceRevision = 7;
            Assert.That(LanProtocol.ValidCommand(decoded, 41, 2, 2, DayPhase.Service, true), Is.False);
        }

        [TestCase(LanCommandKind.AnswerServiceCall)]
        [TestCase(LanCommandKind.TalkServiceGuest)]
        [TestCase(LanCommandKind.DiscussRoomConcern)]
        public void NaturalContactCommandsAcceptBoundedCompositeResponseIds(LanCommandKind kind)
        {
            var command = Valid(); command.kind = kind; command.phase = DayPhase.Service;
            command.subject = new string('r', 512);
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service), Is.True);
            command.subject += "r";
            Assert.That(LanProtocol.ValidCommand(command, 41, 2, 1, DayPhase.Service), Is.False);
        }
    }
}
