#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class LanDevelopmentVerification
    {
        GuestStay agencySource, agencyAffected;
        bool agencySustain;
        float agencyNextMaintain;

        void MaintainAgencyFixture()
        {
            if (!agencySustain || !session || !session.Simulation.Running || Time.realtimeSinceStartup < agencyNextMaintain) return;
            agencyNextMaintain = Time.realtimeSinceStartup + 2;
            // Explicit test setup: sustain one physical source and one in-room observer. These
            // normal developer commands still require the actor to walk to and stage its anchor.
            if (agencySource?.Agent.InAssignedRoom == true &&
                (agencySource.Agent.Activity != GuestActivity.LoudRoom || agencySource.Agent.NextActivityTime < session.Simulation.Elapsed + 5))
                session.Simulation.ForceActivity(agencySource.GuestId, GuestActivity.LoudRoom);
            if (agencyAffected?.Agent.InAssignedRoom == true &&
                (agencyAffected.Agent.Activity != GuestActivity.QuietRest || agencyAffected.Agent.NextActivityTime < session.Simulation.Elapsed + 5))
                session.Simulation.ForceActivity(agencyAffected.GuestId, GuestActivity.QuietRest);
            session.RaiseChanged();
        }

        IEnumerator RunAgencyHost()
        {
            Require(NetworkManager.Singleton && NetworkManager.Singleton.IsListening, "actual agency NGO host listens");
            WriteStage("host-listening");
            yield return Until(() => lan.PeerConnected, 35, "actual agency client connected");
            CheckCameraAndAuthority();
            var sourceOffer = session.Plan.Applications.First(a => (a.Archetype.Traits & GuestTraits.Noisy) != 0);
            var affectedOffer = session.Plan.Applications.First(a => a.Archetype.Kind == GuestKind.Business);
            int Price(BookingApplication offer) => session.Economy.MinPrice + Mathf.RoundToInt(
                (offer.ReferencePrice - session.Economy.MinPrice) / (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, sourceOffer.Id, 101, Price(sourceOffer));
            session.Assign(0, affectedOffer.Id, 103, Price(affectedOffer));
            session.CommitPlan(0);
            ManagementUI.Instance?.Close();
            Require(session.Phase == DayPhase.Service && session.Simulation.Guests.Count == 2, "agency host committed two actual bookings");
            agencySource = session.Simulation.Guests.Single(g => g.RoomId == 101);
            agencyAffected = session.Simulation.Guests.Single(g => g.RoomId == 103);
            facts.Add("Agency setup: host booked source101 / affected103, model key handoffs only; all reception/room arrivals use actual guest routes. No route-completion callbacks.");
            foreach (var guest in session.Simulation.Guests.OrderBy(g => g.Agent.ArrivalTime))
            {
                yield return Until(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 40,
                    "guest physically reaches reception " + guest.GuestId);
                Require(session.Simulation.Keys.PickUp(0, guest.RoomId).Success, "explicit model-key fixture obtains booked key");
                Require(session.Simulation.CheckIn(0, guest.GuestId).Success, "explicit model-key fixture hands key to waiting guest");
                session.RaiseChanged();
            }
            yield return Until(() => agencySource.Agent.InAssignedRoom && agencySource.Agent.ActivityStaged &&
                agencyAffected.Agent.InAssignedRoom && agencyAffected.Agent.ActivityStaged, 40, "both guests physically reach their room anchors");
            agencySustain = true;
            Require(session.Simulation.ForceActivity(agencySource.GuestId, GuestActivity.LoudRoom).Success, "diagnostic actual TV activity requested");
            Require(session.Simulation.ForceActivity(agencyAffected.GuestId, GuestActivity.QuietRest).Success, "diagnostic observer remains at rest anchor");
            session.RaiseChanged();
            yield return Until(() => AgencyCase()?.Stage >= SituationStage.Complaint, 30,
                "sustained staged TV reaches neighbour and creates one real complaint");
            var cause = AgencyCase();
            Require(cause.Cause?.SourceGuestId == agencySource.GuestId && cause.Cause.SourceRoomId == 101,
                "host complaint identifies the actual source guest and room");
            float original = session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise;
            Require(original > .4f && session.Simulation.Noise.Sources.Any(s => s.SourceGuestId == agencySource.GuestId), "real TV source active before remote response");
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            var pose = new GameObject("DIAGNOSTIC empty remote staff room-door approach");
            var position = door.transform.TransformPoint(new Vector3(0, 0, -1.7f)); position.y = .08f;
            var direction = door.transform.position - position; direction.y = 0;
            pose.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            coop.Players[1].ResetToSpawn(pose.transform); Destroy(pose);
            facts.Add("DIAGNOSTIC: only empty remote staff placed near closed source door; all knocking, menu choice, cancellation and entry permission are client pad through normal NGO.");
            WriteStage("agency-world-ready");
            yield return Stage("agency-client-cancelled", 20);
            yield return Until(() => !session.HasGuestConversation(1, agencySource.GuestId), 5, "cancel reached host and revoked first conversation grant");
            WriteStage("agency-cancel-verified");
            yield return Until(() => agencySource.Memory.PreviousNoiseWarnings == 1 && agencySource.Agent.QuietUntil > session.Simulation.Elapsed,
                15, "remote guest context applied real quiet request on host");
            Require(session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise <= original * session.Simulation.NoiseSettings.QuietSourceMultiplier + .001f,
                "host source intensity fell due to remote warning");
            Require(cause.History.Any(h => h.Reason.Contains("asked to keep it down")), "host cause history records remote source warning");
            WriteStage("agency-host-quiet");
            yield return Until(() => door.IsOpen && !session.HasGuestConversation(1, agencySource.GuestId), 18,
                "remote reopened conversation selected real permission entry and consumed grant");
            yield return Until(() => cause.Resolved && agencyAffected.Memory.ProblemsResolvedSuccessfully >= 1, 12,
                "source reduction naturally resolves neighbour situation and records result");
            Require(session.Simulation.Noise.Sources.Any(s => s.SourceGuestId == agencySource.GuestId && s.Active),
                "resolution occurred with quiet TV still physically running");
            facts.Add("RemoteGuestContext=True ActualNoiseReduced=True CausalMemoryReplicated=True HostWarningCount=1 NaturalRecovery=True PermissionDoorOpened=True");
            facts.Add("RemoteCommands=" + lan.AcceptedRemoteCommands + " SourceBefore=" + original.ToString("F3") +
                " SourceAfter=" + session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise.ToString("F3"));
            WriteStage("agency-host-verified");
            yield return Stage("client-complete", 12);
            agencySustain = false;
        }

        IEnumerator RunAgencyClient()
        {
            yield return Until(() => lan.PeerConnected && lan.HasSnapshot, 35, "agency client receives normal host model");
            CheckCameraAndAuthority();
            Require(session.IsLanReplica && session.Simulation.IsReadOnlyMirror, "agency client is a read-only mirror");
            Require(pad != null && pad.added && pad.enabled && pad.canRunInBackground, "agency fixture owns its background-capable pad");
            yield return Until(() => ReferenceEquals(coop.Players[1].Input.Gamepad, pad), 3, "agency client bound its own pad");
            lastClientSequence = lan.AppliedModelSequence; lastClientClock = session.Simulation.Elapsed; clockTracking = true;
            ManagementUI.Instance?.Close();
            yield return Stage("agency-world-ready", 90);
            yield return Until(() => session.Phase == DayPhase.Service && session.Simulation.Guests.Count == 2, 5, "client sees agency bookings");
            agencySource = session.Simulation.Guests.Single(g => g.RoomId == 101);
            agencyAffected = session.Simulation.Guests.Single(g => g.RoomId == 103);
            yield return Until(() => AgencyCase()?.Stage >= SituationStage.Complaint && agencyAffected.Perception.NoiseSources.Count > 0,
                5, "client receives actual cause and perception");
            var cause = AgencyCase();
            float original = session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise;
            Require(cause.Cause.SourceGuestId == agencySource.GuestId && cause.Cause.SourceRoomId == 101 &&
                agencyAffected.Perception.NoiseSources.Any(s => s.SourceGuestId == agencySource.GuestId), "wire agrees about source and affected perception");
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            var approach = door.transform.TransformPoint(new Vector3(0, 0, -1.7f));
            yield return Until(() => Horizontal(coop.Players[1].transform.position, approach) < .15f, 5, "host approach arrived through ordinary world replication");
            yield return AgencyAimDoor(door);
            yield return AgencyOpenConversation(door);
            if (capture) yield return Capture("client-agency-context");
            yield return TapButton(GamepadButton.East);
            yield return Until(() => !ManagementUI.Instance.IsOpen, 4, "client B cancels physical guest conversation");
            WriteStage("agency-client-cancelled");
            yield return Stage("agency-cancel-verified", 6);
            yield return AgencyOpenConversation(door);
            Require(ManagementUI.CanAskForQuiet(session.Simulation, agencySource), "reopened context still exposes real source response");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => agencySource.Memory.PreviousNoiseWarnings == 1 && agencySource.Agent.QuietUntil > session.Simulation.Elapsed,
                8, "quiet command returns through host memory and agent snapshot");
            yield return Stage("agency-host-quiet", 5);
            Require(session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise < original * .3f,
                "client receives lower real source intensity");
            Require(AgencyCase().History.Any(h => h.Reason.Contains("asked to keep it down")), "client receives causal warning history");
            if (capture) yield return Capture("client-agency-quiet");
            yield return TapButton(GamepadButton.East);
            yield return Until(() => !ManagementUI.Instance.IsOpen, 4, "quiet conversation closes normally");
            yield return new WaitForSecondsRealtime(.35f);
            yield return AgencyOpenConversation(door);
            Require(!ManagementUI.CanAskForQuiet(session.Simulation, agencySource), "quiet agreement removes repeated warning choice");
            // Source guest has no complaint: after quieting, first available contextual action is permission to enter.
            Require(!session.Simulation.Incidents.Items.Any(i => i.GuestId == agencySource.GuestId && GuestLabels.IsActionable(i)),
                "permission fixture source has no unrelated complaint changing its contextual choices");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => session.Rooms.Single(r => r.Profile.Id == 101).DoorState == RoomDoorState.Open &&
                !ManagementUI.Instance.IsOpen, 8, "remote permission opens replicated door and closes client panel");
            yield return Stage("agency-host-verified", 15);
            yield return Until(() => AgencyCase().Resolved && agencyAffected.Memory.ProblemsResolvedSuccessfully >= 1, 5,
                "client observes measured recovery and affected guest memory");
            float before = session.Simulation.Elapsed;
            session.Simulation.Tick(10);
            Require(Mathf.Abs(before - session.Simulation.Elapsed) < .00001f, "agency mirror cannot advance its own causal decisions");
            Require(stableClockChecks >= 10, "agency mirror clock stayed unchanged between snapshots");
            facts.Add("RemoteGuestContext=True ActualNoiseReduced=True CausalMemoryReplicated=True ContextCancelReopen=True RemotePermissionEntry=True NaturalRecovery=True");
            facts.Add("Cause=" + cause.Key + " SourceWarnings=" + agencySource.Memory.PreviousNoiseWarnings +
                " AffectedComplaints=" + agencyAffected.Memory.NumberOfComplaints + " Resolved=" + agencyAffected.Memory.ProblemsResolvedSuccessfully +
                " SnapshotOnlyClockChecks=" + stableClockChecks);
            Queue(default); WriteStage("client-complete");
        }

        HotelIncident AgencyCase() => agencyAffected == null || !session ? null : session.Simulation.Incidents.Items.FirstOrDefault(
            i => i.GuestId == agencyAffected.GuestId && i.Reason == IncidentReason.Noise && i.Cause?.SourceGuestId == agencySource?.GuestId);

        IEnumerator AgencyAimDoor(DoorInteractable door)
        {
            var actor = coop.Players[1];
            Vector3 target = door.transform.TransformPoint(new Vector3(0, 1.55f, 0));
            float deadline = Time.realtimeSinceStartup + 7;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 offset = target - actor.PlayerCamera.transform.position;
                float yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan2(offset.y, new Vector2(offset.x, offset.z).magnitude) * Mathf.Rad2Deg;
                float x = Mathf.DeltaAngle(actor.transform.eulerAngles.y, yaw);
                float y = Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x, pitch);
                if (Mathf.Abs(x) < 1.3f && Mathf.Abs(y) < 1.3f) break;
                Queue(new GamepadState { rightStick = new Vector2(LookAxis(x), -LookAxis(y)) }); yield return null;
            }
            Queue(default); yield return new WaitForSecondsRealtime(.3f);
            Require(Vector3.Angle(actor.PlayerCamera.transform.forward, target - actor.PlayerCamera.transform.position) < 4,
                "real network look aimed at source door collider");
            yield return Until(() => (actor.Interactor.ReplicaCaption ?? "").Contains("Knock"), 4,
                "host raycast exposes occupied source door to client");
        }

        IEnumerator AgencyOpenConversation(DoorInteractable door)
        {
            Require(!ManagementUI.Instance.IsOpen, "agency conversation starts from world input");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => (coop.Players[1].Interactor.ReplicaCaption ?? "").Contains("Guest answers"), 4,
                "remote first use produces a real knock and answer");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => ManagementUI.Instance.IsGuestContextOpen && ManagementUI.Instance.ContextGuestId == agencySource.GuestId,
                5, "host physical answer opens the remote contextual guest panel");
            Require(ManagementUI.Instance.Owner == 1, "only remote local actor owns guest context");
            Require(session.Rooms.Single(r => r.Profile.Id == 101).DoorState != RoomDoorState.Open, "conversation does not automatically bypass the room door");
            yield return null; yield return null;
        }
    }
}
#endif
