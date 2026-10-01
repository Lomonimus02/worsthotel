using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        void StartContinuousCalendarFixture(float openingHour = 23, float reportHour = 1)
        {
            var session = GameSession.Instance;
            // LABELLED CALENDAR FIXTURE: a short calendar only. No guests, capacity overrides,
            // synthetic arrival callbacks or altered infrastructure assets are needed here.
            // The reusable fixture's teardown destroys this clone; the shared production configuration remains unchanged.
            waitScenarioSessionConfig = Object.Instantiate(session.config);
            waitScenarioSessionConfig.continuousOperations = true;
            waitScenarioSessionConfig.hotelDaySeconds = 24;
            waitScenarioSessionConfig.openingHour = openingHour;
            waitScenarioSessionConfig.reportHour = reportHour;
            session.config = waitScenarioSessionConfig;
            session.NewGame();
            ManagementUI.Instance.Close();
            Assert.That(session.Simulation.ContinuousOperations && session.Simulation.Running, Is.True);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(session.Simulation.Guests, Is.Empty);
            Assert.That(session.Plan, Is.Not.Null, "The phase-1 opt-in keeps the existing ledger binding valid.");
            Assert.That(session.PlanCommitted, Is.False);
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSessionUpdatesThroughReportWithoutClosingHotelOrOpeningLedger()
        {
            var session = GameSession.Instance;
            var productionConfig = session.config;
            Assert.That(productionConfig.continuousOperations, Is.True,
                "Production uses continuous operations once dated bookings are available.");
            Assert.That(productionConfig.OperationsData(), Is.Not.Null);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            StartContinuousCalendarFixture();
            var simulation = session.Simulation;
            var rooms = session.Rooms;
            var boiler = simulation.Boiler;
            int openingCash = simulation.Economy.Cash;
            float reputation = simulation.Economy.Reputation;
            float boundary = simulation.NextReportAt;
            boiler.ForceFailure(); // Explicit failure setup tests persistence, not overload tuning.

            // Only normal rendered Update frames advance the hotel in this scenario.
            yield return WaitForCondition(() => session.Report != null, 5,
                "The real GameSession Update pipeline must publish its first continuous report.");
            Assert.That(session.Simulation, Is.SameAs(simulation));
            Assert.That(session.Rooms, Is.SameAs(rooms));
            Assert.That(simulation.Boiler, Is.SameAs(boiler));
            Assert.That(boiler.Failed, Is.True, "An accounting boundary cannot restart a failed boiler.");
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(simulation.Running, Is.True);
            Assert.That(simulation.IsServiceComplete, Is.False);
            Assert.That(session.Day, Is.EqualTo(2), "The calendar date and first report number are distinct.");
            Assert.That(session.Report.DayNumber, Is.EqualTo(1));
            Assert.That(session.Report, Is.SameAs(simulation.LastReport));
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(session.Report.Receipts, Is.Empty);
            Assert.That(session.Cash, Is.EqualTo(openingCash - session.Economy.DailyOperatingCost));
            Assert.That(simulation.Economy.Reputation, Is.EqualTo(reputation));
            Assert.That(simulation.Elapsed, Is.GreaterThanOrEqualTo(boundary - .001f));
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            Assert.That(bootstrap.Players[0].IsUIBlocked || bootstrap.Players[1].IsUIBlocked, Is.False);

            var report = session.Report;
            float reportedAt = simulation.Elapsed;
            yield return WaitForCondition(() => simulation.Elapsed > reportedAt + .19f, 2,
                "Hotel time must continue after publishing the report without a Continue button.");
            session.EndShift();
            session.CommitPlan(0);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(simulation.Running && boiler.Failed, Is.True);
            Assert.That(session.Report, Is.SameAs(report));
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(session.Plan.IsCommitted || session.PlanCommitted, Is.False,
                "Reject a legacy commit before it mutates the compatibility plan.");
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            Assert.That(productionConfig.continuousOperations, Is.True, "The fixture must not change the shared production asset.");

            waitScenarioSessionConfig.continuousOperations = false;
            session.NewGame();
            Assert.That(session.Simulation.ContinuousOperations, Is.False);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Planning));
            Assert.That(session.Reports, Is.Empty);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousReportLeavesExistingLedgerOwnerAndPartnerAccessUnchanged()
        {
            StartContinuousCalendarFixture();
            var session = GameSession.Instance;
            var ledger = ManagementUI.Instance;
            ledger.Open(1);
            Assert.That(ledger.IsOpen && ledger.Owner == 1, Is.True);
            Assert.That(bootstrap.Players[0].IsUIBlocked, Is.False);
            Assert.That(bootstrap.Players[1].IsUIBlocked, Is.True);

            yield return WaitForCondition(() => session.Report != null, 5,
                "A local ledger does not pause the hotel's operating calendar.");
            Assert.That(ledger.IsOpen, Is.True);
            Assert.That(ledger.Owner, Is.EqualTo(1), "Reporting must not close and reopen the ledger for staff 1.");
            Assert.That(bootstrap.Players[0].IsUIBlocked, Is.False);
            Assert.That(bootstrap.Players[0].Interactor.CanAct, Is.True);
            Assert.That(bootstrap.Players[1].IsUIBlocked, Is.True);
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(session.Simulation.Running && !bootstrap.IsPaused, Is.True);
            ledger.Close();
            Assert.That(bootstrap.Players[1].IsUIBlocked, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSessionPauseFreezesCalendarAndResumesWithoutWallClockCatchUp()
        {
            StartContinuousCalendarFixture();
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            yield return WaitForCondition(() => simulation.Elapsed >= .6f, 3,
                "The continuous fixture must first advance through its real Update loop.");
            bootstrap.SetPaused(true);
            float pausedAt = simulation.Elapsed;
            float cash = session.Cash;
            Assert.That(pausedAt, Is.LessThan(simulation.NextReportAt));
            Assert.That(Time.timeScale, Is.Zero);
            // Longer than this fixture's time to reporting: a wall-clock catch-up would cross it.
            yield return new WaitForSecondsRealtime(2.2f);
            Assert.That(simulation.Elapsed, Is.EqualTo(pausedAt));
            Assert.That(session.Cash, Is.EqualTo(cash));
            Assert.That(session.Reports, Is.Empty);

            float resumedAt = Time.realtimeSinceStartup;
            bootstrap.SetPaused(false);
            yield return new WaitForSecondsRealtime(.35f);
            float realResumedTime = Time.realtimeSinceStartup - resumedAt;
            Assert.That(simulation.Elapsed - pausedAt, Is.GreaterThan(0));
            Assert.That(simulation.Elapsed - pausedAt, Is.LessThanOrEqualTo(realResumedTime + 1f / session.Settings.TickRate + .05f),
                "Resume may consume a partial normal tick, but cannot replay the paused wall-clock interval.");
            Assert.That(session.Reports, Is.Empty);
            Assert.That(Time.timeScale, Is.EqualTo(1));
            yield return WaitForCondition(() => session.Report != null, 4,
                "The same calendar should later reach its report using resumed simulation time.");
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            Assert.That(session.Cash, Is.EqualTo(cash), "Reporting at 01:00 does not charge the independent 06:00 operating bill.");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSessionRealWaitStopsAtReportWithoutAcceleratingPhysicsOrReusingHeldVotes()
        {
            StartContinuousCalendarFixture(8, 20); // Twelve quiet hotel seconds before the first report.
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            float boundary = simulation.NextReportAt;
            float physicsStep = Time.fixedDeltaTime;
            float reportObservedAt = -1;
            System.Action observeReport = () =>
            {
                if (session.Report != null && reportObservedAt < 0) reportObservedAt = simulation.Elapsed;
            };
            session.Changed += observeReport;
            try
            {
                yield return ConsentToWait();
                Assert.That(Waiter.HasVoted(0) && Waiter.HasVoted(1), Is.True);
                Assert.That(simulation.Clock.Speed, Is.GreaterThan(1));
                Assert.That(Time.timeScale, Is.EqualTo(1));
                yield return WaitForCondition(() => session.Report != null, 5,
                    "Both real pad votes should advance a quiet, empty hotel to its report.");
                Assert.That(reportObservedAt, Is.GreaterThanOrEqualTo(boundary - .001f));
                Assert.That(reportObservedAt, Is.LessThanOrEqualTo(boundary + 1f / session.Settings.TickRate + .001f),
                    "The report event must discard the rest of this frame's accelerated tick budget.");
                Assert.That(Waiter.IsWaiting, Is.False);
                Assert.That(simulation.Clock.Speed, Is.EqualTo(1));
                Assert.That(Waiter.Reason, Does.Contain("Operating report"));
                Assert.That(Waiter.HasVoted(0) || Waiter.HasVoted(1), Is.False);
                Assert.That(Waiter.IsAwaitingRelease(0) && Waiter.IsAwaitingRelease(1), Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(1));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(physicsStep));
                Assert.That(ManagementUI.Instance.IsOpen, Is.False);
                yield return new WaitForSecondsRealtime(WaitHoldSeconds + .15f);
                Assert.That(Waiter.IsWaiting, Is.False, "A report must not rearm continuously held WAIT inputs.");
                Assert.That(session.Reports.Count, Is.EqualTo(1));
                Assert.That(session.Simulation.Running, Is.True);
                yield return ReleaseWaitButtons();
            }
            finally { session.Changed -= observeReport; }
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest, Category("ContinuousOperations")]
        public IEnumerator ContinuousSessionDeveloperAdvancesRemainBoundedAndSynchronizeReports()
        {
            StartContinuousCalendarFixture(8, 6);
            var session = GameSession.Instance;
            var simulation = session.Simulation;
            // Explicit diagnostic commands, separate from tests claiming normal Update/WAIT input.
            session.AdvanceTime(float.MaxValue);
            Assert.That(simulation.Elapsed, Is.GreaterThan(23.9f).And.LessThanOrEqualTo(24.01f),
                "An extreme developer request is capped to one calendar-day horizon.");
            Assert.That(session.Reports.Count, Is.EqualTo(1));
            float before = simulation.Elapsed;
            session.AdvanceToNextEvent();
            Assert.That(simulation.Elapsed - before, Is.InRange(0, simulation.Operations.SecondsPerDay + .01f));
            Assert.That(session.Reports.Count, Is.EqualTo(2));
            Assert.That(session.Report, Is.SameAs(simulation.LastReport));
            Assert.That(session.Phase, Is.EqualTo(DayPhase.Service));
            Assert.That(simulation.Running, Is.True);
            Assert.That(ManagementUI.Instance.IsOpen, Is.False);
            yield return null;
            LogAssert.NoUnexpectedReceived();
        }
    }
}
