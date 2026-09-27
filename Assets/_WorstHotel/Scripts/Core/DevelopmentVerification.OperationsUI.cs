#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        bool operationsUI, operationsUIVerified;
        readonly HashSet<string> operationsImageHashes = new HashSet<string>();

        // Presentation fixture only: actual player rendering and owned controller navigation.
        // It deliberately makes no natural pacing, three-day or physical check-in claim.
        IEnumerator VerifyOperationsUI()
        {
            Require(soloTour && session.Simulation.ContinuousOperations && session.Simulation.AutomaticBookingsEnabled,
                "UI verification uses production continuous SOLO mode with automatic ordinary sales");
            driveSpeed = 1; currentDay = null;
            var production = session.config;
            var fixture = Instantiate(production);
            var economy = Instantiate(production.economy);
            var living = Instantiate(production.living);
            var services = Instantiate(production.services);
            economy.startingCash = 4000;
            living.firstActivityDelay = 1000;
            services.eligibility = 0;
            services.selfResponseObserveSeconds = services.toleranceSeconds = 1000;
            fixture.economy = economy; fixture.living = living; fixture.services = services;
            facts.Add("OPERATIONS UI ONLY: cloned production continuous configuration with automatic sales ENABLED; diagnostic cash=4000, first activity delay=1000, optional service eligibility=0 and self-response observation/tolerance=1000. Initial normal room-policy commands offer two rooms at the allowed minimum rate; labelled clock advances execute scheduled demand decisions and obtain actual reservation IDs, never AcceptBooking or injected contracts. Explicit model reception/key/room callbacks prepare labels; these are not physical guest-route or check-in evidence. Forced boiler condition45, branch B trip, dirty room105 and debug departure demonstrate factual status, not the severe early-departure policy. Staff viewpoints are empty-handed diagnostic placements. Every UI page/action uses the owned synthetic controller. Clock1x plus labelled schedule/report advances; no three-day lifecycle, natural balance, human playtest or natural failure claim.");
            File.WriteAllText(Path.Combine(output, "operations-ui-hashes.txt"),
                "SHA256 capture filename. Distinct hashes reject identical captures only; each real player image still requires manual UI/legibility review.\n");
            try
            {
                if (observedSimulation != null) observedSimulation.Housekeeping.Changed -= ObserveCleaning;
                ManagementUI.Instance.Close(); session.config = fixture; session.NewGame(); ManagementUI.Instance.Close();
                observedSimulation = session.Simulation;
                observedSimulation.Housekeeping.Changed += ObserveCleaning;
                session.Wait.Stop("Explicit UI-only diagnostic"); session.Wait.enabled = false;
                var model = session.Simulation;
                Require(model.AutomaticBookingsEnabled, "presentation clone preserves production automatic sales");
                foreach (var policy in model.RoomSalesPolicies)
                    Require(model.SetRoomSalesPolicy(0, policy.RoomId, policy.RoomId == 101 || policy.RoomId == 102,
                        session.Economy.MinPrice, policy.Revision).Success, "labelled initial two-room sales policy adapter");
                AdvanceOperationsUICalendarTo(model.SalesDecisionAt(1, SalesSettings.DecisionsPerDay - 1) + .25f);
                var today = model.Reservations.Where(item => item.Offer.ArrivalDay == 1 && item.Status == ReservationStatus.Reserved)
                    .OrderBy(item => item.Offer.ArrivalAt).ToArray();
                Require(today.Length == 2 && today.All(item => item.IsAutomatic) && today.Select(item => item.RoomId).OrderBy(id => id).SequenceEqual(new[] { 101, 102 }),
                    "scheduled ordinary demand created exactly two actual day-one contracts under two-room policy");
                for (int index = 0; index < today.Length; index++)
                {
                    AdvanceOperationsUICalendarTo(today[index].Offer.ArrivalAt + .25f);
                    Require(session.ReportGuestReachedReception(today[index].Id).Success, "labelled reception callback adapter");
                    Require(model.Keys.PickUp(0, today[index].RoomId).Success && model.CheckIn(0, today[index].Id).Success,
                        "labelled model rack pickup and room-key handoff adapter");
                    Require(session.ReportGuestReachedRoom(today[index].Id).Success, "labelled room callback adapter");
                }
                foreach (var policy in model.RoomSalesPolicies)
                    Require(model.SetRoomSalesPolicy(0, policy.RoomId, policy.RoomId == 104 || policy.RoomId == 105,
                        session.Economy.MinPrice, policy.Revision).Success, "labelled next-date two-room sales policy adapter");
                Require(today.All(item => item.Price == session.Economy.MinPrice && item.Status == ReservationStatus.Arrived),
                    "closing day-one sale rooms preserves their existing contracts and guests");
                model.Boiler.SetCondition(45);
                Require(model.Electrical.ForceTrip("B").Success && model.DebugMarkRoomDirty(105).Success, "explicit status setup");
                session.RaiseChanged(); yield return null; yield return null;
                var terminal = FindAnyObjectByType<ReceptionTerminal>();
                Require(terminal, "authored reception terminal exists");
                var aim = terminal.transform.position + Vector3.up * .35f;
                Position(coop.Players[0], new Vector3(aim.x, .08f, aim.z - 1.45f), aim);
                // Direct opening is an explicit fixture adapter; all subsequent page/action
                // selection uses the production controller reader, never reflected delegates.
                var ui = ManagementUI.Instance; ui.Open(0);
                yield return Until(() => ui.IsOperationsOpen, 2, "continuous operations menu opens");
                Require(ui.DisplayedOperationsOverview.Contains("2/6 occupied") && ui.DisplayedOperationsOverview.Contains("B: TRIPPED"),
                    "overview renders current occupancy and real branch state");
                yield return CaptureOperationsPage("01-operations", "Operations overview / prepared factual hotel conditions");

                var departed = model.Guests.Single(guest => guest.GuestId == today[0].Id);
                Require(model.DebugCheckoutGuest(departed.GuestId).Success, "explicit debug departure setup, not severe-policy evidence");
                session.AdvanceTime(.1f);
                Require(departed.ReceiptPosted && !ui.DisplayedOperationsOverview.Contains("OUT · 101"), "paid departure is omitted from upcoming movements");
                yield return CaptureOperationsPage("02-paid-departure", "Paid departure is retained in history but omitted from upcoming checkouts");
                yield return ChooseOperationsUI("Room sales / rates");
                yield return CaptureOperationsPage("02a-room-sales", "Actual room sales page / two future sale rooms distinct from existing occupied contracts");
                yield return ChooseOperationsUI("Room 105 · ");
                var policy105 = model.RoomSalesPolicies.Single(item => item.RoomId == 105);
                int policyRevision = policy105.Revision, originalRate = policy105.Price;
                yield return ChooseOperationsUI("+ $");
                Require(ui.DisplayedSalesPrice == originalRate + session.Economy.PriceStep && policy105.Price == originalRate &&
                    policy105.Revision == policyRevision, "controller sale-rate draft does not change policy before Apply");
                yield return CaptureOperationsPage("02b-rate-draft", "Owned controller future-rate draft / stored policy and existing guest prices remain unchanged");
                yield return ChooseOperationsUI("Apply sales policy");
                Require(policy105.Price == originalRate + session.Economy.PriceStep && policy105.Revision == policyRevision + 1 &&
                    today.All(item => item.Price == session.Economy.MinPrice), "controller applies future rate once without repricing existing contracts");
                yield return CaptureOperationsPage("02c-rate-applied", "Applied future room rate / policy revision updated / no contract repricing");
                yield return ChooseOperationsUI("Back to room sales");
                yield return ChooseOperationsUI("Back to operations");
                AdvanceOperationsUICalendarTo(model.SalesDecisionAt(2, SalesSettings.DecisionsPerDay - 1) + .25f);
                var tomorrow = model.Reservations.Where(item => item.Offer.ArrivalDay == 2 && item.Status == ReservationStatus.Reserved)
                    .OrderBy(item => item.Offer.ArrivalAt).ToArray();
                Require(tomorrow.Length == 2 && tomorrow.All(item => item.IsAutomatic) && tomorrow.Select(item => item.RoomId).OrderBy(id => id).SequenceEqual(new[] { 104, 105 }),
                    "scheduled future demand created two real confirmed reservations under the current sales policy");
                yield return ChooseOperationsUI("Tomorrow's bookings");
                Require(!ui.OperationsOptionTitles.Any(title => title.StartsWith("Accept booking", StringComparison.Ordinal)),
                    "automatic schedule exposes confirmed contracts, not ordinary approval controls");
                yield return CaptureOperationsPage("03-bookings", "Tomorrow's automatically confirmed dated bookings / agreed charges before refunds");
                var selected = tomorrow[0];
                int originalRoom = selected.RoomId, originalPrice = selected.Price, originalRevision = selected.Revision;
                var originalOffer = selected.Offer;
                yield return ChooseOperationsUI(selected.Offer.Application.GuestName + " · ");
                yield return ChooseOperationsUI("Room 106 · ");
                Require(selected.RoomId == originalRoom && selected.Price == originalPrice && selected.Revision == originalRevision &&
                    ui.DisplayedBookingRoom == 106 && ui.DisplayedBookingPrice == originalPrice &&
                    !ui.OperationsOptionTitles.Any(title => title.StartsWith("Accept booking", StringComparison.Ordinal) || title == "Update agreed price"),
                    "real room-selection draft preserves the existing contract and its fixed price");
                yield return CaptureOperationsPage("04-booking-detail", "Confirmed booking reassignment preview in room106 / existing agreed price remains fixed");
                yield return ChooseOperationsUI("Forecast · ");
                Require(ui.DisplayedForecastCircuitState.Contains("TRIPPED · reset required") && ui.DisplayedBookingForecast.CircuitReserve > 0,
                    "positive estimated reserve does not conceal actual tripped branch");
                yield return CaptureOperationsPage("05-forecast", "Approximate booking demand / actual tripped circuit remains explicit");
                yield return ChooseOperationsUI("Back to booking");
                yield return ChooseOperationsUI("Reassign booking · room 106");
                Require(selected.RoomId == 106 && selected.Revision == originalRevision + 1 && selected.Price == originalPrice &&
                    ReferenceEquals(selected.Offer, originalOffer), "actual controller reassigns exactly once while preserving contract identity, dates and agreed price");
                yield return CaptureOperationsPage("05a-reassigned", "Applied future-room reassignment / same automatic contract, guest, dates and agreed rate");
                yield return ChooseOperationsUI("Back to bookings");
                yield return ChooseOperationsUI("Back to operations");
                yield return ChooseOperationsUI("Boiler maintenance");
                yield return CaptureOperationsPage("06-maintenance", "Basic / Full Service inspection, cost and actual boiler condition");
                int maintenanceCash = model.Economy.Cash, maintenanceRevision = model.Boiler.MaintenanceRevision;
                yield return ChooseOperationsUI("Select Full Service");
                var boilerStation = BoilerServiceInteraction.Instance;
                Require(boilerStation && boilerStation.TryGetSelection(0, out var selectedKind, out var selectedRevision) &&
                    selectedKind == BoilerServiceKind.Full && selectedRevision == maintenanceRevision &&
                    model.Economy.Cash == maintenanceCash && !model.Boiler.MaintenanceInProgress,
                    "controller selects Full Service without payment or boiler shutdown");
                yield return ChooseOperationsUI("Close / keep working");
                facts.Add("Maintenance UI capture: explicitly repositioned empty-handed staff to the authored boiler plate; actual owned controller look, first-surface focus and held setup start Full Service. This verifies physical setup, not walking a reception-to-boiler route.");
                yield return PlaceServiceStaff(boilerStation.InteractionPoint + Vector3.back * 2.1f, boilerStation.InteractionPoint);
                yield return Until(() => coop.Players[0].Interactor.Focused == boilerStation, 3, "real ray reaches the boiler inspection plate");
                BindSyntheticStaff();
                Require(ReferenceEquals(coop.Players[0].Input.Gamepad, verificationPads[0]), "physical service uses only the owned controller");
                InputSystem.QueueStateEvent(verificationPads[0], new GamepadState().WithButton(GamepadButton.South));
                try { yield return Until(() => model.Boiler.MaintenanceInProgress, 6, "actual held physical setup starts Full Service"); }
                finally { InputSystem.QueueStateEvent(verificationPads[0], new GamepadState()); }
                yield return null; yield return null;
                Require(model.Boiler.MaintenanceInProgress && model.PeriodMaintenanceSpend == session.Economy.ProperRepairCost,
                    "actual physical setup starts one paid maintenance job");
                yield return PressMenu(GamepadButton.South);
                yield return Until(() => ui.IsOperationsOpen && ui.OperationsOptionTitles.Any(title => title.StartsWith("Maintenance in progress")),
                    2, "real boiler interaction reopens active service inspection");
                yield return CaptureOperationsPage("07-maintenance-active", "Maintenance downtime and remaining hotel time");
                yield return ChooseOperationsUI("Back to operations");
                Require(ui.DisplayedOperationsOverview.Contains("OFF · maintenance until"), "overview shows actual maintenance downtime");
                yield return CaptureOperationsPage("08-operations-maintenance", "Operations overview during maintenance");
                yield return ChooseOperationsUI("Capacity upgrades");
                yield return CaptureOperationsPage("09-upgrades", "Boiler and circuit upgrade choices / price and capacity");
                yield return ChooseOperationsUI("Upgrade circuit B");
                Require(model.Electrical.UpgradedCircuitId == "B" && model.Electrical.Find("B").Tripped,
                    "actual controller purchases capacity without resetting the circuit");
                yield return CaptureOperationsPage("10-upgrade-installed", "Purchased circuit capacity / trip remains unresolved");
                yield return ChooseOperationsUI("Back to operations");
                yield return ChooseOperationsUI("Daily reports");
                yield return CaptureOperationsPage("11-current-finances", "Current period cash, collected receipts, unpaid bookings and actual costs");
                facts.Add("UI diagnostic advances hotel clock directly to the next report boundary; no natural three-day claim.");
                session.AdvanceTime(model.NextReportAt - model.Elapsed + .1f);
                Require(session.Reports.Count == 1 && session.Report.MaintenanceSpend == session.Economy.ProperRepairCost &&
                    session.Report.CapitalSpend == session.Economy.ElectricalUpgradeCost, "one nonmodal report includes both actual expenditures");
                yield return CaptureOperationsPage("12-daily-report-list", "Current period after automatic report / historical report remains available");
                int cash = model.Economy.Cash;
                yield return ChooseOperationsUI("Operating report 1");
                Require(model.Economy.Cash == cash && session.Phase == DayPhase.Service, "opening a report neither charges again nor stops operations");
                yield return CaptureOperationsPage("13-report-detail", "Historical daily report with maintenance and capital arithmetic");

                ui.Close();
                var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
                Require(phone, "authored reception phone exists");
                yield return PlaceServiceStaff(phone.transform.position + Vector3.back * 1.45f, phone.GetComponent<Collider>().bounds.center);
                yield return Until(() => coop.Players[0].Interactor.Focused == phone, 3, "staff ray reaches actual telephone");
                yield return PressMenu(GamepadButton.South);
                yield return Until(() => ui.IsWakePhoneOpen, 2, "actual phone interaction opens incoming/outgoing call interface");
                yield return CaptureOperationsPage("14-phone-calendar", "Actual telephone UI / hotel date and time wording");
                ui.Close();

                Position(coop.Players[0], new Vector3(-.52f, .1f, 33.9f), new Vector3(-.52f, 2.4f, 36.12f));
                yield return CaptureOperationsPage("15-boiler-readout", "Physical boiler readout / pressure and capacity are distinct gauges");
                electricalPanel.cover.RequestOpen();
                yield return Until(() => electricalPanel.cover.IsPassageOpen, 3, "diagnostic cabinet opening reveals physical readouts");
                Position(coop.Players[0], new Vector3(4.9f, .1f, 32.7f), new Vector3(4.9f, 1.9f, 35.25f));
                yield return CaptureOperationsPage("16-electrical-readout", "Physical panel / installed branch capacity and actual power state");
                Require(captures.Count == 20 && operationsImageHashes.Count == 20, "twenty distinct fresh player capture candidates");
                completedReports = session.Reports.ToArray(); finalCash = model.Economy.Cash; finalReputation = model.Economy.Reputation;
                operationsUIVerified = true;
                facts.Add("OperationsUIVerified=True AutomaticSales=True ControllerPolicyRate=True ConfirmedContracts=True ImmutableAgreedPrice=True ReassignmentPreviewAndApply=True ControllerPages=True PaidActions=True ReportsOnce=True DistinctCaptureCandidates=20 ManualVisualReview=REQUIRED ThreeDayLifecycle=False NaturalBalance=False HumanPlaytest=False");
            }
            finally
            {
                session.config = production;
                Destroy(fixture); Destroy(economy); Destroy(living); Destroy(services);
            }
        }

        void AdvanceOperationsUICalendarTo(float target)
        {
            var model = session.Simulation;
            Require(model.AutomaticBookingsEnabled && Number.IsFinite(target), "UI calendar adapter requires an automatic production hotel");
            if (model.Elapsed >= target) return;
            facts.Add("UI-only scheduled-clock adapter: " + model.Elapsed.ToString("F2") + " -> " + target.ToString("F2") +
                ". Regular model steps process due sales; no reservation injection or synthetic demand decision.");
            // AdvanceTime uses the ordinary fixed model steps. A quarter-second margin avoids
            // treating a rounded final remainder as a missing decision exactly at the boundary.
            session.AdvanceTime(target - model.Elapsed + .25f);
            Require(ReferenceEquals(session.Simulation, model) && model.Elapsed >= target && session.Phase == DayPhase.Service,
                "scheduled-clock adapter preserves the active model and reaches the intended presentation date");
        }

        IEnumerator ChooseOperationsUI(string prefix)
        {
            var ui = ManagementUI.Instance;
            yield return null; yield return null;
            Require(ui.IsOperationsOpen && ui.OperationsOptionTitles.Any(title => title.StartsWith(prefix, StringComparison.Ordinal)),
                "operations control exists: " + prefix);
            for (int step = 0; step < 32 && !(ui.FocusedOperationsOption?.StartsWith(prefix, StringComparison.Ordinal) ?? false); step++)
                yield return PressMenu(GamepadButton.DpadDown);
            Require(ui.FocusedOperationsOption?.StartsWith(prefix, StringComparison.Ordinal) ?? false, "actual controller focuses " + prefix);
            yield return PressMenu(GamepadButton.South);
        }

        IEnumerator CaptureOperationsPage(string name, string note)
        {
            yield return Capture(name, note);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(Path.Combine(output, name + ".png")))).Replace("-", string.Empty).ToLowerInvariant();
            File.AppendAllText(Path.Combine(output, "operations-ui-hashes.txt"), hash + " " + name + ".png\n");
            Require(operationsImageHashes.Add(hash), "real player capture is not byte-identical to a previous page: " + name);
        }
    }
}
#endif
