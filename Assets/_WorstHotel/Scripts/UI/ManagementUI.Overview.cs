using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        string OverviewOccupancy => Session.Rooms.Count(room => room.Occupied) + "/" + Session.Rooms.Length +
            " occupied · " + Session.Rooms.Count(OverviewRoomReady) + " vacant rooms ready";

        static bool OverviewRoomReady(RoomState room) => !room.Occupied && string.IsNullOrEmpty(room.DepartingGuestId) &&
            room.Cleanliness == Cleanliness.Clean && room.TurnoverState != HousekeepingState.Moving && room.TurnoverState != HousekeepingState.Cleaning;

        string OverviewRoomLine(RoomState room)
        {
            string state = room.Occupied ? "Occupied" : !string.IsNullOrEmpty(room.DepartingGuestId) ? "Guest leaving" :
                OverviewRoomReady(room) ? "Ready" : "Needs linen";
            var task = Session.Simulation.Housekeeping?.Find(room.Profile.Id);
            if (!room.Occupied && string.IsNullOrEmpty(room.DepartingGuestId) && task?.Step == RoomPreparationStep.MakingBed)
                state = "Bed making " + Mathf.RoundToInt(task.Progress01 * 100) + "%";
            return room.Profile.Id + " · " + state + " · " + room.Temperature.ToString("F1") + "°C";
        }

        IEnumerable<(float time, string label)> OverviewMovements()
        {
            var model = Session.Simulation;
            var next = new List<(float time, string label)>();
            foreach (var reservation in model.Reservations.Where(value => value.Status == ReservationStatus.Reserved))
                next.Add((reservation.Offer.ArrivalAt, "IN · " + reservation.RoomId + " · " + reservation.Offer.Application.GuestName));
            foreach (var guest in model.Guests.Where(value => value.Agent?.State == GuestAgentState.WaitingForCheckIn))
                next.Add((model.Elapsed, "HERE · " + guest.RoomId + " · " + guest.Name + " · awaiting key"));
            foreach (var guest in model.Guests.Where(value => !value.ReceiptPosted && value.Agent?.CheckedIn == true &&
                value.Agent.State != GuestAgentState.CheckingOut && value.Agent.State != GuestAgentState.Leaving && value.Agent.State != GuestAgentState.Left))
                next.Add((guest.Agent.CheckoutTime, "OUT · " + guest.RoomId + " · " + guest.Name));
            return next.OrderBy(value => value.time);
        }

        string OverviewHeating()
        {
            var model = Session.Simulation; var boiler = model.Boiler;
            string state = boiler.MaintenanceInProgress ? "OFF · maintenance until " + GuestLabels.HotelMoment(model, boiler.MaintenanceEndsAt) :
                boiler.Failed ? "FAILED · reduced heat" : CapacityLabels.Band(boiler.CapacityBand) + (boiler.EmergencyPatchActive ? " · PATCHED" : "");
            return "HEATING · " + state + " · boiler " + boiler.Condition.ToString("F0") + "%";
        }

        static string OverviewCircuit(ElectricalCircuit circuit)
        {
            if (circuit == null) return "Unavailable";
            if (circuit.Tripped) return "TRIPPED · power off";
            return circuit.CapacityBand == CapacityBand.Comfortable ? "Normal" : circuit.CapacityBand == CapacityBand.Busy ? "Busy" : circuit.CapacityBand == CapacityBand.Strained ?
                "Near limit" : circuit.CapacityBand == CapacityBand.Overloaded ? "Overloaded" : "High stress";
        }

        string OverviewPower => "POWER · A: " + OverviewCircuit(Session.Simulation.Electrical?.Find("A")) +
            "   /   B: " + OverviewCircuit(Session.Simulation.Electrical?.Find("B"));

        string OverviewBacklog()
        {
            var boiler = Session.Simulation.Boiler;
            var notes = new List<string>();
            if (boiler.MaintenanceInProgress) notes.Add("planned downtime");
            else if (boiler.Failed) notes.Add("failed");
            else if (boiler.Condition < 50) notes.Add("poor");
            if (boiler.EmergencyPatchActive) notes.Add("patched");
            if (boiler.Stress01 >= .5f) notes.Add("high stress");
            int beds = Session.Rooms.Count(room => !room.Occupied && room.Cleanliness != Cleanliness.Clean);
            int lamps = Session.Rooms.Count(room => room.LampBroken);
            if (notes.Count == 0 && beds == 0 && lamps == 0) return "MAINTENANCE · No recorded backlog";
            // Explicit short lines keep even poor + patch + stress + all rooms readable.
            return "MAINTENANCE · Boiler: " + (notes.Count > 0 ? string.Join(" / ", notes) : "no recorded backlog") +
                "\nRooms: " + beds + " need linen / " + lamps + " lamps out";
        }

        // The text is the same factual projection rendered below; opening it never changes hotel state.
        public string DisplayedOperationsOverview => !IsOperationsOpen || operationsPage != OperationsPage.Overview ? null :
            OverviewOccupancy + "\n" + string.Join("\n", Session.Rooms.Select(OverviewRoomLine)) + "\n" +
            string.Join("\n", OverviewMovements().Take(5).Select(value => value.label)) + "\n" +
            OverviewHeating() + "\n" + OverviewPower + "\n" + OverviewBacklog();

        void DrawOperationsOverview()
        {
            var model = Session.Simulation;
            Label(new Rect(42, 252, 342, 29), "ROOMS", Heading, Teal);
            Label(new Rect(405, 252, 342, 29), "NEXT IN & OUT", Heading, Teal);
            Label(new Rect(42, 283, 342, 26), OverviewOccupancy, Small, Muted);
            for (int index = 0; index < Session.Rooms.Length; index++)
            {
                var room = Session.Rooms[index];
                Label(new Rect(42, 312 + index * 28, 342, 26), OverviewRoomLine(room), Small,
                    !room.Occupied && !OverviewRoomReady(room) ? Wine : Ink);
            }
            int row = 0;
            foreach (var item in OverviewMovements().Take(5))
                Label(new Rect(405, 288 + row++ * 39, 342, 38), item.label + "\n" + GuestLabels.HotelMoment(model, item.time), Small);
            if (row == 0) Label(new Rect(405, 294, 342, 66), "No scheduled arrivals or checkouts.\nApplications are available above.", Small, Muted);
            Fill(new Rect(42, 493, 705, 132), LightPaper);
            Label(new Rect(55, 500, 679, 28), OverviewHeating(), Small, model.Boiler.Failed || model.Boiler.MaintenanceInProgress ? Wine : Teal);
            Label(new Rect(55, 529, 679, 26), OverviewPower, Small, Ink);
            Label(new Rect(55, 555, 679, 40), OverviewBacklog(), Small, Muted);
            Label(new Rect(55, 599, 679, 24), "Next report " + GuestLabels.HotelMoment(model, model.NextReportAt) +
                " · operating bill $" + Session.Economy.DailyOperatingCost + " · " + Session.Reports.Count + " reports", Small, Muted);
        }
    }
}
