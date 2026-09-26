using System;
using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    /// <summary>Local authority/composition root. Presentation submits actor-stamped commands here.</summary>
    [DefaultExecutionOrder(-400)]
    public sealed partial class GameSession : MonoBehaviour
    {
        public static GameSession Instance { get; private set; }
        public SessionConfig config;
        public WaitController Wait { get; private set; }
        public RoomState[] Rooms { get; private set; }
        public GuestProfile[] GuestProfiles { get; private set; }
        public PlanningSystem Plan { get; private set; }
        public EconomySettings Economy { get; private set; }
        public BoilerSettings BoilerSettings { get; private set; }
        public int Day { get; private set; } = 1;
        public float Cash { get; private set; }
        public BookingAssignment[] CommittedBookings { get; private set; } = Array.Empty<BookingAssignment>();
        public bool PlanCommitted { get; private set; }
        public event Action Changed;
        public string LastMessage { get; private set; } = "Choose guests, then assign rooms. Nothing is accepted until you commit.";

        void Awake()
        {
            Instance = this;
            Wait = GetComponent<WaitController>();
            if (config == null) { Debug.LogError("GameSession requires generated SessionConfig. Rebuild the prototype scene."); enabled = false; return; }
            NewGame();
        }

        public void NewGame()
        {
            if (LanSession.Instance && LanSession.Instance.IsClientReplica && !buildingReplica) return;
            if (!buildingReplica && LanSession.Instance) LanSession.Instance.NotifyHostNewGame();
            if (Wait) Wait.Stop("New session");
            Day = 1;
            Economy = config.economy.ToData();
            BoilerSettings = config.boiler.ToData();
            Cash = Economy.StartingCash;
            GuestProfiles = config.guestArchetypes.Select(x => x.ToData()).ToArray();
            Rooms = config.rooms.Select(x => new RoomState(x.ToData())).ToArray();
            InitializeService();
            RefreshSoloConfiguration();
            OpenPlanning();
            if (Simulation.ContinuousOperations)
            {
                var opened = Simulation.StartOperations();
                if (!opened.Success) throw new InvalidOperationException(opened.Message);
                Phase = DayPhase.Service;
                Day = Simulation.CalendarDay;
                LastMessage = opened.Message;
                RaiseChanged();
            }
            else LastMessage = "Choose guests, then assign rooms. Nothing is accepted until you commit.";
        }

        void OpenPlanning()
        {
            PlanCommitted = false;
            Phase = DayPhase.Planning;
            CommittedBookings = Array.Empty<BookingAssignment>();
            foreach (var room in Rooms) { room.GuestId = null; room.ReservedGuestId = null; }
            Plan = new PlanningSystem(Rooms, GuestSystem.GenerateApplications(Day, GuestProfiles,
                config.day3BusinessReferencePrice), Economy, BoilerSettings);
            RaiseChanged();
        }

        public void Assign(int actorId, string bookingId, int roomId, int price)
        { if (!ForwardLan(LanCommandKind.Assign, bookingId, roomId, price)) ReportCommand(Plan.Assign(actorId, bookingId, roomId, price)); }
        public void Remove(int actorId, int roomId)
        { if (!ForwardLan(LanCommandKind.Remove, room: roomId)) ReportCommand(Plan.Remove(actorId, roomId)); }
        public void SetPrice(int actorId, int roomId, int price)
        { if (!ForwardLan(LanCommandKind.SetPrice, room: roomId, amount: price)) ReportCommand(Plan.SetPrice(actorId, roomId, price)); }
        void ReportCommand(CommandResult result) { LastMessage = result.Message; RaiseChanged(); }
        public void CommitPlan(int actorId)
        {
            if (Simulation.ContinuousOperations)
            { ReportCommand(CommandResult.Fail("Continuous operations do not use a shift plan.")); return; }
            if (ForwardLan(LanCommandKind.CommitPlan)) return;
            if (Phase != DayPhase.Planning) return;
            if (!Plan.TryCommit(actorId, out var bookings, out var error)) { LastMessage = error; RaiseChanged(); return; }
            CommittedBookings = bookings;
            var result = Simulation.StartShift(bookings, Plan.Applications);
            if (!result.Success)
            {
                var applications = Plan.Applications;
                Plan = new PlanningSystem(Rooms, applications, Economy, BoilerSettings);
                foreach (var booking in bookings) Plan.Assign(actorId, booking.BookingId, booking.RoomId, booking.Price);
                LastMessage = result.Message; RaiseChanged(); return;
            }
            PlanCommitted = true;
            Phase = DayPhase.Service; accumulator = 0;
            LastMessage = "Guests are checking in. Their comfort now depends on the hotel you sold them.";
            RaiseChanged();
            if (ManagementUI.Instance != null) ManagementUI.Instance.Close();
        }
        public void RaiseChanged() => Changed?.Invoke();
        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
