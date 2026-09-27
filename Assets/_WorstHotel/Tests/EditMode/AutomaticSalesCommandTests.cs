using System;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class AutomaticSalesCommandTests
    {
        static LanCommand Policy() => new LanCommand { epoch = 821, sequence = 3, day = 1, phase = DayPhase.Service,
            kind = LanCommandKind.SetRoomSalesPolicy, roomId = 102, openForSale = true, amount = 240, expectedPolicyRevision = 7 };
        static LanCommand Reassign() => new LanCommand { epoch = 821, sequence = 3, day = 1, phase = DayPhase.Service,
            kind = LanCommandKind.ReassignBooking, roomId = 106, subject = "stay-2-1", expectedReservationRevision = 4 };
        static bool Valid(LanCommand command, bool auto = true) => LanProtocol.ValidCommand(command, 821, 2, 2, DayPhase.Service, true, auto);
        static LanCommand Wire(LanCommand command) => JsonUtility.FromJson<LanCommand>(JsonUtility.ToJson(command));

        [Test]
        public void PolicyJsonRetainsAtomicRateOpenAndRevisionWithoutUnrelatedDecisionFields()
        {
            var command = Wire(Policy());
            Assert.That(Valid(command), Is.True, "A previous-day policy command may cross midnight, while its explicit revision remains authoritative.");
            Assert.That(command.roomId, Is.EqualTo(102)); Assert.That(command.amount, Is.EqualTo(240));
            Assert.That(command.openForSale, Is.True); Assert.That(command.expectedPolicyRevision, Is.EqualTo(7));
            Assert.That(command.expectedReservationRevision, Is.EqualTo(-1));
            Assert.That(command.expectedMaintenanceRevision, Is.EqualTo(-1));
            Assert.That(command.expectedDirectIntentRevision, Is.EqualTo(-1));
            Assert.That(string.IsNullOrEmpty(command.expectedDirectIntentId), Is.True);
            Assert.That(Valid(command, false), Is.False);
            Assert.That(LanProtocol.ValidCommand(command, 821, 3, 2, DayPhase.Service, true, true), Is.False);
        }

        [Test]
        public void ReassignmentJsonRetainsContractRevisionAndCannotCarryRateOrPolicyEdits()
        {
            var command = Wire(Reassign());
            Assert.That(Valid(command), Is.True);
            Assert.That(command.subject, Is.EqualTo("stay-2-1")); Assert.That(command.roomId, Is.EqualTo(106));
            Assert.That(command.expectedReservationRevision, Is.EqualTo(4));
            Assert.That(command.expectedPolicyRevision, Is.EqualTo(-1)); Assert.That(command.openForSale, Is.False);
            Assert.That(command.amount, Is.Zero);
            foreach (Action<LanCommand> corrupt in new Action<LanCommand>[] {
                row => row.amount = 180, row => row.openForSale = true, row => row.expectedPolicyRevision = 1,
                row => row.expectedReservationRevision = 0, row => row.subject = "", row => row.roomId = 0 })
            { var bad = Reassign(); corrupt(bad); Assert.That(Valid(Wire(bad)), Is.False); }
        }

        [Test]
        public void PolicyCommandsRejectForeignFieldsMissingRevisionAndInvalidRoomBeforeModelMutation()
        {
            foreach (Action<LanCommand> corrupt in new Action<LanCommand>[] {
                row => row.subject = "stay-2-1", row => row.expectedPolicyRevision = -1,
                row => row.expectedPolicyRevision = 0, row => row.expectedReservationRevision = 2,
                row => row.expectedMaintenanceRevision = 1, row => row.expectedDirectIntentId = "private-concern",
                row => row.roomId = 0, row => row.roomId = 107, row => row.day = 3 })
            { var bad = Policy(); corrupt(bad); Assert.That(Valid(Wire(bad)), Is.False); }
        }

        [TestCase(LanCommandKind.AcceptBooking)] [TestCase(LanCommandKind.SetBookingPrice)]
        public void AutomaticHostRejectsObsoleteManualApprovalAndContractRepricing(LanCommandKind kind)
        {
            var command = new LanCommand { epoch = 821, sequence = 3, day = 1, phase = DayPhase.Service,
                kind = kind, roomId = 101, subject = "stay-2-1", amount = 180,
                expectedReservationRevision = kind == LanCommandKind.SetBookingPrice ? 1 : -1 };
            Assert.That(Valid(Wire(command), false), Is.True, "Explicit historical manual hosts retain their original protocol.");
            Assert.That(Valid(Wire(command)), Is.False);
        }

        [TestCase(LanCommandKind.RequestQuiet)] [TestCase(LanCommandKind.CancelBooking)] [TestCase(LanCommandKind.PurchaseBoilerUpgrade)]
        public void UnrelatedCommandsCannotSmuggleSalesState(LanCommandKind kind)
        {
            var command = new LanCommand { epoch = 821, sequence = 3, day = 1, phase = DayPhase.Service,
                kind = kind, subject = "stay-2-1", expectedReservationRevision = kind == LanCommandKind.CancelBooking ? 1 : -1 };
            Assert.That(Valid(Wire(command)), Is.True);
            command.openForSale = true; Assert.That(Valid(Wire(command)), Is.False);
            command.openForSale = false; command.expectedPolicyRevision = 1;
            Assert.That(Valid(Wire(command)), Is.False);
        }
    }
}
