using UnityEngine;

namespace WorstHotel
{
    [DefaultExecutionOrder(100), RequireComponent(typeof(Rigidbody), typeof(PhysicsPickup))]
    public sealed class PortableHeater : HotelInteractable
    {
        public string heaterId = "portable-heater-1";
        public RoomVolumeRegistry roomVolumes;
        public BoxCollider placementCollider;
        public TextMesh statusLabel;
        public Renderer heatGlow, statusLamp;
        public Transform switchLever;
        public Rigidbody Body { get; private set; }
        public bool IsCarried { get; private set; }
        public PortableHeaterState State => simulation?.Heaters?.Find(heaterId);

        GameSession session;
        HotelSimulation simulation;
        MaterialPropertyBlock glowProperties, lampProperties;
        int visualState = -1, visualRoom = -1;
        RoomState placedRoom;
        int placedRoomId = -1;
        bool HasAuthority => !LocalCoopBootstrap.Instance || LocalCoopBootstrap.Instance.HasWorldAuthority;

        void Awake()
        {
            Body = GetComponent<Rigidbody>();
            if (placementCollider == null) placementCollider = GetComponent<BoxCollider>();
            displayName = "Portable electric heater";
            glowProperties = new MaterialPropertyBlock(); lampProperties = new MaterialPropertyBlock();
        }

        void Update()
        {
            var authority = GameSession.Instance;
            var next = authority != null ? authority.Simulation : null;
            if (session != authority || !ReferenceEquals(simulation, next))
            {
                session = authority; simulation = next; visualState = -1; placedRoom = null; placedRoomId = -1;
                if (HasAuthority && session != null && simulation != null && simulation.LivingEnabled) session.RegisterHeater(heaterId);
            }
            var state = State;
            if (state == null) return;
            if (!HasAuthority)
            {
                // Replica model already contains the host's actual placement. Never resolve local physics.
                IsCarried = false;
                RefreshVisuals(state);
                return;
            }
            IsCarried = false;
            var coop = LocalCoopBootstrap.Instance;
            if (coop != null)
                foreach (var player in coop.Players)
                    if (player != null && player.Interactor != null && player.Interactor.HeldBody == Body) IsCarried = true;
            int? room = !IsCarried && roomVolumes != null && placementCollider != null && placementCollider.enabled ?
                roomVolumes.ResolveRoom(placementCollider.bounds) : null;
            if (room != state.RoomId) session.SetHeaterPlacement(heaterId, room);
            if (placedRoomId != (state.RoomId ?? -1))
            {
                placedRoomId = state.RoomId ?? -1;
                placedRoom = System.Array.Find(session.Rooms, item => item.Profile.Id == placedRoomId);
            }
            RefreshVisuals(state);
        }

        public override bool CanInteract(PlayerInteractor actor) => HasAuthority && base.CanInteract(actor) && actor != null &&
            session != null && State != null && !IsCarried &&
            (State.SwitchedOn || session.Phase == DayPhase.Service);

        public override void Interact(PlayerInteractor actor)
        {
            if (!CanInteract(actor)) return;
            session.SetHeaterSwitch(actor.ActorId, heaterId, !State.SwitchedOn);
            RefreshVisuals(State);
        }

        public override string GetPrompt(PlayerInteractor actor)
        {
            var state = State;
            if (state == null) return "Heater unavailable";
            if (!state.SwitchedOn && session.Phase != DayPhase.Service) return "Switched off · Power available during service\nCarry to move";
            if (HasAuthority && IsCarried) return "Carried · Heat paused\nPut down fully inside a room";
            if (!state.RoomId.HasValue)
                return (state.SwitchedOn ? "Switch off" : "Switch on") + " · Outside a room\nNo local heat or room circuit load";
            // The host's registered placement also feeds LAN captions; never sample replica physics.
            var circuit = simulation.Electrical?.CircuitForRoom(state.RoomId.Value);
            if (circuit != null)
            {
                string load = "Load " + circuit.RequestedLoad.ToString("0.##") + "/" + circuit.Capacity.ToString("0.##");
                if (!state.SwitchedOn)
                {
                    float added = state.Settings.ElectricalLoad;
                    string effect = circuit.LoadOverride.HasValue ? " actual; test load" :
                        (double)circuit.RequestedLoad + added > circuit.Capacity ? " would overload" : " within limit";
                    return "Switch on · Room " + state.RoomId.Value + " · Circuit " + circuit.Id + "\n" +
                        (circuit.Tripped ? "No power · " : "") + load + " · +" + added.ToString("0.##") + effect;
                }
                return "Switch off · " + (state.Powered ? "Heating room " : "No power in room ") + state.RoomId.Value + "\n" +
                    "Circuit " + circuit.Id + " · " + load + (circuit.LoadOverride.HasValue ? " · test load" :
                        circuit.RequestedLoad > circuit.Capacity ? " · overloaded" : "");
            }
            string status = IsCarried ? "Carried · Heat paused" : !state.SwitchedOn ? "Switched off" :
                !state.RoomId.HasValue ? "Outside a room · No local heat" : !state.Powered ? "No electrical power" :
                "Heating " + state.RoomId.Value + (placedRoom != null ? " · " + placedRoom.Temperature.ToString("F1") + "°C" : "") + " · manual heat";
            if (state.EffectiveHeatOutput > 0 && !IsCarried) return status + "\nSwitch off when warm · Carry to move";
            return status + "\n" + (state.SwitchedOn ? "Switch off" : "Switch on") + " · Carry to move";
        }

        void RefreshVisuals(PortableHeaterState state)
        {
            if (state == null) return;
            int mode = !state.SwitchedOn ? 0 : IsCarried ? 1 : !state.RoomId.HasValue ? 2 : !state.Powered ? 3 : 4;
            int room = state.RoomId ?? -1;
            if (mode == visualState && room == visualRoom) return;
            visualState = mode; visualRoom = room;
            if (statusLabel != null) statusLabel.text = state.SwitchedOn ? "ON" : "OFF";
            if (switchLever != null) switchLever.localRotation = Quaternion.Euler(state.SwitchedOn ? -24 : 24, 0, 0);
            Color signal = mode == 4 ? new Color(1, .42f, .09f) : mode == 0 ? new Color(.09f, .12f, .10f) : new Color(.72f, .5f, .10f);
            if (statusLamp != null)
            {
                statusLamp.GetPropertyBlock(lampProperties);
                lampProperties.SetColor("_BaseColor", signal);
                lampProperties.SetColor("_EmissionColor", mode == 0 ? Color.black : signal);
                statusLamp.SetPropertyBlock(lampProperties);
            }
            if (heatGlow != null)
            {
                heatGlow.GetPropertyBlock(glowProperties);
                glowProperties.SetColor("_BaseColor", mode == 4 ? new Color(.95f, .16f, .035f) : new Color(.10f, .065f, .04f));
                glowProperties.SetColor("_EmissionColor", mode == 4 ? new Color(1, .23f, .02f) * 1.6f : Color.black);
                heatGlow.SetPropertyBlock(glowProperties);
            }
        }

        void OnDisable()
        {
            if (HasAuthority && session != null && ReferenceEquals(session.Simulation, simulation)) session.UnregisterHeater(heaterId);
            if (statusLabel != null) statusLabel.text = "OFFLINE";
            if (heatGlow != null && glowProperties != null)
            {
                glowProperties.SetColor("_BaseColor", new Color(.10f, .065f, .04f));
                glowProperties.SetColor("_EmissionColor", Color.black);
                heatGlow.SetPropertyBlock(glowProperties);
            }
            if (statusLamp != null && lampProperties != null)
            {
                lampProperties.SetColor("_BaseColor", new Color(.09f, .12f, .10f));
                lampProperties.SetColor("_EmissionColor", Color.black);
                statusLamp.SetPropertyBlock(lampProperties);
            }
            simulation = null; session = null; IsCarried = false; visualState = -1;
        }
    }
}
