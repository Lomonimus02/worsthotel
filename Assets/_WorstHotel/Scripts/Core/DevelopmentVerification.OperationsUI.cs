#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEngine;
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
            Require(soloTour && session.Simulation.ContinuousOperations, "UI verification uses production continuous SOLO mode");
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
            facts.Add("OPERATIONS UI ONLY: cloned production continuous configuration; diagnostic cash=4000, first activity delay=1000, optional service eligibility=0 and self-response observation/tolerance=1000. Explicit initial model reception/key/room callbacks prepare labels; these are not evidence of physical guest routes or check-in. Forced boiler condition45, branch B trip, dirty room105 and early checkout demonstrate factual status. Staff viewpoints are empty-handed diagnostic placements. Page choices use only the owned synthetic controller. Clock1x plus a labelled report-boundary advance; no three-day lifecycle, human playtest or natural failure claim.");
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
                var today = model.BookingOffers.Where(offer => offer.ArrivalDay == 1).OrderBy(offer => offer.ArrivalAt).Take(2).ToArray();
                Require(today.Length == 2, "two current dated offers exist");
                for (int index = 0; index < today.Length; index++)
                {
                    Require(session.AcceptBooking(0, today[index].Id, 101 + index, session.Economy.MinPrice).Success, "diagnostic current booking");
                    session.AdvanceTime(today[index].ArrivalAt - model.Elapsed + .1f);
                    Require(session.ReportGuestReachedReception(today[index].Id).Success, "labelled reception callback adapter");
                    Require(model.Keys.PickUp(0, 101 + index).Success && model.CheckIn(0, today[index].Id).Success,
                        "labelled model rack pickup and room-key handoff adapter");
                    Require(session.ReportGuestReachedRoom(today[index].Id).Success, "labelled room callback adapter");
                }
                var tomorrow = model.BookingOffers.Where(offer => offer.ArrivalDay == 2).OrderBy(offer => offer.ArrivalAt).Take(2).ToArray();
                Require(tomorrow.Length == 2, "two future dated offers exist");
                Require(session.AcceptBooking(0, tomorrow[0].Id, 104, session.Economy.MinPrice).Success, "diagnostic future booking");
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
                Require(model.DebugCheckoutGuest(departed.GuestId).Success, "explicit early checkout setup");
                session.AdvanceTime(.1f);
                Require(departed.ReceiptPosted && !ui.DisplayedOperationsOverview.Contains("OUT · 101"), "paid departure is omitted from upcoming movements");
                yield return CaptureOperationsPage("02-paid-departure", "Paid departure is retained in history but omitted from upcoming checkouts");
                yield return ChooseOperationsUI("Tomorrow's bookings");
                yield return CaptureOperationsPage("03-bookings", "Tomorrow's actual dated booking list");
                yield return ChooseOperationsUI(tomorrow[1].Application.GuestName + " · ");
                yield return ChooseOperationsUI("Room 106 · ");
                yield return CaptureOperationsPage("04-booking-detail", "Selected room106 / unchanged draft booking price");
                yield return ChooseOperationsUI("Forecast · ");
                Require(ui.DisplayedForecastCircuitState.Contains("TRIPPED · reset required") && ui.DisplayedBookingForecast.CircuitReserve > 0,
                    "positive estimated reserve does not conceal actual tripped branch");
                yield return CaptureOperationsPage("05-forecast", "Approximate booking demand / actual tripped circuit remains explicit");
                yield return ChooseOperationsUI("Back to booking");
                yield return ChooseOperationsUI("Back to bookings");
                yield return ChooseOperationsUI("Back to operations");
                yield return ChooseOperationsUI("Boiler maintenance");
                yield return CaptureOperationsPage("06-maintenance", "Proper maintenance cost and actual boiler condition");
                yield return ChooseOperationsUI("Start proper maintenance");
                Require(model.Boiler.MaintenanceInProgress && model.PeriodMaintenanceSpend == session.Economy.ProperRepairCost,
                    "actual controller starts one paid maintenance job");
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
                Require(captures.Count == 16 && operationsImageHashes.Count == 16, "sixteen distinct fresh player capture candidates");
                completedReports = session.Reports.ToArray(); finalCash = model.Economy.Cash; finalReputation = model.Economy.Reputation;
                operationsUIVerified = true;
                facts.Add("OperationsUIVerified=True ControllerPages=True PaidActions=True ReportsOnce=True DistinctCaptureCandidates=16 ManualVisualReview=REQUIRED ThreeDayLifecycle=False HumanPlaytest=False");
            }
            finally
            {
                session.config = production;
                Destroy(fixture); Destroy(economy); Destroy(living); Destroy(services);
            }
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
