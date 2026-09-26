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
        GuestStay serviceGuest;
        bool sustainServiceGuest;
        float nextServiceMaintain;
        ServiceSupplyItem serviceBlanket;
        float nextServiceNetworkSample;

        [Serializable]
        sealed class ServiceNetworkSample
        {
            public long sentWorldSequence, recordedUtcTicks;
            public float hotelTime;
            public Vector3 cameraAngles;
            public Vector2 inputLook;
            public int worldBytes, worldDecodedBytes, modelBytes;
        }

        void RecordServiceNetworkSample()
        {
            if (!host || !lan || !session || !coop || !coop.Players[1] || Time.realtimeSinceStartup < nextServiceNetworkSample) return;
            nextServiceNetworkSample = Time.realtimeSinceStartup + .5f;
            // Development-only observation. Never apply this side-channel data to gameplay.
            var sequenceField = typeof(LanSession).GetField("worldSequence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var sample = new ServiceNetworkSample
            {
                sentWorldSequence = sequenceField == null ? -1 : (long)sequenceField.GetValue(lan),
                recordedUtcTicks = DateTime.UtcNow.Ticks, hotelTime = session.Simulation.Elapsed,
                cameraAngles = coop.Players[1].PlayerCamera.transform.eulerAngles, inputLook = coop.Players[1].Input.Look,
                worldBytes = lan.LastWorldBytes, worldDecodedBytes = lan.LastWorldDecodedBytes, modelBytes = lan.LastModelBytes
            };
            WriteText("service-network-host.json", JsonUtility.ToJson(sample));
        }

        string ServiceNetworkDiagnostic(long appliedSequence)
        {
            try
            {
                string path = System.IO.Path.Combine(output, "service-network-host.json");
                if (!System.IO.File.Exists(path)) return "hostSample=unavailable";
                var sample = JsonUtility.FromJson<ServiceNetworkSample>(System.IO.File.ReadAllText(path));
                return "hostWorld=" + sample.sentWorldSequence + " clientWorld=" + appliedSequence +
                    " gap=" + (sample.sentWorldSequence - appliedSequence) +
                    " sampleAgeMs=" + TimeSpan.FromTicks(DateTime.UtcNow.Ticks - sample.recordedUtcTicks).TotalMilliseconds.ToString("F0") +
                    " hostHotelTime=" + sample.hotelTime.ToString("F2") + " clientHotelTime=" + session.Simulation.Elapsed.ToString("F2") +
                    " hostCamera=" + sample.cameraAngles.ToString("F2") + " hostLook=" + sample.inputLook.ToString("F3") +
                    " worldBytes=" + sample.worldBytes + " worldDecodedBytes=" + sample.worldDecodedBytes + " modelBytes=" + sample.modelBytes;
            }
            catch (System.IO.IOException) { return "hostSample=between atomic writes"; }
        }

        void MaintainServicesFixture()
        {
            RecordServiceNetworkSample();
            if (!sustainServiceGuest || !session || !session.Simulation.Running ||
                serviceGuest?.Agent.InAssignedRoom != true || Time.realtimeSinceStartup < nextServiceMaintain) return;
            nextServiceMaintain = Time.realtimeSinceStartup + 2;
            if (serviceGuest.Agent.ResponseActionId == null && (serviceGuest.Agent.Activity != GuestActivity.QuietRest ||
                serviceGuest.Agent.NextActivityTime < session.Simulation.Elapsed + 5))
                session.Simulation.ForceActivity(serviceGuest.GuestId, GuestActivity.QuietRest);
            if (serviceGuest.BlanketComfortBonus <= 0) session.Simulation.DebugSetMildCold(serviceGuest.GuestId);
        }

        IEnumerator RunServicesHost()
        {
            Require(NetworkManager.Singleton && NetworkManager.Singleton.IsListening, "actual service NGO host listens");
            WriteStage("host-listening");
            yield return Until(() => lan.PeerConnected, 35, "actual service client connects");
            CheckCameraAndAuthority();
            Require(session.Simulation.Services != null, "production guest services enabled");
            var offer = session.Plan.Applications.First(item => item.Archetype.Kind == GuestKind.Business);
            int price = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) /
                (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, offer.Id, 106, price); session.CommitPlan(0); ManagementUI.Instance?.Close();
            serviceGuest = session.Simulation.Guests.Single();
            facts.Add("SERVICE SETUP: one real Business booking106; explicitly labelled model key pickup/handoff at actual reception; all guest arrivals/anchors use production routes. No route-completion callbacks.");
            yield return Until(() => serviceGuest.Agent.State == GuestAgentState.WaitingForCheckIn, 45, "service guest physically reaches reception");
            Require(session.Simulation.Keys.PickUp(0, 106).Success, "model-key fixture retrieves room106 key");
            Require(session.Simulation.CheckIn(0, serviceGuest.GuestId).Success, "model-key fixture hands key to physically waiting guest");
            session.RaiseChanged();
            yield return Until(() => serviceGuest.Agent.InAssignedRoom && serviceGuest.Agent.ActivityStaged, 40, "service guest physically reaches room106");
            sustainServiceGuest = true;
            Require(session.Simulation.ForceActivity(serviceGuest.GuestId, GuestActivity.QuietRest).Success, "diagnostic rest preserves service access");
            yield return Until(() => serviceGuest.Agent.ActivityStaged, 10, "guest physically stages the prepared rest activity");
            Require(session.Simulation.DebugSetMildCold(serviceGuest.GuestId).Success, "diagnostic mild cold has an actual temperature cause");
            yield return Until(() => session.Simulation.Incidents.Items.Any(i => i.GuestId == serviceGuest.GuestId &&
                i.Active && i.Reason == IncidentReason.Temperature), 3, "actual cold is measured before diagnostic request creation");
            Require(session.Simulation.DebugForceService(serviceGuest.GuestId, ServiceKind.ExtraBlanket).Success, "diagnostic causal blanket request");
            Require(session.Simulation.BreakRoomLamp(105).Success, "diagnostic actual lamp105 fault");
            Require(session.Simulation.SetRadiatorSetting(0, 105, 2).Success, "diagnostic initial valve105 setting");
            var blanketCase = session.Simulation.Services.Cases.Single(item => item.GuestId == serviceGuest.GuestId && item.Kind == ServiceKind.ExtraBlanket);
            yield return ReceiveNaturalConcernHost(blanketCase, "natural-cold");
            serviceBlanket = FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Single(item => item.ItemId == "blanket:0");
            var target = FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 106);
            int initialStock = session.Simulation.Services.BlanketsAvailable;
            var door = GameObject.Find("Door106").GetComponent<DoorInteractable>();
            // Permission/knocking has a separate actual LAN case. Let the real door observe
            // its occupancy/privacy transition before preparing this labelled access fixture.
            yield return new WaitForFixedUpdate();
            Require(session.Simulation.RequestStaffRoomAccess(1, 106).Success, "model fixture receives the present guest's permission");
            Require(door.RequestGuestOpen(serviceGuest.GuestId), "authorized fixture opens the real guest doorway");
            yield return Until(() => door.IsPassageOpen, 3, "prepared service doorway physically opens");
            PositionEmptyServiceActor(new Vector3(serviceBlanket.SourceAnchor.position.x, .08f, 29.5f), serviceBlanket.Body.worldCenterOfMass);
            facts.Add("DIAGNOSTIC: sustained mild cold/rest, initial burnt lamp105/valve105 and pre-opened room106 door. Only empty actor1 is repositioned at shelf, then valve and phone; every carried blanket metre is real remote input through NGO.");
            session.RaiseChanged(); WriteStage("services-world-ready");
            yield return Until(() => serviceBlanket.State?.Location == ServiceItemLocation.HeldByPlayer && serviceBlanket.State.PlayerId == 1,
                25, "remote actual shelf grab reaches host ownership");
            Require(coop.Players[1].Interactor.HeldBody == serviceBlanket.Body, "host physics joint holds the same blanket body");
            Require(serviceBlanket.Body.GetComponent<ConfigurableJoint>() != null, "service carry uses the production joint");
            Require(!session.TakeServiceItem(0, serviceBlanket).Success && !session.DeliverBlanket(0, target).Success,
                "other employee cannot take or deliver actor1's carried blanket");
            Require(serviceBlanket.State.PlayerId == 1, "rejected cross-actor command leaves actual carrier unchanged");
            Vector3 carryStart = serviceBlanket.Body.position;
            WriteStage("services-host-pickup");
            yield return Until(() => serviceBlanket.State.Location == ServiceItemLocation.Delivered, 50, "remote carry and immediate bed use deliver actual blanket");
            Require(serviceGuest.BlanketComfortBonus > 0 && serviceGuest.Memory.BlanketsDelivered == 1, "host receives blanket comfort and memory once");
            Require(session.Simulation.Services.BlanketsAvailable == initialStock - 1 && blanketCase.Response.StaffActionAt >= 0,
                "physical delivery spends one stock slot and records real help separately from recovery");
            yield return Until(() => blanketCase.Status == ServiceStatus.Fulfilled || !blanketCase.Active &&
                !session.Simulation.Incidents.Items.Any(i => i.GuestId == serviceGuest.GuestId && i.Reason == IncidentReason.Temperature && i.Active),
                25, "sustained perceived recovery closes the same cold situation after delivery");
            yield return Until(() => target.deliveredBlanket.activeSelf && !coop.Players[1].Interactor.HeldBody, 3,
                "host late-update displays the delivered blanket and releases its physical grab");
            Require(target.deliveredBlanket.activeSelf && !coop.Players[1].Interactor.HeldBody, "delivered bed blanket visible and physical grab released");
            float carryMetres = Horizontal(carryStart, target.transform.position);
            Require(carryMetres > 5, "actual blanket travelled from utility storage to guest room");
            var wakeCase = session.Simulation.Services.Cases.SingleOrDefault(item => item.GuestId == serviceGuest.GuestId && item.Kind == ServiceKind.WakeUpCall);
            if (wakeCase == null)
            {
                float wakeContextAt = serviceGuest.Agent.Schedule.SleepTime - session.Simulation.Services.Settings.ReplySeconds;
                float contextWait = Mathf.Clamp(wakeContextAt - session.Simulation.Elapsed + 30, 30, 180);
                facts.Add("WAKE CONTEXT WAIT: hotelTime=" + session.Simulation.Elapsed.ToString("F1") +
                    "; contextAt=" + wakeContextAt.ToString("F1") + "; realTimeout=" + contextWait.ToString("F1") +
                    "; normalHotelClock=True.");
                yield return Until(() => session.Simulation.Elapsed >= wakeContextAt, contextWait,
                    "wake request waits for actual pre-sleep context");
                wakeCase = session.Simulation.Services.Cases.SingleOrDefault(item => item.GuestId == serviceGuest.GuestId && item.Kind == ServiceKind.WakeUpCall);
                // Production preferences can arise while waiting. Only already heard concerns
                // may be declined here; an unexpected private concern fails this isolated fixture.
                foreach (var incidental in session.Simulation.Services.Cases.Where(item => item.GuestId == serviceGuest.GuestId &&
                    item.Active && item.Kind != ServiceKind.WakeUpCall).ToArray())
                {
                    Require(incidental.IsKnownToHotel, "unexpected private incidental preference requires its own communication scenario: " + incidental.Kind);
                    Require(session.Simulation.RespondToService(0, incidental.Id, false).Success, "labelled fixture declines incidental " + incidental.Kind);
                    facts.Add("DIAGNOSTIC: explicitly declined naturally generated " + incidental.Kind + " before the independent wake-phone case; history retained.");
                }
                if (wakeCase == null)
                {
                    Require(session.Simulation.DebugForceService(serviceGuest.GuestId, ServiceKind.WakeUpCall).Success, "diagnostic within-stay wake request");
                    wakeCase = session.Simulation.Services.Cases.Single(item => item.GuestId == serviceGuest.GuestId && item.Kind == ServiceKind.WakeUpCall);
                }
            }
            else facts.Add("WAKE SETUP: reused the naturally generated wake-up case and retained its original identity and due time.");
            if (!wakeCase.IsKnownToHotel) yield return ReceiveNaturalConcernHost(wakeCase, "natural-wake");
            Require(wakeCase.Active, "wake request remains actionable before management acceptance");
            Require(session.Simulation.RespondToService(0, wakeCase.Id, true).Success, "labelled management acceptance creates one promise");
            var promise = session.Simulation.Services.Promises.Single();
            session.RaiseChanged(); WriteStage("services-host-delivered");
            yield return Stage("services-invalid-call-ui-open", 15);
            yield return Until(() => coop.Players[1].IsUIBlocked, 5, "ordinary remote management UI state reaches the host");
            WriteStage("services-invalid-call-ready");
            yield return Stage("services-invalid-call-sent", 12);
            yield return Until(() => session.LastMessage.Contains("reception telephone"), 6, "normal remote call command rejected without physical phone grant");
            Require(promise.Status == PromiseStatus.Accepted && serviceGuest.Memory.PromisesKept == 0,
                "rejected remote call cannot fulfill another station's promise");
            WriteStage("services-invalid-call-rejected");
            yield return Stage("services-invalid-call-ui-closed", 8);
            yield return Until(() => !coop.Players[1].IsUIBlocked, 5, "remote management UI closes before the physical valve input");
            var valve = FindObjectsByType<RadiatorValveInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 106);
            Require(session.Simulation.SetRadiatorSetting(0, 106, 1).Success, "labelled valve-input fixture resets106 after independently verified guest self-help");
            facts.Add("DIAGNOSTIC: valve106 reset to1 only after the cold contact/delivery/recovery chain, so the independent remote valve-input check has a known initial level.");
            float boilerBefore = session.Simulation.Boiler.Load;
            PositionEmptyServiceActor(new Vector3(8, .08f, valve.transform.position.z), valve.transform.position);
            WriteStage("services-valve-ready");
            yield return Until(() => valve.State.RadiatorSetting == 2, 20, "remote physical radiator use reaches host room setting");
            Require(session.Simulation.Boiler.Load > boilerBefore, "remote valve increases actual boiler demand");
            WriteStage("services-valve-verified");
            yield return Stage("services-client-valve", 10);
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            PositionEmptyServiceActor(new Vector3(aim.x, .08f, aim.z - 1.45f), aim);
            WriteStage("services-phone-ready");
            yield return Stage("services-client-phone-focused", 15);
            sustainServiceGuest = false;
            session.AdvanceToNextPromise(); session.RaiseChanged();
            Require(Mathf.Abs(session.Simulation.Elapsed - promise.DueTime) < 1, "diagnostic clock advances to actual promised due time");
            WriteStage("services-phone-due");
            yield return Until(() => promise.Status == PromiseStatus.Completed, 15, "remote actual phone menu completes promised wake-up call");
            Require(serviceGuest.Memory.PromisesKept == 1 && serviceGuest.Memory.PromisesBroken == 0, "host remembers one kept promise");
            facts.Add("PhysicalBlanketPickup=True PhysicalBlanketDelivery=True ServiceStateReplicated=True CrossActorOwnershipRejected=True UngrantedCallRejected=True RemoteRadiator=True RemotePhonePromise=True");
            facts.Add("BlanketStock=" + initialStock + "->" + session.Simulation.Services.BlanketsAvailable + " CarryDisplacementMetres=" + carryMetres.ToString("F2") +
                " ComfortBonus=" + serviceGuest.BlanketComfortBonus.ToString("F2") + " PromisesKept=" + serviceGuest.Memory.PromisesKept +
                " Lamp105Broken=" + session.Rooms.Single(room => room.Profile.Id == 105).LampBroken);
            facts.Add("WorldSnapshotPayloadBytes=" + lan.LastWorldBytes + " WorldSnapshotDecodedBytes=" + lan.LastWorldDecodedBytes +
                " EncodedFraction=" + (lan.LastWorldDecodedBytes > 0 ? (lan.LastWorldBytes / (float)lan.LastWorldDecodedBytes).ToString("F4") : "unavailable") +
                " ModelSnapshotPayloadBytes=" + lan.LastModelBytes + "; measured most recent payload sizes, not a latency or throughput benchmark.");
            WriteStage("services-host-complete"); yield return Stage("client-complete", 12);
        }

        void PositionEmptyServiceActor(Vector3 position, Vector3 target)
        {
            Require(!coop.Players[1].Interactor.HeldBody, "diagnostic reposition only affects an empty employee");
            var pose = new GameObject("DIAGNOSTIC empty remote service approach");
            Vector3 forward = target - position; forward.y = 0;
            pose.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            coop.Players[1].ResetToSpawn(pose.transform); Destroy(pose);
        }

        IEnumerator RunServicesClient()
        {
            yield return Until(() => lan.PeerConnected && lan.HasSnapshot, 35, "service client receives host model");
            CheckCameraAndAuthority();
            Require(session.IsLanReplica && session.Simulation.IsReadOnlyMirror, "service client has no simulation authority");
            Require(pad != null && pad.added && pad.enabled && pad.canRunInBackground, "service fixture owns its private background pad");
            yield return Until(() => ReferenceEquals(coop.Players[1].Input.Gamepad, pad), 3, "service client binds own pad");
            lastClientSequence = lan.AppliedModelSequence; lastClientClock = session.Simulation.Elapsed; clockTracking = true;
            ManagementUI.Instance?.Close();
            yield return ReceiveNaturalConcernClient(ServiceKind.ExtraBlanket, "natural-cold");
            yield return Stage("services-world-ready", 100);
            yield return Until(() => session.Simulation.Services?.Cases.Any(item => item.Kind == ServiceKind.ExtraBlanket) == true, 6,
                "client receives persistent blanket request");
            serviceGuest = session.Simulation.Guests.Single();
            serviceBlanket = FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Single(item => item.ItemId == "blanket:0");
            int initialStock = session.Simulation.Services.BlanketsAvailable;
            Require(session.Rooms.Single(room => room.Profile.Id == 105).LampBroken && session.Rooms.Single(room => room.Profile.Id == 105).RadiatorSetting == 2,
                "lamp fault and radiator setting arrive in normal model snapshot");
            var lamp = FindObjectsByType<RoomLampInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 105);
            yield return Until(() => !lamp.bulbLight.enabled, 5, "actual burnt lamp light is also off in world snapshot");
            Require(!session.Simulation.TakeServiceItem(1, "blanket:0").Success && !session.Simulation.SetRadiatorSetting(1, 106, 3).Success,
                "direct mirror cannot grant an item or change radiator demand");
            yield return Until(() => Horizontal(coop.Players[1].transform.position,
                new Vector3(serviceBlanket.SourceAnchor.position.x, .08f, 29.5f)) < .2f, 5, "shelf approach arrives by world replication");
            yield return ServicesAim(() => serviceBlanket.Body.worldCenterOfMass, "Extra blanket", true, serviceBlanket.PlacementCollider as BoxCollider);
            if (capture) yield return Capture("client-service-stock");
            yield return TapGrab();
            yield return Until(ServiceBlanketHeld, 6, "actual remote grab mirrors blanket ownership");
            yield return Stage("services-host-pickup", 6);
            yield return ServicesWalk(new Vector3(.25f, 0, 29.5f));
            yield return ServicesWalk(new Vector3(.25f, 0, 24));
            yield return ServicesWalk(new Vector3(3.25f, 0, 24));
            yield return ServicesWalk(new Vector3(4.65f, 0, 24.1f));
            var target = FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 106);
            yield return ServicesAim(() => target.transform.position, "Give extra blanket");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => session.Simulation.Services.FindItem("blanket:0").Location == ServiceItemLocation.Delivered &&
                serviceGuest.BlanketComfortBonus > 0 && serviceGuest.Memory.BlanketsDelivered == 1, 6, "host delivery returns bonus and memory to client");
            yield return Until(() => target.deliveredBlanket.activeSelf, 5, "host delivered blanket becomes visible on replicated bed");
            Require(session.Simulation.Services.BlanketsAvailable == initialStock - 1, "client finite stock agrees with actual consumption");
            Require(session.Simulation.Services.Cases.Single(item => item.Kind == ServiceKind.ExtraBlanket).Response.StaffActionAt >= 0,
                "real blanket help returns to the same persistent response before sustained recovery");
            if (capture) yield return Capture("client-service-delivered");
            yield return ReceiveNaturalConcernClient(ServiceKind.WakeUpCall, "natural-wake");
            yield return Stage("services-host-delivered", 8);
            yield return Until(() => session.Simulation.Services.Promises.Count == 1, 5, "accepted wake promise reaches client snapshot");
            var promise = session.Simulation.Services.Promises.Single();
            // LABELLED UI FIXTURE: open the ordinary management board and wait for its real
            // network UI state. This lets the normal command reach the phone range/grant check;
            // sending from a world-input state would be rejected earlier by ReceiveCommand.
            ManagementUI.Instance.Open(1);
            Require(ManagementUI.Instance.IsOpen && ManagementUI.Instance.Owner == 1 && !ManagementUI.Instance.IsWakePhoneOpen,
                "diagnostic ordinary management panel grants no reception-phone access");
            facts.Add("DIAGNOSTIC: ordinary management UI opened at the bed for the negative phone-authorization test; no phone grant is fabricated.");
            WriteStage("services-invalid-call-ui-open");
            yield return Stage("services-invalid-call-ready", 8);
            session.CompleteWakeUpCall(1, promise.Id); WriteStage("services-invalid-call-sent");
            yield return Stage("services-invalid-call-rejected", 8);
            Require(session.Simulation.Services.FindPromise(promise.Id).Status == PromiseStatus.Accepted,
                "unauthorized phone command did not mutate client promise");
            ManagementUI.Instance.Close();
            Require(!ManagementUI.Instance.IsOpen, "diagnostic management panel closes before returning to world input");
            WriteStage("services-invalid-call-ui-closed");
            yield return Stage("services-valve-ready", 8);
            var valve = FindObjectsByType<RadiatorValveInteraction>(FindObjectsSortMode.None).Single(item => item.roomId == 106);
            yield return Until(() => Horizontal(coop.Players[1].transform.position, new Vector3(8, .08f, valve.transform.position.z)) < .2f,
                5, "empty staff valve approach replicated");
            yield return ServicesAim(() => valve.transform.position, "Radiator 1/3");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => session.Rooms.Single(room => room.Profile.Id == 106).RadiatorSetting == 2, 6, "physical radiator state returns to mirror");
            yield return Stage("services-valve-verified", 6);
            yield return Until(() => Quaternion.Angle(valve.knob.localRotation, Quaternion.Euler(0, 0, -65 + 2 * 43.3f)) < 1,
                5, "visible valve pointer rotation follows same host setting");
            WriteStage("services-client-valve");
            yield return Stage("services-phone-ready", 8);
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            yield return Until(() => Horizontal(coop.Players[1].transform.position, new Vector3(aim.x, .08f, aim.z - 1.45f)) < .2f,
                5, "empty staff phone approach replicated");
            yield return ServicesAim(() => aim, "Reception telephone");
            WriteStage("services-client-phone-focused");
            yield return Stage("services-phone-due", 8);
            yield return TapButton(GamepadButton.South);
            yield return Until(() => ManagementUI.Instance.IsWakePhoneOpen, 6, "remote physical phone opens its scoped menu");
            yield return TapButton(GamepadButton.South);
            yield return Until(() => session.Simulation.Services.FindPromise(promise.Id).Status == PromiseStatus.Completed && serviceGuest.Memory.PromisesKept == 1,
                8, "short remote phone call completes through host and returns kept memory");
            if (capture) yield return Capture("client-service-phone");
            yield return Stage("services-host-complete", 6);
            float before = session.Simulation.Elapsed; session.Simulation.Tick(20);
            Require(Mathf.Abs(before - session.Simulation.Elapsed) < .00001f && stableClockChecks >= 10,
                "service mirror advances only from host snapshots");
            facts.Add("PhysicalBlanketPickup=True PhysicalBlanketDelivery=True ServiceStateReplicated=True CrossActorOwnershipRejected=True UngrantedCallRejected=True RemoteRadiator=True RemotePhonePromise=True");
            facts.Add("BlanketStock=" + initialStock + "->" + session.Simulation.Services.BlanketsAvailable + " ComfortBonus=" + serviceGuest.BlanketComfortBonus.ToString("F2") +
                " PromisesKept=" + serviceGuest.Memory.PromisesKept + " Radiator106=" + session.Rooms.Single(room => room.Profile.Id == 106).RadiatorSetting +
                " SnapshotOnlyClockChecks=" + stableClockChecks);
            Queue(default); WriteStage("client-complete");
        }

        bool ServiceBlanketHeld()
        {
            var state = session.Simulation.Services.FindItem("blanket:0");
            return state != null && state.Location == ServiceItemLocation.HeldByPlayer && state.PlayerId == 1;
        }

        IEnumerator ServicesAim(Func<Vector3> target, string caption = null, bool pickup = false, BoxCollider intendedSurface = null)
        {
            var actor = coop.Players[1];
            var replica = session.GetComponent<LanWorldReplicator>();
            Require(replica != null, "service aiming observes the actual world snapshot sequence");
            float started = Time.realtimeSinceStartup, deadline = started + 18;
            int pulses = 0; bool acquired = false, settled = false;
            float bestAngle = 180, maximumFrameSeconds = 0;
            var pulseNotes = new System.Collections.Generic.List<string>();
            Vector2 AimError()
            {
                Vector3 delta = target() - actor.PlayerCamera.transform.position;
                return new Vector2(Mathf.DeltaAngle(actor.transform.eulerAngles.y, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg),
                    Mathf.DeltaAngle(actor.PlayerCamera.transform.localEulerAngles.x,
                        -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg));
            }
            float AimAngle() => Vector3.Angle(actor.PlayerCamera.transform.forward, target() - actor.PlayerCamera.transform.position);
            bool Neutral() => pad.rightStick.ReadValue().sqrMagnitude < .00001f && actor.Input.Look.sqrMagnitude < .00001f;
            bool TargetSurface()
            {
                if (caption == null || (pickup && !actor.Interactor.ReplicaPickup) ||
                    !(actor.Interactor.ReplicaCaption ?? "").Contains(caption)) return false;
                if (!intendedSurface) return true;
                // Replica colliders are disabled. Intersect the selected item's authored box
                // so an adjacent identically labelled blanket cannot satisfy this observation.
                var ray = new Ray(intendedSurface.transform.InverseTransformPoint(actor.PlayerCamera.transform.position),
                    intendedSurface.transform.InverseTransformVector(actor.PlayerCamera.transform.forward));
                return new Bounds(intendedSurface.center, intendedSurface.size).IntersectRay(ray);
            }
            bool Aligned() => caption != null ? TargetSurface() : AimAngle() < 2.2f;
            float startAngle = AimAngle();
            Queue(default);
            yield return Until(Neutral, 1, "private service pad becomes neutral before aiming");
            while (Time.realtimeSinceStartup < deadline)
            {
                bestAngle = Mathf.Min(bestAngle, AimAngle());
                Quaternion before = actor.PlayerCamera.transform.rotation;
                long beforeSequence = replica.AppliedSequence;
                Vector2 emitted = Vector2.zero;
                float responseBegan = Time.realtimeSinceStartup;
                bool response = Aligned();
                if (!response)
                {
                    Vector2 error = AimError();
                    float requestedDegrees = Mathf.Clamp(error.magnitude * .60f, .55f, 50);
                    Vector2 axis = new Vector2(error.x, -error.y).normalized * .35f;
                    float pulseDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + 1.5f);
                    Queue(new GamepadState { rightStick = axis }); pulses++;
                    // Send a finite angular dose measured from this private pad's actual
                    // processed input. Never continue steering from delayed host feedback.
                    do
                    {
                        yield return null;
                        maximumFrameSeconds = Mathf.Max(maximumFrameSeconds, Time.unscaledDeltaTime);
                        emitted += actor.Input.Look * actor.controllerLookSpeed * Time.unscaledDeltaTime;
                    }
                    while (emitted.magnitude < requestedDegrees && Time.realtimeSinceStartup < pulseDeadline);
                    Queue(default);
                    float neutralDeadline = Mathf.Min(deadline, Time.realtimeSinceStartup + 1);
                    do
                    {
                        yield return null;
                        emitted += actor.Input.Look * actor.controllerLookSpeed * Time.unscaledDeltaTime;
                    }
                    while (!Neutral() && Time.realtimeSinceStartup < neutralDeadline);
                    Require(Neutral(), "finite service look dose released its private pad; raw=" + pad.rightStick.ReadValue() + " input=" + actor.Input.Look);
                    responseBegan = Time.realtimeSinceStartup;
                    float responseDeadline = Mathf.Min(deadline, responseBegan + 4);
                    // A second pulse is forbidden until new world snapshots show that the
                    // first bounded dose arrived. This also measures delayed pose delivery.
                    while (Time.realtimeSinceStartup < responseDeadline)
                    {
                        float observed = Quaternion.Angle(before, actor.PlayerCamera.transform.rotation);
                        response = replica.AppliedSequence > beforeSequence && observed >= Mathf.Max(.20f, emitted.magnitude * .80f);
                        if (response) break;
                        yield return null;
                    }
                }
                if (!response)
                {
                    pulseNotes.Add("dose=" + emitted.ToString("F2") + " noResponse world=" + beforeSequence + "->" + replica.AppliedSequence +
                        " observed=" + Quaternion.Angle(before, actor.PlayerCamera.transform.rotation).ToString("F2") +
                        " raw=" + pad.rightStick.ReadValue().ToString("F3") + " input=" + actor.Input.Look.ToString("F3") + " " + ServiceNetworkDiagnostic(replica.AppliedSequence));
                    break;
                }
                long settleSequence = replica.AppliedSequence;
                float settleBegan = Time.realtimeSinceStartup, stableAt = settleBegan;
                Quaternion last = actor.PlayerCamera.transform.rotation;
                settled = false;
                while (Time.realtimeSinceStartup < deadline && Time.realtimeSinceStartup - settleBegan < 2)
                {
                    yield return null;
                    Quaternion current = actor.PlayerCamera.transform.rotation;
                    if (Quaternion.Angle(last, current) > .08f) stableAt = Time.realtimeSinceStartup;
                    last = current;
                    if (Neutral() && replica.AppliedSequence >= settleSequence + 3 && Time.realtimeSinceStartup - stableAt >= .25f)
                    { settled = true; break; }
                }
                pulseNotes.Add("dose=" + emitted.ToString("F2") + " world=" + beforeSequence + "->" + replica.AppliedSequence +
                    " responseAndSettle=" + (Time.realtimeSinceStartup - responseBegan).ToString("F2") +
                    " observed=" + Quaternion.Angle(before, actor.PlayerCamera.transform.rotation).ToString("F2") +
                    " angle=" + AimAngle().ToString("F2") + " settled=" + settled +
                    " raw=" + pad.rightStick.ReadValue().ToString("F3") + " input=" + actor.Input.Look.ToString("F3") + " " + ServiceNetworkDiagnostic(replica.AppliedSequence));
                if (!settled) break;
                if (Aligned()) { acquired = true; break; }
            }
            Queue(default);
            float finalAngle = AimAngle(); Vector2 finalError = AimError();
            string diagnostic = "target=" + target().ToString("F3") + " actor=" + actor.transform.position.ToString("F3") +
                " camera=" + actor.PlayerCamera.transform.position.ToString("F3") + " angle=" + finalAngle.ToString("F2") +
                " yawError=" + finalError.x.ToString("F2") + " pitchError=" + finalError.y.ToString("F2") +
                " startAngle=" + startAngle.ToString("F2") + " bestAngle=" + bestAngle.ToString("F2") +
                " elapsed=" + (Time.realtimeSinceStartup - started).ToString("F2") + " pulses=" + pulses +
                " settled=" + settled + " worldSequence=" + replica.AppliedSequence +
                " inputLook=" + actor.Input.Look.ToString("F3") + " intendedSurface=" + (!intendedSurface || TargetSurface()) +
                " maxSampleFrameMs=" + (maximumFrameSeconds * 1000).ToString("F1") + " uiBlocked=" + actor.IsUIBlocked +
                " pickup=" + actor.Interactor.ReplicaPickup + " caption=" + (actor.Interactor.ReplicaCaption ?? "none").Replace('\n', '|');
            facts.Add("SERVICE AIM: " + diagnostic);
            foreach (var note in pulseNotes) facts.Add("SERVICE LOOK DOSE: " + note);
            bool success = acquired && settled && (caption != null ? TargetSurface() : finalAngle < 4);
            if (!success && capture) yield return Capture("client-service-aim-failed");
            Require(success, "remote service look settled on " + (caption ?? "walking heading within four degrees") + "; " + diagnostic);
        }

        IEnumerator ServicesWalk(Vector3 destination)
        {
            var actor = coop.Players[1]; Vector3 direction = destination - actor.transform.position; direction.y = 0;
            if (direction.sqrMagnitude > .01f) yield return ServicesAim(() => actor.PlayerCamera.transform.position + direction.normalized * 5);
            float deadline = Time.realtimeSinceStartup + 18;
            while (Horizontal(actor.transform.position, destination) > .14f && Time.realtimeSinceStartup < deadline)
            {
                Require(ServiceBlanketHeld(), "actual network carry retains the same host-owned blanket");
                Vector3 delta = destination - actor.transform.position; delta.y = 0;
                Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                Queue(new GamepadState { leftStick = new Vector2(local.x, local.z) * Mathf.Clamp(delta.magnitude, .24f, .75f) });
                yield return null;
            }
            Queue(default); yield return new WaitForSecondsRealtime(.30f);
            Require(Horizontal(actor.transform.position, destination) < .3f, "remote carry reached " + destination);
            Require(ServiceBlanketHeld() && Vector3.Distance(serviceBlanket.Body.worldCenterOfMass, actor.PlayerCamera.transform.position) < 2.4f,
                "replicated blanket body follows actual carrier through corridor and doorway");
        }
    }
}
#endif
