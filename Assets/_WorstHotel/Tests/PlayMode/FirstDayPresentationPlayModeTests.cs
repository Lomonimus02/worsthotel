using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator FirstDayWorldCluesUseActualRoomState()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            ManagementUI.Instance.Close(); var actor = bootstrap.Players[0];
            var board = Object.FindObjectsByType<RoomStatusBoard>(FindObjectsSortMode.None).Single(b => b.roomIds.Length == 10);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(board.labels[0].text, Does.Contain("101").And.Contain("NOT READY"));
            Assert.That(board.labels[1].text, Does.Contain("READY"));
            System.IO.Directory.CreateDirectory("docs/screenshots/cadence067");
            foreach (var shot in new[] {
                ("reception", new Vector3(-4.2f,.08f,.5f), new Vector3(-5.4f,2.75f,5.55f)),
                ("room101", new Vector3(-3.95f,.08f,9.2f), new Vector3(-5.95f,1.0f,10.15f)),
                ("laundry", new Vector3(11.65f,.08f,3.5f), new Vector3(11.65f,2.0f,6.2f)) })
            {
                // Labelled visual viewpoints, not a substitute for walking a complete hotel day.
                var pose = new GameObject("Readability viewpoint"); pose.transform.position = shot.Item2;
                actor.ResetToSpawn(pose.transform); Object.Destroy(pose); yield return WaitForGroundContact(actor);
                yield return AimAtKeyScenarioPoint(actor, padA, () => shot.Item3);
                var capture = VerificationOffscreenCapture.Capture(new[] { actor });
                System.IO.File.WriteAllBytes("docs/screenshots/cadence067/" + shot.Item1 + ".png", capture.EncodeToPNG());
                Object.Destroy(capture);
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
