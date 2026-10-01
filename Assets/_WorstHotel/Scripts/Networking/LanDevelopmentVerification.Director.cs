#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class LanDevelopmentVerification
    {
        [Serializable] sealed class DirectorMeasurement { public string visitor; public int cash, contract, situations; public float spent; public Vector3 target; }
        void PrepareDirectorFixture(GameSession current)
        {
            continuousFundingConfig = Instantiate(current.config);
            continuousFundingConfig.initiallyOpenRooms = 0; continuousFundingConfig.automaticBookings = false;
            continuousFundingConfig.openingHour = 16;
            continuousFundingConfig.director = current.config.director.Copy();
            continuousFundingConfig.director.QuietSeconds = 3;
            continuousFundingConfig.director.Deck = new[] { HotelDirectorSettings.DefaultDeck().Single(d => d.Kind == HotelSituationKind.Visitor) };
            current.config = continuousFundingConfig; current.NewGame();
            facts.Add("DIRECTOR FIXTURE: ordinary 480s continuous clock and economy; one manual guest at16:00, 3s quiet dwell, visitor-only deck. Physical NPC routes remain production. Empty remote staff approach is an explicit adapter.");
        }
        IEnumerator RunDirectorHost()
        {
            WriteStage("host-listening");
            yield return Until(() => lan.PeerConnected, 35, "remote director peer connected");
            CheckCameraAndAuthority(); ManagementUI.Instance.Close();
            var h = session.Simulation;
            Require(h.DebugSpawnGuest(GuestKind.Budget, 102).Success, "book actual ordinary host guest");
            yield return Until(() => h.Guests.Any(g => g.Agent.State == GuestAgentState.WaitingForCheckIn), 25, "guest physically reaches desk");
            var guest = h.Guests.Single();
            Require(h.Keys.PickUp(0, 102).Success && h.CheckIn(0, guest.GuestId).Success, "explicit staff model key handoff");
            session.RaiseChanged();
            PositionEmptyServiceActor(new Vector3(-4.4f, .08f, 4.45f), key.Body.worldCenterOfMass);
            WriteStage("director-key-ready");
            yield return Until(() => key.State.Location == RoomKeyLocation.HeldByPlayer && key.State.PlayerId == 1, 25, "actual remote grab owns key");
            Require(coop.Players[1].Interactor.HeldBody == key.Body, "physical key joint belongs to remote staff");
            Require(!h.Keys.PickUp(0, 101).Success, "second staff cannot steal remote held key");
            WriteStage("director-key-held");
            yield return Until(() => key.State.Location == RoomKeyLocation.Dropped, 20, "actual remote release drops key");
            yield return Until(() => h.Director.Visitors.Any(v => v.State == HotelVisitorState.WaitingAtReception), 60, "director visitor physically reaches reception");
            var v = h.Director.Visitors.Single();
            var body = FindObjectsByType<VisitorInteraction>(FindObjectsSortMode.None).Single(i => i.VisitorId == v.Id);
            var target = body.transform.position + Vector3.up * 1.1f;
            PositionEmptyServiceActor(new Vector3(body.transform.position.x, .08f, body.transform.position.z - 1.2f), target);
            var sample = new DirectorMeasurement { visitor = v.Id, cash = h.Economy.Cash, contract = h.ContractSequence, situations = h.Director.History.Count, spent = h.Director.SpentToday, target = target };
            WriteText("director-measurement.json", JsonUtility.ToJson(sample));
            WriteStage("director-visitor-ready");
            yield return Until(() => v.Allowed, 20, "remote E permits visitor through host physical interaction");
            yield return Until(() => v.State == HotelVisitorState.Visiting, 35, "permitted visitor physically enters real room");
            Require(h.Director.History.Count == 1, "director does not duplicate on peer");
            facts.Add("DirectorReplicated=True RemoteKeyOwnership=True RemoteVisitorDecision=True RealVisitorRoute=True EconomyAgreement=True");
            WriteStage("director-host-complete"); yield return Stage("client-complete", 15);
        }
        IEnumerator RunDirectorClient()
        {
            yield return Until(() => lan.PeerConnected && lan.HasSnapshot, 35, "client receives production model");
            CheckCameraAndAuthority(); ManagementUI.Instance.Close();
            var h = session.Simulation;
            Require(h.IsReadOnlyMirror && h.Director != null, "director exists on read-only mirror");
            float time = h.Elapsed; h.Tick(2); Require(h.Elapsed == time, "client never ticks director or clock");
            lastClientSequence = lan.AppliedModelSequence; lastClientClock = time; clockTracking = true;
            yield return Stage("director-key-ready", 35);
            yield return new WaitForSecondsRealtime(.5f);
            yield return ServicesAim(() => key.Body.worldCenterOfMass, "Pick up", true, key.GetComponent<BoxCollider>()); yield return TapGrab();
            yield return Until(() => MirrorKeyHeld(), 10, "remote key ownership is mirrored");
            yield return Stage("director-key-held", 10); yield return TapGrab();
            yield return Stage("director-visitor-ready", 65);
            var sample = JsonUtility.FromJson<DirectorMeasurement>(File.ReadAllText(Path.Combine(output, "director-measurement.json")));
            yield return Until(() => h.Director.FindVisitor(sample.visitor) != null && FindAnyObjectByType<LanWorldReplicator>().ReplicaGuestCount >= 2, 10, "ordinary guest and visitor are visible replicas");
            Require(h.Economy.Cash == sample.cash && h.ContractSequence == sample.contract && h.Director.History.Count == sample.situations && h.Director.SpentToday == sample.spent,
                "economy, contract, history and budget agree with measured host state");
            Require(!h.Director.DecideVisitor(1, sample.visitor, true).Success, "client model cannot author visitor decision");
            yield return new WaitForSecondsRealtime(.5f);
            yield return ServicesAim(() => sample.target, "Allow visit");
            // Host staff pose is already aimed at the actual visitor; real normal Use travels over NGO.
            Queue(new UnityEngine.InputSystem.LowLevel.GamepadState().WithButton(UnityEngine.InputSystem.LowLevel.GamepadButton.South));
            yield return new WaitForSecondsRealtime(.15f); Queue(default);
            yield return Stage("director-host-complete", 45);
            yield return Until(() => h.Director.FindVisitor(sample.visitor)?.State == HotelVisitorState.Visiting, 8, "room arrival is mirrored");
            Require(h.Director.History.Count == 1 && stableClockChecks > 0, "mirror did not independently select events");
            facts.Add("DirectorReplicated=True RemoteKeyOwnership=True RemoteVisitorDecision=True RealVisitorRoute=True EconomyAgreement=True ReadOnlyMirror=True");
            WriteStage("client-complete");
        }
    }
}
#endif
