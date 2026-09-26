using System;
using System.Collections.Generic;
using System.Linq;

namespace WorstHotel
{
    public sealed partial class PlanningSystem
    {
        public IReadOnlyList<RoomState> Rooms { get; }
        public IReadOnlyList<BookingApplication> Applications { get; }
        public IReadOnlyList<BookingAssignment> Assignments => Array.AsReadOnly(assignments.Values.OrderBy(a => a.RoomId).ToArray());
        public bool IsCommitted { get; private set; }
        public int ProjectedGross => assignments.Values.Sum(assignment => assignment.Price);
        public float ProjectedLoad => assignments.Values.Sum(assignment => applications[assignment.BookingId].Archetype.HeatingDemand);
        public bool IsOverSafeLoad => ProjectedLoad > boiler.SafeLoad;
        public event Action Changed;

        private readonly Dictionary<int, RoomState> rooms;
        private readonly Dictionary<string, BookingApplication> applications;
        private readonly Dictionary<int, BookingAssignment> assignments = new Dictionary<int, BookingAssignment>();
        private readonly EconomySettings economy;
        private readonly BoilerSettings boiler;

        public PlanningSystem(IEnumerable<RoomState> roomStates, IEnumerable<BookingApplication> bookingApplications,
            EconomySettings economySettings, BoilerSettings boilerSettings)
        {
            if (roomStates == null || bookingApplications == null) throw new ArgumentNullException("Rooms and applications are required.");
            economy = economySettings ?? throw new ArgumentNullException(nameof(economySettings));
            boiler = boilerSettings ?? throw new ArgumentNullException(nameof(boilerSettings));
            rooms = roomStates.ToDictionary(room => room.Profile.Id);
            applications = bookingApplications.ToDictionary(application => application.Id);
            Rooms = Array.AsReadOnly(rooms.Values.OrderBy(room => room.Profile.Id).ToArray());
            Applications = Array.AsReadOnly(applications.Values.ToArray());
        }

        public bool TryGetAssignment(int roomId, out BookingAssignment assignment) => assignments.TryGetValue(roomId, out assignment);

        public CommandResult Assign(int actorId, string bookingId, int roomId, int price)
        {
            var gate = ValidateActorAndPlan(actorId);
            if (!gate.Success) return gate;
            if (bookingId == null || !applications.ContainsKey(bookingId)) return CommandResult.Fail("Select a valid booking application.");
            if (!rooms.TryGetValue(roomId, out var room)) return CommandResult.Fail("Select a valid room.");
            if (room.Occupied) return CommandResult.Fail("This room is occupied.");
            if (assignments.ContainsKey(roomId)) return CommandResult.Fail("Release the room's current booking before assigning another guest.");
            if (assignments.Values.Any(assignment => assignment.BookingId == bookingId)) return CommandResult.Fail("This guest already has a room.");
            var priceCheck = ValidatePrice(price);
            if (!priceCheck.Success) return priceCheck;
            assignments.Add(roomId, new BookingAssignment(roomId, bookingId, price, actorId));
            Changed?.Invoke();
            return CommandResult.Ok(room.Cleanliness == Cleanliness.Dirty ?
                "Room " + roomId + " reserved in the plan, but it is dirty. Cleaning must finish before the guest can check in." :
                !string.IsNullOrEmpty(room.DepartingGuestId) ?
                "Room " + roomId + " reserved in the plan. The previous guest must physically leave before check-in." :
                "Guest accepted into room " + roomId + ".");
        }

        public CommandResult Remove(int actorId, int roomId)
        {
            var gate = ValidateActorAndPlan(actorId);
            if (!gate.Success) return gate;
            if (!assignments.Remove(roomId)) return CommandResult.Fail("This room has no accepted booking.");
            Changed?.Invoke();
            return CommandResult.Ok("Booking released.");
        }

        public CommandResult SetPrice(int actorId, int roomId, int price)
        {
            var gate = ValidateActorAndPlan(actorId);
            if (!gate.Success) return gate;
            if (!assignments.TryGetValue(roomId, out var assignment)) return CommandResult.Fail("Accept a guest before changing the agreed price.");
            var priceCheck = ValidatePrice(price);
            if (!priceCheck.Success) return priceCheck;
            assignments[roomId] = new BookingAssignment(roomId, assignment.BookingId, price, actorId);
            Changed?.Invoke();
            return CommandResult.Ok("Room price updated.");
        }

        public bool CanCommit(out string error)
        {
            if (IsCommitted) { error = "This plan has already been committed."; return false; }
            if (assignments.Count == 0) { error = "Accept at least one booking to open the hotel."; return false; }
            if (assignments.Values.Any(assignment => rooms[assignment.RoomId].Occupied))
            { error = "An assigned room is occupied. Release that booking first."; return false; }
            error = string.Empty;
            return true;
        }

        public bool TryCommit(int actorId, out BookingAssignment[] committedAssignments, out string error)
        {
            committedAssignments = Array.Empty<BookingAssignment>();
            var gate = ValidateActorAndPlan(actorId);
            if (!gate.Success) { error = gate.Message; return false; }
            if (!CanCommit(out error)) return false;
            committedAssignments = assignments.Values.OrderBy(assignment => assignment.RoomId).ToArray();
            IsCommitted = true;
            Changed?.Invoke();
            return true;
        }

        private CommandResult ValidateActorAndPlan(int actorId)
        {
            if (IsReadOnlyMirror) return CommandResult.Fail(HotelSimulation.MirrorMessage);
            if (actorId < 0 || actorId > 1) return CommandResult.Fail("Unknown staff actor.");
            if (IsCommitted) return CommandResult.Fail("The accepted plan is locked for this shift.");
            return CommandResult.Ok();
        }

        private CommandResult ValidatePrice(int price)
        {
            if (price < economy.MinPrice || price > economy.MaxPrice || (price - economy.MinPrice) % economy.PriceStep != 0)
                return CommandResult.Fail("Price must be $" + economy.MinPrice + "–$" + economy.MaxPrice + " in $" + economy.PriceStep + " steps.");
            return CommandResult.Ok();
        }
    }
}


