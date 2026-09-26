using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Small authored-world replica. Only the host runs collision, carrying and guest routes.</summary>
    public sealed class LanWorldReplicator : MonoBehaviour
    {
        sealed class BodyState
        {
            public Rigidbody body;
            public bool kinematic, gravity;
            public RigidbodyConstraints constraints;
            public RigidbodyInterpolation interpolation;
        }
        readonly Dictionary<string, Transform> objects = new Dictionary<string, Transform>();
        readonly Dictionary<string, DoorInteractable> doors = new Dictionary<string, DoorInteractable>();
        readonly Dictionary<string, Renderer> visuals = new Dictionary<string, Renderer>();
        readonly Dictionary<string, TextMesh> texts = new Dictionary<string, TextMesh>();
        readonly Dictionary<string, Light> lights = new Dictionary<string, Light>();
        readonly Dictionary<Behaviour, bool> originalEnabled = new Dictionary<Behaviour, bool>();
        readonly List<BodyState> bodies = new List<BodyState>();
        readonly Dictionary<string, LanGuestView> guestViews = new Dictionary<string, LanGuestView>();
        readonly HashSet<string> visibleGuests = new HashSet<string>();
        readonly List<string> removedGuests = new List<string>();
        MaterialPropertyBlock properties;
        GuestPresentation guestPresentation;
        Transform replicaGuests;
        bool cached;
        LanRole role;
        long appliedEpoch = -1, appliedSequence = -1;
        float lastAppliedAt;
        bool replicaStale;
        public int ReplicatedObjectCount => objects.Count;
        public int ReplicaGuestCount => guestViews.Count;
        public long AppliedSequence => appliedSequence;

        void Awake() => properties = new MaterialPropertyBlock();

        public void Configure(LanRole next)
        {
            CacheScene();
            if (role == next) return;
            bool wasClient = role == LanRole.Client;
            role = next;
            if (wasClient)
            {
                foreach (var state in bodies) if (state.body)
                { state.body.isKinematic = state.kinematic; state.body.useGravity = state.gravity;
                    state.body.constraints = state.constraints; state.body.interpolation = state.interpolation; }
                foreach (var pair in originalEnabled) if (pair.Key) pair.Key.enabled = pair.Value;
                originalEnabled.Clear();
                ClearGuestViews();
            }
            if (role == LanRole.Client)
            {
                // Cache once. Authoritative components stay disabled even between snapshots or on disconnect.
                foreach (var root in gameObject.scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        if (IsAuthorityDriver(component))
                        { originalEnabled[component] = component.enabled; component.enabled = false; }
                foreach (var state in bodies) if (state.body)
                { state.body.isKinematic = true; state.body.useGravity = false; state.body.interpolation = RigidbodyInterpolation.None; }
            }
            appliedEpoch = appliedSequence = -1;
        }

        static bool IsAuthorityDriver(MonoBehaviour component) => component && !(component is PortableHeater) &&
            (component is HotelInteractable || component is PhysicalCarryItem || component is GuestPresentation ||
             component is HousekeeperPresentation || component is RepairSequenceController);

        void CacheScene()
        {
            if (cached) return;
            cached = true;
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                {
                    AddObject(body.transform);
                    bodies.Add(new BodyState { body = body, kinematic = body.isKinematic, gravity = body.useGravity,
                        constraints = body.constraints, interpolation = body.interpolation });
                    foreach (var renderer in body.GetComponentsInChildren<Renderer>(true)) AddVisual(renderer);
                }
                foreach (var label in root.GetComponentsInChildren<TextMesh>(true)) texts[Id(label.transform)] = label;
                foreach (var light in root.GetComponentsInChildren<Light>(true)) lights[Id(light.transform)] = light;
                foreach (var door in root.GetComponentsInChildren<DoorInteractable>(true))
                { AddObject(door.doorPivot); if (door.doorPivot) doors[Id(door.doorPivot)] = door; }
                foreach (var bed in root.GetComponentsInChildren<LinenBedInteraction>(true))
                    if (bed.madeBedPieces != null) foreach (var piece in bed.madeBedPieces) AddVisualTree(piece);
                foreach (var heater in root.GetComponentsInChildren<PortableHeater>(true))
                { AddObject(heater.switchLever); AddVisual(heater.heatGlow); AddVisual(heater.statusLamp); }
                foreach (var panel in root.GetComponentsInChildren<ElectricalPanelPresentation>(true))
                {
                    foreach (var circuit in panel.circuits) { AddObject(circuit.lever); AddVisual(circuit.warningLens); }
                    foreach (var room in panel.roomLights) foreach (var surface in room.luminousSurfaces) AddVisual(surface);
                }
                foreach (var radiator in root.GetComponentsInChildren<RadiatorHeatFeedback>(true))
                    if (radiator.fins != null) foreach (var fin in radiator.fins) AddVisual(fin);
                foreach (var valve in root.GetComponentsInChildren<RadiatorValveInteraction>(true)) AddObject(valve.knob);
                foreach (var lamp in root.GetComponentsInChildren<RoomLampInteraction>(true)) AddVisual(lamp.bulbSurface);
                foreach (var blanket in root.GetComponentsInChildren<RoomBlanketDeliveryInteraction>(true)) AddVisualTree(blanket.deliveredBlanket);
                foreach (var control in root.GetComponentsInChildren<RepairControl>(true)) AddObject(control.movingPart);
                foreach (var readout in root.GetComponentsInChildren<BoilerReadout>(true))
                { AddObject(readout.needle); AddVisual(readout.warningLens); }
                foreach (var noise in root.GetComponentsInChildren<RoomNoiseInteraction>(true)) AddObject(noise.knocker);
                foreach (var feedback in root.GetComponentsInChildren<HotelFeedback>(true))
                { AddObject(feedback.vibratingPipe); AddObject(feedback.phoneReceiver); AddVisual(feedback.phoneLens); }
                var presentation = root.GetComponentInChildren<GuestPresentation>(true);
                if (presentation)
                {
                    guestPresentation = presentation;
                    if (presentation.roomMarkers != null)
                        foreach (var room in presentation.roomMarkers)
                        { AddVisualTree(room.showerWater); AddVisualTree(room.showerCurtain); AddVisualTree(room.loudIndicator); }
                }
            }
        }

        void AddObject(Transform value) { if (value) objects[Id(value)] = value; }
        void AddVisual(Renderer value) { if (value) visuals[Id(value.transform)] = value; }
        void AddVisualTree(GameObject value)
        {
            if (!value) return;
            AddObject(value.transform);
            foreach (var renderer in value.GetComponentsInChildren<Renderer>(true)) AddVisual(renderer);
        }

        public static string StableObjectId(Transform value) => value ? Id(value) : null;
        static string Id(Transform value)
        {
            var pickup = value.GetComponent<PhysicsPickup>();
            if (pickup)
            {
                if (value.TryGetComponent<RoomKeyItem>(out var key)) return "key:" + key.roomId;
                if (value.TryGetComponent<LinenBundleItem>(out var linen)) return "linen:" + linen.itemId;
                if (value.TryGetComponent<PortableHeater>(out var heater)) return "heater:" + heater.heaterId;
            }
            string path = value.name;
            while (value.parent != null)
            { path = value.GetSiblingIndex() + ":" + path; value = value.parent; path = value.name + "/" + path; }
            // Stable across identical scenes/builds, unlike GetInstanceID. IDs are never supplied as commands.
            ulong hash = 14695981039346656037UL;
            foreach (char c in path) { hash ^= c; hash *= 1099511628211UL; }
            return hash.ToString("x16");
        }

        public LanWorldFrame Capture(long epoch, long sequence)
        {
            CacheScene();
            if (role == LanRole.Client) return null;
            var frame = new LanWorldFrame { epoch = epoch, sequence = sequence };
            var coop = LocalCoopBootstrap.Instance;
            var playerFrames = new List<LanWorldPlayer>(2);
            if (coop) foreach (var player in coop.Players)
            {
                if (!player || !player.PlayerCamera) continue;
                var actor = player.Interactor;
                bool usable = actor.Focused && actor.Focused.CanInteract(actor);
                playerFrames.Add(new LanWorldPlayer
                {
                    actorId = player.ActorId, position = player.transform.position, rotation = player.transform.rotation,
                    cameraLocalRotation = player.PlayerCamera.transform.localRotation, movement = player.IsUIBlocked ? Vector2.zero : player.Input.Move,
                    usingHands = actor.IsInteracting || actor.HeldBody, uiBlocked = player.IsUIBlocked,
                    caption = Caption(player, usable), usable = usable, pickup = actor.FocusedPickup,
                    heldId = actor.HeldBody ? Id(actor.HeldBody.transform) : null
                });
            }
            frame.players = playerFrames.ToArray();
            var objectFrames = new List<LanWorldObject>(objects.Count);
            foreach (var pair in objects) if (pair.Value)
            {
                bool door = doors.TryGetValue(pair.Key, out var control) && control;
                objectFrames.Add(new LanWorldObject { id = pair.Key, position = pair.Value.position,
                    rotation = pair.Value.rotation, active = pair.Value.gameObject.activeSelf,
                    isDoor = door, doorOpen = door && control.IsOpen });
            }
            frame.objects = objectFrames.ToArray();
            var visualFrames = new List<LanWorldVisual>(visuals.Count);
            foreach (var pair in visuals) if (pair.Value)
            {
                properties.Clear(); pair.Value.GetPropertyBlock(properties);
                bool baseColor = properties.HasColor("_BaseColor"), emission = properties.HasColor("_EmissionColor");
                visualFrames.Add(new LanWorldVisual { id = pair.Key, enabled = pair.Value.enabled,
                    active = pair.Value.gameObject.activeSelf, hasColor = baseColor, hasEmission = emission,
                    color = baseColor ? properties.GetColor("_BaseColor") : Color.white,
                    emission = emission ? properties.GetColor("_EmissionColor") : Color.black });
            }
            frame.visuals = visualFrames.ToArray();
            var textFrames = new List<LanWorldText>(texts.Count);
            foreach (var pair in texts) if (pair.Value) textFrames.Add(new LanWorldText
            { id = pair.Key, text = pair.Value.text, color = pair.Value.color });
            frame.texts = textFrames.ToArray();
            var lightFrames = new List<LanWorldLight>(lights.Count);
            foreach (var pair in lights) if (pair.Value) lightFrames.Add(new LanWorldLight
            { id = pair.Key, enabled = pair.Value.enabled, intensity = pair.Value.intensity, color = pair.Value.color });
            frame.lights = lightFrames.ToArray();
            frame.guests = guestPresentation ? guestPresentation.CaptureLanGuests() : Array.Empty<LanWorldGuest>();
            return frame;
        }

        static string Caption(FirstPersonController player, bool usable)
        {
            var actor = player.Interactor;
            if (actor.HeldBody && actor.Focused && actor.Focused.AllowsHeldItem(actor))
                return actor.Focused.displayName + "\n" + (usable ? "[" + player.Input.PrimaryLabel + "]  " : "") +
                    actor.Focused.GetPrompt(actor) + "\n[" + player.Input.GrabLabel + "]  Put down";
            if (actor.HeldBody) return "[" + player.Input.GrabLabel + "]  Put down  ·  " + actor.HeldBody.name;
            if (actor.Focused) return actor.Focused.displayName + "\n" + (usable ? "[" + player.Input.PrimaryLabel + "]  " : "") + actor.Focused.GetPrompt(actor);
            return actor.FocusedPickup ? actor.FocusedPickup.itemName + "\n[" + player.Input.GrabLabel + "]  Carry" : null;
        }

        public bool Apply(LanWorldFrame frame)
        {
            CacheScene();
            if (role != LanRole.Client || frame == null || frame.epoch < appliedEpoch ||
                (frame.epoch == appliedEpoch && frame.sequence <= appliedSequence)) return false;
            if (frame.epoch != appliedEpoch) ClearGuestViews();
            appliedEpoch = frame.epoch; appliedSequence = frame.sequence;
            lastAppliedAt = Time.unscaledTime; replicaStale = false;
            var coop = LocalCoopBootstrap.Instance;
            if (coop && frame.players != null) foreach (var state in frame.players)
            {
                if (state == null || state.actorId < 0 || state.actorId >= coop.Players.Length) continue;
                var player = coop.Players[state.actorId]; if (!player) continue;
                player.ApplyReplicaPose(state.position, state.rotation, state.cameraLocalRotation, state.movement, state.usingHands);
                player.Interactor.ApplyReplicaPrompt(state.caption, state.usable, state.pickup);
            }
            if (frame.objects != null) foreach (var state in frame.objects)
                if (state != null && state.id != null && objects.TryGetValue(state.id, out var target) && target)
                {
                    target.SetPositionAndRotation(state.position, state.rotation);
                    if (target.gameObject.activeSelf != state.active) target.gameObject.SetActive(state.active);
                    if (state.isDoor && doors.TryGetValue(state.id, out var door) && door) door.ApplyReplicaState(state.doorOpen);
                }
            if (frame.visuals != null) foreach (var state in frame.visuals)
                if (state != null && state.id != null && visuals.TryGetValue(state.id, out var target) && target)
                {
                    target.enabled = state.enabled;
                    if (target.gameObject.activeSelf != state.active) target.gameObject.SetActive(state.active);
                    properties.Clear();
                    if (state.hasColor) properties.SetColor("_BaseColor", state.color);
                    if (state.hasEmission) properties.SetColor("_EmissionColor", state.emission);
                    target.SetPropertyBlock(properties);
                }
            if (frame.texts != null) foreach (var state in frame.texts)
                if (state != null && state.id != null && texts.TryGetValue(state.id, out var target) && target)
                { target.text = state.text; target.color = state.color; }
            if (frame.lights != null) foreach (var state in frame.lights)
                if (state != null && state.id != null && lights.TryGetValue(state.id, out var target) && target)
                { target.enabled = state.enabled; target.intensity = state.intensity; target.color = state.color; }
            ApplyGuests(frame.guests);
            return true;
        }

        void ApplyGuests(LanWorldGuest[] states)
        {
            if (!guestPresentation) return;
            if (!replicaGuests)
            { replicaGuests = new GameObject("LAN guest views (host poses)").transform; replicaGuests.SetParent(transform, false); }
            visibleGuests.Clear();
            if (states != null) foreach (var state in states)
            {
                if (state == null || string.IsNullOrEmpty(state.id) || !visibleGuests.Add(state.id)) continue;
                if (!guestViews.TryGetValue(state.id, out var view))
                { view = guestPresentation.CreateLanReplica(replicaGuests, state); guestViews.Add(state.id, view); }
                view.Apply(state);
            }
            removedGuests.Clear();
            foreach (var pair in guestViews) if (!visibleGuests.Contains(pair.Key)) removedGuests.Add(pair.Key);
            foreach (var id in removedGuests)
            {
                var view = guestViews[id];
                guestPresentation.ReleaseLanReplica(id, view.root);
                if (view.root) Destroy(view.root.gameObject); guestViews.Remove(id);
            }
        }

        void ClearGuestViews()
        {
            foreach (var pair in guestViews)
            {
                if (guestPresentation) guestPresentation.ReleaseLanReplica(pair.Key, pair.Value.root);
                if (pair.Value.root) Destroy(pair.Value.root.gameObject);
            }
            guestViews.Clear();
        }
        void LateUpdate()
        {
            if (role != LanRole.Client || replicaStale || Time.unscaledTime - lastAppliedAt <= .5f) return;
            replicaStale = true;
            var coop = LocalCoopBootstrap.Instance;
            if (coop) foreach (var player in coop.Players) if (player)
            {
                player.StopReplicaMotion();
                player.Interactor.ApplyReplicaPrompt(null, false, false);
            }
        }
        void OnDestroy() { ClearGuestViews(); if (replicaGuests) Destroy(replicaGuests.gameObject); }
    }
}
