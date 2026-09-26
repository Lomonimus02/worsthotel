#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        bool serviceFixtures, serviceVerified;
        float serviceCarriedMetres;

        IEnumerator VerifyGuestServices()
        {
            Require(soloTour, "service fixtures use the actual SOLO staff/controller");
            var production = session.config;
            var fixture = Instantiate(production);
            var services = Instantiate(production.services);
            services.eligibility = 0;
            services.naturalCommunicationEnabled = false;
            fixture.services = services;
            session.config = fixture;
            facts.Add("SERVICE REGRESSION FIXTURES: two disposable hotels use explicitly cloned legacy communication and eligibility=0 to isolate the existing stock, carrying and promise chains. DebugForceService, mild cold, burnt bulb and dirty-room setup select the scenarios. Service stock, thermal/electrical tuning, due windows and consequences remain production values. Empty-handed staff viewpoints and model key pickup/handoff are labelled adapters; actual owned controller, colliders, physics carrying, service choices and production guest routes execute the chains. No synthetic guest route callbacks or carried-item repositioning. Separate natural-contact hotels follow before the production three-day tour.");
            yield return ResetAgencyHotel();
            yield return StartServiceFixture(GuestKind.Business, false);
            var guest = session.Simulation.Guests.Single();
            var room = session.Rooms.Single(r => r.Profile.Id == 101);
            yield return VerifyPhysicalBlanket(guest, room);
            yield return VerifyPhysicalFixtures(guest, room);
            yield return VerifyServicePromiseAndCheckout(guest, room);
            yield return ResetAgencyHotel();
            yield return StartServiceFixture(GuestKind.Budget, true);
            yield return VerifyPhysicalLuggage(session.Simulation.Guests.Single());
            services.naturalCommunicationEnabled = true;
            yield return VerifyNaturalServiceContacts();
            serviceVerified = true;
            facts.Add("GuestServicesVerified=True PhysicalServiceCarryMetres=" + serviceCarriedMetres.ToString("F2") +
                " Blanket=True Radiator=True Lamp=True Luggage=True PhoneCancelAndComplete=True LateCheckoutDelay=True NaturalContactsVerified=True NoSyntheticGuestRouteCallbacks=True");
            session.config = production;
            yield return ResetAgencyHotel();
            Destroy(fixture); Destroy(services);
            Require(session.Simulation.Services.Settings.Eligibility == production.services.eligibility,
                "production service generation restored before natural three-day tour");
            Require(session.Simulation.Services.Settings.NaturalCommunicationEnabled == production.services.naturalCommunicationEnabled,
                "production communication mode restored before natural three-day tour");
        }

        IEnumerator StartServiceFixture(GuestKind kind, bool waitingForRoom)
        {
            Require(session.Phase == DayPhase.Planning && session.Simulation.Services != null, "fresh service fixture hotel");
            if (waitingForRoom) Require(session.Simulation.DebugMarkRoomDirty(101).Success, "explicit unprepared-room luggage prerequisite");
            var offer = session.Plan.Applications.First(a => a.Archetype.Kind == kind);
            int rate = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) /
                (float)session.Economy.PriceStep) * session.Economy.PriceStep;
            session.Assign(0, offer.Id, 101, rate); session.CommitPlan(0); ManagementUI.Instance.Close();
            agencyModel = session.Simulation; driveSpeed = 8;
            var guest = agencyModel.Guests.Single();
            Position(coop.Players[0], new Vector3(.2f, .08f, 1), new Vector3(0, 1.5f, 4));
            yield return Until(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 25, "service guest physically arrives at reception");
            if (waitingForRoom) { driveSpeed = 1; yield break; }
            Require(agencyModel.Keys.PickUp(0, 101).Success && agencyModel.CheckIn(0, guest.GuestId).Success,
                "explicit service model rack-key pickup/handoff adapter");
            yield return Until(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 30, "service guest traverses real doorway to room101");
            yield return SetAgencyActivity(guest, GuestActivity.QuietRest);
            driveSpeed = 1;
        }

        IEnumerator VerifyPhysicalBlanket(GuestStay guest, RoomState room)
        {
            Require(session.DebugSetMildCold(guest.GuestId).Success && session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success,
                "explicit mild-cold blanket request setup");
            var request = agencyModel.Services.Cases.Single(c => c.Active);
            int stock = agencyModel.Services.BlanketsAvailable;
            yield return OpenServiceBoardCase(request.Id, "service-board");
            yield return ChooseServiceAction(GuestLabels.ServiceAcceptance(request));
            Require(request.Status == ServiceStatus.InProgress && agencyModel.Services.BlanketsAvailable == stock && guest.BlanketComfortBonus == 0,
                "board agreement does not create or deliver a blanket");
            yield return PressMenu(GamepadButton.East); yield return PressMenu(GamepadButton.East);
            var blanket = FindServiceSupply(ServiceItemKind.Blanket);
            yield return PlaceServiceStaff(blanket.SourceAnchor.position + blanket.SourceAnchor.forward * -1.5f, blanket.Body.worldCenterOfMass);
            yield return Capture("service-blanket-stock", "DIAGNOSTIC mild cold / finite physical blanket shelf before real grab and carry");
            yield return GrabServiceSupply(blanket);
            yield return WalkServiceRoute(blanket, new Vector3(-.32f, 0, 29.8f), new Vector3(-.32f, 0, 10), new Vector3(-.75f, 0, 10));
            yield return ServiceRoomEntryWithSupply(blanket, 101);
            yield return WalkServiceRoute(blanket, new Vector3(-3.25f, 0, 10), new Vector3(-4.18f, 0, 10));
            var target = FindObjectsByType<RoomBlanketDeliveryInteraction>(FindObjectsSortMode.None).Single(t => t.roomId == 101);
            yield return AimServicePoint(() => target.GetComponent<Collider>().bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == target, 3, "actual bed blanket target raycast");
            float temperature = room.Temperature;
            float circuitLoad = agencyModel.Electrical.CircuitForRoom(101).RequestedLoad;
            yield return PressMenu(GamepadButton.South);
            yield return Until(() => blanket.State.Location == ServiceItemLocation.Delivered && target.deliveredBlanket.activeSelf, 3,
                "actual carried blanket attaches visibly to guest bed");
            Require(guest.BlanketComfortBonus > 0 && agencyModel.Services.BlanketsAvailable == stock - 1 &&
                coop.Players[0].Interactor.HeldBody == null && request.Status == ServiceStatus.Fulfilled,
                "physical blanket is consumed once and remembered as fulfilled");
            Require(Mathf.Abs(room.Temperature - temperature) < .3f &&
                Mathf.Abs(agencyModel.Electrical.CircuitForRoom(101).RequestedLoad - circuitLoad) < .001f,
                "blanket does not create heat or electrical demand");
            yield return Capture("service-blanket-delivered", "Actual shelf pickup, physics carry, guest entry and bed delivery / personal comfort without electrical load");
            facts.Add("SERVICE BLANKET PASS: item=" + blanket.ItemId + "; shelf=" + stock + "->" + agencyModel.Services.BlanketsAvailable +
                "; visibleBedBlanket=True; comfortBonus=" + guest.BlanketComfortBonus + "; electricalLoadUnchanged=True; productionTemperatureDrift=" +
                (room.Temperature - temperature).ToString("F3") + "; physicalInput=True.");
        }

        IEnumerator VerifyPhysicalFixtures(GuestStay guest, RoomState room)
        {
            var valve = FindObjectsByType<RadiatorValveInteraction>(FindObjectsSortMode.None).Single(v => v.roomId == 101);
            // The wall radiators are rotated +/-90 degrees. Approach their room-facing
            // local -Z side: room101 lands near (-7.98, .08, 9.2), mirroring the tested
            // room106 approach at x8. Global -Z instead runs along the exterior wall/fins.
            yield return PlaceServiceStaff(valve.transform.position - valve.transform.forward * 1.35f, valve.GetComponent<Collider>().bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == valve, 3, "real radiator valve target");
            float load = agencyModel.Boiler.Load;
            int setting = room.RadiatorSetting;
            yield return PressMenu(GamepadButton.South);
            Require(room.RadiatorSetting == setting + 1 && agencyModel.Boiler.Load > load,
                "physical valve changes local setting and actual boiler demand");
            yield return Capture("service-radiator", "Actual controller radiator adjustment / local setting and increased central heating demand");
            facts.Add("SERVICE RADIATOR PASS: setting=" + setting + "->" + room.RadiatorSetting + "; actualBoilerLoad=" + load.ToString("F3") + "->" + agencyModel.Boiler.Load.ToString("F3") + ".");
            var lamp = FindObjectsByType<RoomLampInteraction>(FindObjectsSortMode.None).Single(l => l.roomId == 101);
            Require(session.DebugBreakLamp(101).Success, "explicit burnt-bulb fixture");
            // Use the clear inner bed aisle, also reached by the real bulb carry below,
            // so the before/after views have the same angle and avoid bed/table colliders.
            yield return PlaceServiceStaff(new Vector3(-4.18f, .08f, 10), lamp.GetComponent<Collider>().bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == lamp, 3, "failed lamp visible from its actual delivery aisle");
            yield return Until(() => !lamp.IsLit && !lamp.bulbLight.enabled, 3, "actual failed lamp is dark");
            yield return Capture("service-lamp-off", "DIAGNOSTIC burnt bulb / actual bedside light and lamp glass are off");
            var bulb = FindServiceSupply(ServiceItemKind.ReplacementBulb);
            yield return PlaceServiceStaff(bulb.SourceAnchor.position + bulb.SourceAnchor.forward * -1.3f, bulb.Body.worldCenterOfMass);
            yield return GrabServiceSupply(bulb);
            yield return WalkServiceRoute(bulb, new Vector3(4.8f, 0, 32.4f), new Vector3(4.8f, 0, 30), new Vector3(1, 0, 30), new Vector3(-.32f, 0, 29.8f),
                new Vector3(-.32f, 0, 10), new Vector3(-.75f, 0, 10));
            yield return ServiceRoomEntryWithSupply(bulb, 101);
            yield return WalkServiceRoute(bulb, new Vector3(-3.25f, 0, 10), new Vector3(-4.18f, 0, 10));
            yield return AimServicePoint(() => lamp.GetComponent<Collider>().bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == lamp, 3, "physical carried bulb reaches failed bedside lamp");
            yield return PressMenu(GamepadButton.South);
            yield return Until(() => !room.LampBroken && lamp.IsLit && lamp.bulbLight.enabled, 3, "real replacement bulb restores real powered light");
            Require(bulb.State.Location == ServiceItemLocation.Delivered && bulb.State.RoomId == 101 && coop.Players[0].Interactor.HeldBody == null,
                "replacement consumes the held bulb once");
            yield return Capture("service-lamp-on", "Actual maintenance-shelf bulb pickup, physics carry and installation restore the same lamp");
            facts.Add("SERVICE LAMP PASS: explicitBreak=True; darkLightObserved=True; physicalShelfBulb=" + bulb.ItemId + "; installedRoom=101; realLightRestored=True.");
            electricalPanel.cover.RequestOpen();
            yield return Until(() => electricalPanel.cover.IsPassageOpen, 3, "diagnostic cabinet cover opens for consumer inspection");
            yield return PlaceServiceStaff(new Vector3(4.9f, .08f, 32.7f), new Vector3(4.9f, 1.9f, 35.25f));
            yield return Capture("service-electrical-consumers", "Actual electrical cabinet and consumer readout / diagnostic cover opening only / no load or breaker override");
            facts.Add("SERVICE POWER INSPECTION: " + string.Join("; ", agencyModel.Electrical.Consumers.Select(c => c.Id + " room=" + c.RoomId +
                " requested=" + c.RequestedLoad.ToString("F3") + " delivered=" + c.DeliveredLoad.ToString("F3"))) + ".");
        }

        IEnumerator VerifyServicePromiseAndCheckout(GuestStay guest, RoomState room)
        {
            Require(session.DebugForceService(guest.GuestId, ServiceKind.WakeUpCall).Success, "explicit within-stay wake request");
            var wake = agencyModel.Services.Cases.Single(c => c.Active);
            yield return OpenServiceBoardCase(wake.Id, "service-wake-request");
            yield return ChooseServiceAction("Promise a call");
            var promise = agencyModel.Services.Promises.Single();
            Require(promise.Status == PromiseStatus.Accepted, "actual board choice creates a scheduled promise");
            ManagementUI.Instance.Close();
            Require(!session.CompleteWakeUpCall(0, promise.Id).Success, "cannot fulfill a promised call away from physical phone");
            session.AdvanceToNextPromise(); driveSpeed = 1;
            Require(agencyModel.ForceActivity(guest.GuestId, GuestActivity.QuietRest).Success, "explicit awake phone fixture");
            yield return Until(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 10, "guest actually settles before answering phone");
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            yield return PlaceServiceStaff(phone.transform.position + Vector3.back * 1.45f, phone.GetComponent<Collider>().bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == phone, 3, "physical reception phone is focused");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsWakePhoneOpen, "physical phone opens its real call panel");
            yield return Capture("service-phone", "Accepted wake-up promise shown at the actual reception telephone near its due time");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsWakeCallInProgress, "call begins with a short timed conversation");
            yield return PressMenu(GamepadButton.East);
            yield return new WaitForSecondsRealtime(1.3f);
            Require(promise.Status == PromiseStatus.Accepted && guest.Memory.PromisesKept == 0, "cancelled call cannot complete later");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsWakePhoneOpen, "same physical phone reopens");
            yield return PressMenu(GamepadButton.South);
            yield return Until(() => promise.Status == PromiseStatus.Completed, 3, "actual controller phone call fulfills promise");
            Require(guest.Memory.PromisesKept == 1 && !session.CompleteWakeUpCall(0, promise.Id).Success, "wake-up promise completes exactly once");
            ManagementUI.Instance.Close();
            facts.Add("SERVICE PHONE PASS: actualReceptionPhone=True; timedCallCancelledWithoutCompletion=True; retryCompleted=True; kept=" + guest.Memory.PromisesKept + ".");
            float originalCheckout = guest.Agent.CheckoutTime;
            Require(session.DebugForceService(guest.GuestId, ServiceKind.LateCheckout).Success, "explicit late-checkout preference before original departure");
            var late = agencyModel.Services.Cases.Single(c => c.Active);
            yield return OpenServiceBoardCase(late.Id, "service-late-checkout");
            yield return ChooseServiceAction("Allow checkout");
            ManagementUI.Instance.Close();
            Require(guest.Agent.CheckoutTime > originalCheckout && late.Status == ServiceStatus.Fulfilled, "real checkout schedule extends after actual agreement");
            driveSpeed = 8;
            yield return Until(() => agencyModel.Elapsed >= originalCheckout + 1, 15, "observe room after original checkout time");
            Require(room.Occupied && room.GuestId == guest.GuestId && guest.Agent.CheckedIn &&
                guest.Agent.State != GuestAgentState.CheckingOut && guest.Agent.State != GuestAgentState.Leaving,
                "late guest still occupies the room and delays turnover beyond original deadline");
            facts.Add("SERVICE LATE CHECKOUT PASS: original=" + originalCheckout.ToString("F1") + "; accepted=" + guest.Agent.CheckoutTime.ToString("F1") +
                "; occupiedAt=" + agencyModel.Elapsed.ToString("F1") + "; turnoverDelayed=True.");
            yield return Until(() => room.DepartingGuestId == null && !room.Occupied && room.Cleanliness == Cleanliness.Dirty, 20,
                "late guest physically leaves before room becomes available for linen preparation");
        }

        IEnumerator VerifyPhysicalLuggage(GuestStay guest)
        {
            Require(session.DebugForceService(guest.GuestId, ServiceKind.LuggageStorage).Success, "explicit waiting-for-unready-room luggage request");
            var request = agencyModel.Services.Cases.Single(c => c.Active);
            yield return OpenServiceBoardCase(request.Id, "service-luggage-request");
            yield return ChooseServiceAction(GuestLabels.ServiceAcceptance(request));
            ManagementUI.Instance.Close();
            var suitcase = FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None).Single(s => s.State?.Kind == ServiceItemKind.Luggage && s.State.GuestId == guest.GuestId);
            // The waiting guest faces -Z and docks their case on their local right (-X).
            // Approach from that outer side; global +X puts the guest between us and it.
            // Only the empty staff viewpoint is placed. The suitcase is never moved here.
            Vector3 approach = suitcase.Body.position + suitcase.transform.right * 1.45f;
            yield return PlaceServiceStaff(approach, suitcase.Body.worldCenterOfMass);
            yield return GrabServiceSupply(suitcase);
            // First clear reception along the outer x column, avoiding the waiting guest
            // and the two loose lobby cases, then carry west to the storage platform.
            yield return WalkServiceRoute(suitcase, new Vector3(approach.x, 0, -2.3f), new Vector3(-7.3f, 0, -2.3f));
            var zone = FindAnyObjectByType<LuggageStorageZone>();
            yield return AimServicePoint(() => zone.storageBounds.bounds.center);
            yield return Until(() => coop.Players[0].Interactor.Focused == zone, 3, "actual storage zone raycast while carrying guest suitcase");
            yield return PressMenu(GamepadButton.South);
            yield return Until(() => suitcase.State.Location == ServiceItemLocation.Stored && coop.Players[0].Interactor.HeldBody == null, 3,
                "actual held suitcase is placed in storage");
            Require(request.Status == ServiceStatus.Fulfilled && guest.Memory.LuggageStored == 1 && !guest.Agent.CheckedIn,
                "storage agreement fulfilled while guest still awaits prepared room");
            yield return Capture("service-luggage-stored", "Actual accepted guest suitcase picked up, carried across reception and placed on luggage platform");
            facts.Add("SERVICE LUGGAGE PASS: actualGuestSuitcase=" + suitcase.ItemId + "; physicalCarry=True; stored=True; stillAwaitingRoom=True; noAutoStaff=True.");
        }

        ServiceSupplyItem FindServiceSupply(ServiceItemKind kind) => FindObjectsByType<ServiceSupplyItem>(FindObjectsSortMode.None)
            .Where(s => s.State?.Kind == kind && s.State.Location == ServiceItemLocation.OnShelf).OrderBy(s => s.ItemId).First();

        IEnumerator OpenServiceBoardCase(string id, string capture)
        {
            ManagementUI.Instance.Close(); driveSpeed = 1;
            var board = FindAnyObjectByType<ReceptionServiceBoardInteraction>();
            var aim = board.transform.position + new Vector3(0, .46f, .06f);
            yield return PlaceServiceStaff(board.transform.position + Vector3.back * 1.45f, aim);
            yield return Until(() => coop.Players[0].Interactor.Focused == board, 3, "actual service board raycast");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsServiceBoardOpen, "physical service board opens NOW and UPCOMING");
            if (capture == "service-board") yield return Capture(capture, "Original NOW / UPCOMING service board opened with owned controller at its actual reception prop");
            var request = agencyModel.Services.FindCase(id);
            yield return ChooseServiceAction(request.RoomId + " · " + GuestLabels.Service(request.Kind));
            Require(ReadMenuField<string>(ManagementUI.Instance, "selectedServiceCase") == id, "controller selects the intended service case");
            if (capture != "service-board") yield return Capture(capture, "Original service decision page selected through actual board/controller input");
        }

        IEnumerator ChooseServiceAction(string beginsWith)
        {
            var ui = ManagementUI.Instance;
            var choices = ReadMenuField<List<(Rect rect, string title, Action action, bool enabled)>>(ui, "serviceChoices");
            int index = choices.FindIndex(c => c.enabled && c.title.StartsWith(beginsWith, StringComparison.Ordinal));
            Require(index >= 0, "visible service action exists: " + beginsWith);
            yield return NavigateServiceMenu(index);
            yield return PressMenu(GamepadButton.South);
        }

        IEnumerator NavigateServiceMenu(int target)
        {
            var ui = ManagementUI.Instance;
            for (int count = 0; ReadMenuField<int>(ui, "focus") != target && count < 20; count++) yield return PressMenu(GamepadButton.DpadDown);
            Require(ReadMenuField<int>(ui, "focus") == target, "owned controller navigates to intended service response");
        }

        IEnumerator PlaceServiceStaff(Vector3 position, Vector3 target)
        {
            var actor = coop.Players[0];
            Require(actor.Interactor.HeldBody == null, "only empty-handed service viewpoint setup may reposition staff");
            position.y = .08f;
            Position(actor, position, target); actor.enabled = true;
            yield return null; yield return null;
            yield return AimServicePoint(() => target);
        }

        IEnumerator AimServicePoint(Func<Vector3> target)
        {
            var actor = coop.Players[0]; actor.enabled = true;
            float deadline = Time.realtimeSinceStartup + 6;
            while (Time.realtimeSinceStartup < deadline)
            {
                Vector3 delta = target() - actor.PlayerCamera.transform.position;
                float yaw = Mathf.DeltaAngle(actor.transform.eulerAngles.y, Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg);
                float pitch = -Mathf.Atan2(delta.y, new Vector2(delta.x, delta.z).magnitude) * Mathf.Rad2Deg -
                    Mathf.DeltaAngle(0, actor.PlayerCamera.transform.localEulerAngles.x);
                if (Mathf.Abs(yaw) < 1.5f && Mathf.Abs(pitch) < 1.5f) break;
                InputSystem.QueueStateEvent(verificationPads[0], new GamepadState { rightStick = new Vector2(
                    Mathf.Abs(yaw) < 1.5f ? 0 : Mathf.Sign(yaw) * .35f, Mathf.Abs(pitch) < 1.5f ? 0 : -Mathf.Sign(pitch) * .35f) });
                yield return null;
            }
            InputSystem.QueueStateEvent(verificationPads[0], new GamepadState()); yield return null; yield return null;
            Require(Vector3.Angle(actor.PlayerCamera.transform.forward, target() - actor.PlayerCamera.transform.position) < 4,
                "real controller look acquires service target");
        }

        IEnumerator GrabServiceSupply(ServiceSupplyItem item)
        {
            yield return AimServicePoint(() => item.Body.worldCenterOfMass);
            var actor = coop.Players[0];
            var pickup = item.GetComponent<PhysicsPickup>();
            float deadline = Time.realtimeSinceStartup + 3;
            while (actor.Interactor.FocusedPickup != pickup && Time.realtimeSinceStartup < deadline) yield return null;
            if (actor.Interactor.FocusedPickup != pickup)
            {
                var ray = new Ray(actor.PlayerCamera.transform.position, actor.PlayerCamera.transform.forward);
                string hits = string.Join("; ", Physics.RaycastAll(ray, actor.Interactor.reach, ~0, QueryTriggerInteraction.Ignore)
                    .Where(hit => !hit.collider.transform.IsChildOf(actor.transform)).OrderBy(hit => hit.distance)
                    .Select(hit => hit.collider.name + " at " + hit.distance.ToString("F2") + "m point=" + hit.point));
                string diagnostic = "target=" + item.ItemId + "; actor=" + actor.transform.position + "; item=" + item.Body.position +
                    "; focused=" + (actor.Interactor.Focused ? actor.Interactor.Focused.name : "none") +
                    "; pickup=" + (actor.Interactor.FocusedPickup ? actor.Interactor.FocusedPickup.name : "none") + "; rayHits=" + hits;
                facts.Add("SERVICE PICKUP FOCUS FAILURE: " + diagnostic);
                yield return Capture("service-pickup-blocked", "DIAGNOSTIC failed pickup ray / " + diagnostic);
                Require(false, "actual service item is the first pickup surface: " + diagnostic);
            }
            yield return ServiceGrabEdge();
            Require(coop.Players[0].Interactor.HeldBody == item.Body && item.State.PlayerId == 0 &&
                item.State.Location == ServiceItemLocation.HeldByPlayer && item.Body.GetComponent<ConfigurableJoint>(),
                "actual grab joint and authoritative owner for " + item.ItemId);
        }

        IEnumerator ServiceGrabEdge()
        {
            BindSyntheticStaff(); var pad = verificationPads[0];
            Require(ReferenceEquals(coop.Players[0].Input.Gamepad, pad), "service grab uses only the owned synthetic pad");
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
            InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.RightShoulder));
            bool read = false;
            for (int i = 0; i < 8 && !read; i++) { yield return null; read = coop.Players[0].Input.GrabPressed; }
            InputSystem.QueueStateEvent(pad, new GamepadState()); yield return null; yield return null;
            Require(read, "actual service grab button read");
        }

        IEnumerator WalkServiceRoute(ServiceSupplyItem item, params Vector3[] points)
        {
            var actor = coop.Players[0]; actor.enabled = true;
            foreach (var point in points)
            {
                Vector3 direction = point - actor.transform.position; direction.y = 0;
                if (direction.sqrMagnitude > .01f) yield return AimServicePoint(() => actor.PlayerCamera.transform.position + direction.normalized * 5);
                float deadline = Time.realtimeSinceStartup + 20;
                while (ServiceDistance(actor.transform.position, point) > .16f && Time.realtimeSinceStartup < deadline)
                {
                    Require(actor.Interactor.HeldBody == item.Body && item.State.PlayerId == 0, "service item remains physically held throughout route: " + item.ItemId);
                    Vector3 previous = item.Body.position;
                    Vector3 delta = point - actor.transform.position; delta.y = 0;
                    Vector3 local = actor.transform.InverseTransformDirection(delta.normalized);
                    float speed = Mathf.Clamp(delta.magnitude * 1.3f, .25f, .8f);
                    InputSystem.QueueStateEvent(verificationPads[0], new GamepadState { leftStick = new Vector2(local.x, local.z) * speed });
                    yield return null;
                    serviceCarriedMetres += ServiceDistance(previous, item.Body.position);
                }
                InputSystem.QueueStateEvent(verificationPads[0], new GamepadState()); yield return null; yield return null;
                Require(ServiceDistance(actor.transform.position, point) < .22f && actor.Interactor.HeldBody == item.Body,
                    "physical service route reaches " + point + "; actor=" + actor.transform.position + "; body=" + item.Body.position);
                Require(Vector3.Distance(item.Body.worldCenterOfMass, actor.PlayerCamera.transform.position) < 2.2f,
                    "carried service body follows its actual holder");
            }
        }

        static float ServiceDistance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        IEnumerator ServiceRoomEntryWithSupply(ServiceSupplyItem item, int roomId)
        {
            // Conversation UI requires free hands. Put the real body down, ask permission,
            // then reacquire that same dropped identity through the ordinary grab raycast.
            yield return ServiceGrabEdge();
            Require(item.State.Location == ServiceItemLocation.Dropped, "put supply down before guest conversation");
            var knock = FindObjectsByType<RoomNoiseInteraction>(FindObjectsSortMode.None).Single(k => k.roomId == roomId);
            yield return AimServicePoint(() => knock.GetComponent<Collider>().bounds.center);
            yield return PressMenu(GamepadButton.South); yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsGuestContextOpen, "real knock and conversation while delivery waits outside");
            var choices = ReadMenuField<List<(string title, Action action)>>(ManagementUI.Instance, "contextChoices");
            int index = choices.FindIndex(c => c.title == "Ask permission to enter");
            Require(index >= 0, "guest offers permission to enter");
            yield return NavigateServiceMenu(index); yield return PressMenu(GamepadButton.South);
            var door = knock.GetComponentInParent<DoorInteractable>();
            yield return Until(() => !ManagementUI.Instance.IsOpen && door.IsPassageOpen, 3, "permission opens the actual room doorway");
            yield return GrabServiceSupply(item);
        }
    }
}
#endif
