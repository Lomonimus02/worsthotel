using System.Linq;
using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        public BookingLoadForecast DisplayedBookingForecast => IsOperationsOpen &&
            (operationsPage == OperationsPage.Offer || operationsPage == OperationsPage.Forecast) ?
            Session.Simulation.ForecastBookingLoad(operationsOfferId, selectedRoom) : null;
        public int DisplayedBookingPrice => Session.Simulation.AutomaticBookingsEnabled ?
            Session.Simulation.FindReservation(operationsOfferId)?.Price ?? operationsPrice : operationsPrice;
        public string DisplayedForecastCircuitState
        {
            get
            {
                var forecast = DisplayedBookingForecast;
                if (forecast?.Available != true) return "Forecast unavailable";
                var circuit = Session.Simulation.Electrical?.Find(forecast.CircuitId);
                return "CIRCUIT " + forecast.CircuitId + " · NOW " + (circuit?.Tripped == true ?
                    "TRIPPED · reset required" : circuit?.HasPower == true ? "POWER ON · " + OverviewCircuit(circuit) : "POWER OFF");
            }
        }

        string BookingForecastChoiceTitle()
        {
            var forecast = DisplayedBookingForecast;
            return forecast != null && forecast.Available ? "Forecast · " + CapacityLabels.Band(forecast.TypicalBand) +
                " typical / " + CapacityLabels.Percent(forecast.PeakRatio) + " with one shower ›" : "Forecast unavailable · details ›";
        }

        void DrawBookingForecast()
        {
            var model = Session.Simulation;
            var forecast = DisplayedBookingForecast;
            var offer = OperationsOffer(operationsOfferId);
            if (forecast == null || !forecast.Available)
            {
                Label(new Rect(42, 210, 705, 102), "Forecast unavailable\n" + (forecast?.Reason ?? "This booking is no longer available."), Body, Muted);
                return;
            }
            Label(new Rect(42, 198, 705, 70), (offer?.Application.GuestName ?? operationsOfferId) + " · room " + forecast.RoomId + " preview" +
                "\n" + GuestLabels.HotelMoment(model, forecast.ArrivalAt) + " → " + GuestLabels.HotelMoment(model, forecast.CheckoutAt), Body);
            Fill(new Rect(42, 282, 705, 209), LightPaper);
            Label(new Rect(58, 291, 673, 32), "HEATING · APPROXIMATE DEMAND", Heading, Teal);
            Label(new Rect(58, 335, 673, 142), "NOW, measured: " + forecast.CurrentDemand.ToString("F2") + " u · " + CapacityLabels.Band(forecast.CurrentBand) +
                " · " + BoilerMaintenanceLabels.State(model) +
                "\nDURING THIS STAY, including this booking and existing commitments:" +
                "\nTypical " + forecast.TypicalDemand.ToString("F2") + " u · " + CapacityLabels.Band(forecast.TypicalBand) + " · " + CapacityLabels.Percent(forecast.TypicalRatio) +
                "\nWith one shower " + forecast.OneShowerPeakDemand.ToString("F2") + " u · " + CapacityLabels.Percent(forecast.PeakRatio) +
                "\nCurrent effective capacity " + forecast.EffectiveCapacity.ToString("F2") + " u · up to " + forecast.MaxConcurrentGuests + " overlapping stays", Small);
            Label(new Rect(42, 500, 705, 27), DisplayedForecastCircuitState, Body, Session.Simulation.Electrical?.Find(forecast.CircuitId)?.Tripped == true ? Wine : Teal);
            Label(new Rect(42, 534, 705, 49), "Typical request " + forecast.TypicalCircuitDemand.ToString("F2") +
                " / " + forecast.CircuitCapacity.ToString("F2") + " u · reserve " + CapacityLabels.Reserve(forecast.CircuitReserve) + " u\n" +
                (model.AutomaticBookingsEnabled ? "Agreed room price $" : "Room price $") + DisplayedBookingPrice +
                " · paid at checkout; credits or refunds may reduce income.", Small);
            Label(new Rect(42, 586, 705, 105), "Estimate uses the present boiler condition, radiator settings and heater switches. Future guest activities, changed settings and repairs are not predicted." +
                "\n" + (model.AutomaticBookingsEnabled ? "The agreed price stays fixed. Future sale rates affect income and expectations, not this physical load estimate." :
                "Price changes income and expectations; it does not change this physical load estimate."), Small, Muted);
        }

        void DrawCurrentOperationsFinance()
        {
            var model = Session.Simulation;
            Label(new Rect(42, 196, 705, 34), "SINCE " + GuestLabels.HotelMoment(model, model.PeriodStartedAt), Heading, Teal);
            Label(new Rect(42, 240, 705, 143), "Opening cash $" + model.PeriodOpeningCash + " → cash now $" + model.Economy.Cash +
                "\nCollected at checkout $" + model.PeriodCheckoutIncome + " (room charges $" + model.PeriodGross + " − credits/refunds $" + model.PeriodCompensation + ")" +
                "\nAlready paid: maintenance $" + model.PeriodMaintenanceSpend + " · capital purchases $" + model.PeriodCapitalSpend +
                "\nBooked charges (all dates, unpaid) $" + model.UnpaidBookedRevenue + " · before future credits/refunds" +
                "\nNext operating charge $" + Session.Economy.DailyOperatingCost + " at " + GuestLabels.HotelMoment(model, model.NextReportAt) +
                (Session.Reports.Count == 0 ? "\nNo completed reports yet." : ""), Small, Muted);
        }
    }
}
