using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousDiagnosticAdvanceDiscardsOnlyAnUnrepresentableFinalRemainder()
        {
            var session = GameSession.Instance;
            session.NewGame(); ManagementUI.Instance.Close();
            var model = session.Simulation;
            var clock = model.Clock;
            var rooms = session.Rooms;
            var roomIdentities = (RoomState[])rooms.Clone();
            Assert.That(model.ContinuousOperations && model.Running, Is.True);
            float step = 1f / session.Settings.TickRate;
            Assert.That(step, Is.EqualTo(.2f), "Exercise the production fixed-tick budget.");

            // One real session tick establishes small nonzero elapsed time. No field/reflection
            // clock setup or large timestamp is needed to reproduce the 1.2 - 6 * .2 remainder.
            session.AdvanceTime(step);
            float before = model.Elapsed;
            Assert.That(before, Is.GreaterThan(0));
            float expected = before + 1.2f;
            float ulp = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(expected) + 1) - expected;
            Assert.DoesNotThrow(() => session.AdvanceTime(1.2f));
            Assert.That(model.Elapsed, Is.EqualTo(expected).Within(ulp),
                "Discard only the unrepresentable tail; preserve the requested representable advancement.");

            float next = model.Elapsed + step;
            Assert.DoesNotThrow(() => session.AdvanceTime(step));
            Assert.That(model.Elapsed, Is.EqualTo(next), "A later normal positive advance must still work.");
            Assert.That(session.Simulation, Is.SameAs(model));
            Assert.That(model.Clock, Is.SameAs(clock));
            Assert.That(session.Rooms, Is.SameAs(rooms));
            for (int i = 0; i < rooms.Length; i++) Assert.That(rooms[i], Is.SameAs(roomIdentities[i]));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(model.Running, Is.True);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
