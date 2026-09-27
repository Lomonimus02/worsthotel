using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;

namespace WorstHotel.Tests
{
    /// <summary>Historical shift-mode integration with the original 4.2-rated boiler and explicit timed route callbacks.
    /// ContinuousEconomyComparisonTests owns current production continuous-calendar balance.</summary>
    public sealed class LivingEmergenceTests
    {
        const string AssetPath = "Assets/_WorstHotel/ScriptableObjects/PrototypeSession.asset";
        const string HeaterId = "portable-heater-1";

        sealed class Run
        {
            public SessionSettings Settings;
            public HotelSimulation Simulation;
            public RoomState[] Rooms;
            public DayReport Report;
            public GuestStay Target, Secondary;
            public float ColdObserved = -1, Complaint = -1, HeaterOn = -1, Warning = -1, Trip = -1, SecondaryObserved = -1, SecondaryComplaint = -1;
            public float SecondaryCaseExposure;
            public float PlacementTemperature, PretripWarmest, PlacementDissatisfaction, BestPoweredDissatisfaction;
            public float MaximumBoilerLoad, MinimumHeatingOutput = 1, EndTargetTemperature;
            public int NaturalShowers, Seed;
            public readonly StringBuilder Events = new StringBuilder();
            public RoomState Room(int id) => Rooms.Single(room => room.Profile.Id == id);
        }

        // Authored prototype routes: spawn z=-3.8, reception x=-2.8-index*1.05/z=1.30,
        // corridor x=+/-0.32, doors z=10/17/24 and room target x=+/-4.3/z=door+0.7.
        // Travel is distance / the production guest speed 1.35, plus an explicit door allowance.
        // This adapter does not claim to simulate collider motion; PlayMode owns that evidence.
        sealed class Boundaries
        {
            sealed class Travel
            {
                public GuestAgentState State = GuestAgentState.Scheduled;
                public float Due, VacateAt = -1;
                public int Actor = -1;
                public bool Vacated;
            }
            readonly Run run;
            readonly Dictionary<string, Travel> travels = new Dictionary<string, Travel>();
            readonly string[] staff = new string[2];
            readonly TimedManualTurnoverAdapter manualTurnover;
            public Boundaries(Run run) { this.run = run; manualTurnover = new TimedManualTurnoverAdapter(run.Simulation, run.Rooms, staff); }

            public void Tick()
            {
                var simulation = run.Simulation;
                float now = simulation.Elapsed;
                for (int index = 0; index < simulation.Guests.Count; index++)
                {
                    var guest = simulation.Guests[index];
                    if (!travels.TryGetValue(guest.GuestId, out var travel))
                        travels.Add(guest.GuestId, travel = new Travel());
                    var state = guest.Agent.State;
                    if (state != travel.State)
                    {
                        if (travel.Actor >= 0) { staff[travel.Actor] = null; travel.Actor = -1; }
                        travel.State = state;
                        float receptionX = 2.8f + index * 1.05f;
                        if (state == GuestAgentState.Arriving)
                            travel.Due = now + (4.15f + receptionX + .95f) / 1.35f;
                        if (state == GuestAgentState.GoingToRoom)
                        {
                            float side = guest.RoomId % 2 == 1 ? -1 : 1;
                            float doorZ = DoorZ(guest.RoomId);
                            float distance = .95f + Math.Abs(-receptionX - side * .32f) + doorZ - .35f + 3.98f + .7f;
                            travel.Due = now + distance / 1.35f + .8f;
                        }
                        if (state == GuestAgentState.Leaving)
                        {
                            // Sleeping guests leave from the authored rest position; include margin/door travel.
                            travel.VacateAt = now + 7;
                            travel.Due = travel.VacateAt + (DoorZ(guest.RoomId) + 5.5f) / 1.35f;
                        }
                        if (state == GuestAgentState.LeavingRoom || state == GuestAgentState.ReturningToRoom)
                            travel.Due = now + (DoorZ(guest.RoomId) + 12.5f) / 1.35f + .8f;
                    }
                    if (state == GuestAgentState.Arriving && now >= travel.Due)
                        Assert.That(simulation.SignalGuestReachedReception(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.WaitingForCheckIn)
                    {
                        if (travel.Actor < 0)
                        {
                            int actor = Array.FindIndex(staff, value => value == null);
                            if (actor >= 0) { staff[actor] = guest.GuestId; travel.Actor = actor; travel.Due = now + simulation.LivingSettings.KeyRetrievalEstimateSeconds; }
                        }
                        if (travel.Actor >= 0 && now >= travel.Due)
                            Assert.That(ModelKeyHandoff.CheckIn(simulation, travel.Actor, guest.GuestId).Success, Is.True);
                    }
                    if (state == GuestAgentState.GoingToRoom && now >= travel.Due)
                        Assert.That(simulation.SignalGuestReachedRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.LeavingRoom && now >= travel.Due)
                        Assert.That(simulation.SignalGuestLeftRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.ReturningToRoom && now >= travel.Due)
                        Assert.That(simulation.SignalGuestReturnedRoom(guest.GuestId).Success, Is.True);
                    if (state == GuestAgentState.Leaving)
                    {
                        if (!travel.Vacated && now >= travel.VacateAt)
                        {
                            foreach (var room in run.Rooms.Where(room => room.DepartingGuestId == guest.GuestId))
                                Assert.That(simulation.SignalGuestVacatedRoom(guest.GuestId, room.Profile.Id).Success, Is.True);
                            travel.Vacated = true;
                        }
                        if (now >= travel.Due) Assert.That(simulation.SignalGuestLeft(guest.GuestId).Success, Is.True);
                    }
                }
                manualTurnover.Tick();
            }
            static float DoorZ(int room) => 10 + (room - 101) / 2 * 7;
        }

        static Run Create()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SessionConfig>(AssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.living && asset.needs && asset.noise && asset.heater && asset.electricity && asset.housekeeping, Is.True);
            // This scenario explicitly exercises StartShift/EndShift and Defer, including the
            // historical small overload that motivated its heater workaround. Copy the assets
            // so new continuous-calendar capacity tuning cannot rewrite that legacy fixture.
            var legacy = UnityEngine.Object.Instantiate(asset);
            var legacyBoiler = UnityEngine.Object.Instantiate(asset.boiler);
            SessionSettings settings;
            try
            {
                legacy.continuousOperations = false;
                legacyBoiler.safeLoad = 4.2f;
                legacy.boiler = legacyBoiler;
                settings = legacy.ToData();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(legacy);
                UnityEngine.Object.DestroyImmediate(legacyBoiler);
            }
            var rooms = settings.Rooms.Select(profile => new RoomState(profile)).ToArray();
            return new Run { Settings = settings, Rooms = rooms, Seed = asset.living.seed,
                Simulation = new HotelSimulation(settings, rooms, asset.living.ToData(), asset.needs.ToData(),
                    asset.noise.ToData(), asset.heater.ToData(), asset.electricity.ToData(), asset.housekeeping.ToData()) };
        }

        static void Start(Run run, int day, int[] assignmentRooms)
        {
            var offers = GuestSystem.GenerateApplications(day, run.Settings.GuestArchetypes, run.Settings.Day3BusinessReferencePrice);
            var selected = offers.Take(assignmentRooms.Length).ToArray();
            Assert.That(run.Simulation.StartShift(selected.Select((offer, index) =>
                new BookingAssignment(assignmentRooms[index], offer.Id, offer.ReferencePrice, index % 2)), selected).Success, Is.True);
        }

        static Run Simulate(bool distributed, bool useHeater)
        {
            var run = Create();
            var simulation = run.Simulation;
            float dt = 1 / run.Settings.TickRate;
            Start(run, 1, new[] { 102, 106, 101, 105 });
            var boundaries = new Boundaries(run);
            for (int step = 0; step < 20000 && !simulation.IsServiceComplete; step++)
            { simulation.Tick(dt); boundaries.Tick(); }
            Assert.That(simulation.IsServiceComplete, Is.True);
            var firstDay = simulation.EndShift();
            Assert.That(firstDay.Receipts.All(receipt => receipt.Price > 0), Is.True, "Every accepted first-day booking must receive actual room time.");
            Assert.That(simulation.ApplyMaintenance(0, MaintenanceChoice.Defer).Success, Is.True);
            for (int step = 0; step < 20000 && (simulation.Housekeeping.HasPendingWork ||
                run.Rooms.Any(room => room.DepartingGuestId != null) || simulation.Guests.Any(guest => guest.Agent.State != GuestAgentState.Left)); step++)
            { simulation.AdvancePreparation(dt); boundaries.Tick(); }
            Assert.That(run.Rooms.All(room => room.Cleanliness == Cleanliness.Clean && room.DepartingGuestId == null), Is.True);
            Assert.That(simulation.Housekeeping.HasPendingWork, Is.False);
            run.Events.AppendLine("Day1: gross=" + firstDay.Gross + ", net=" + firstDay.Net + ", condition after defer=" + simulation.Boiler.Condition + ", failed=" + simulation.Boiler.Failed);

            Start(run, 2, new[] { 101, 104, 106, distributed ? 103 : 105, 102 });
            boundaries = new Boundaries(run);
            run.Target = simulation.Guests.Single(guest => guest.RoomId == 104);
            run.Secondary = simulation.Guests.Single(guest => guest.RoomId == 106);
            Assert.That(simulation.Heaters.Register(HeaterId).Success, Is.True);
            var previousActivities = new Dictionary<string, GuestActivity>();
            Action<HotelRequest> recordRequest = request => run.Events.AppendLine(simulation.Elapsed.ToString("F1") +
                "s tick start: room " + request.RoomId + " " + request.Reason + " complaint");
            simulation.Requests.OnRequestCreated += recordRequest;
            try
            {
                for (int step = 0; step < 20000 && !simulation.IsServiceComplete; step++)
                {
                    simulation.Tick(dt); boundaries.Tick();
                    float time = simulation.Elapsed;
                    run.MaximumBoilerLoad = Math.Max(run.MaximumBoilerLoad, simulation.Boiler.Load);
                    run.MinimumHeatingOutput = Math.Min(run.MinimumHeatingOutput, simulation.Boiler.HeatingOutput);
                    foreach (var guest in simulation.Guests.Where(guest => guest.Agent.InAssignedRoom))
                    {
                        if (!previousActivities.TryGetValue(guest.GuestId, out var activity) || activity != guest.Agent.Activity)
                        {
                            previousActivities[guest.GuestId] = guest.Agent.Activity;
                            if (guest.Agent.Activity == GuestActivity.Shower) run.NaturalShowers++;
                        }
                    }
                    bool coldComplaint = simulation.Requests.Items.Any(request => request.GuestId == run.Target.GuestId &&
                        request.Reason == IncidentReason.Temperature && !request.Resolved) &&
                        run.Room(104).Temperature < run.Target.Application.Archetype.Needs.PreferredTemperatureMin;
                    if (run.Complaint < 0 && coldComplaint)
                    {
                        run.Complaint = time;
                        run.Events.AppendLine(time.ToString("F1") + "s: natural cold complaint; room104=" + run.Room(104).Temperature + ", boiler load=" + simulation.Boiler.Load);
                    }
                    if (run.ColdObserved < 0 && run.Target.Agent.InAssignedRoom &&
                        run.Target.Needs.Temperature.Severity > simulation.NeedsSettings.RecoverySeverityThreshold &&
                        run.Target.Needs.Temperature.ExposureSeconds >= simulation.NeedsSettings.ComplaintExposureSeconds * .5f &&
                        run.Room(104).Temperature < run.Target.Application.Archetype.Needs.PreferredTemperatureMin)
                    {
                        run.ColdObserved = time;
                        run.Events.AppendLine(time.ToString("F1") + "s: staff inspect sustained real cold in room104; thermometer=" + run.Room(104).Temperature);
                    }
                    // Staff can inspect a cold room before a formal complaint, then choose the portable workaround.
                    // The same 12-second transport budget and untouched thermal/electrical simulation still apply.
                    if (useHeater && run.HeaterOn < 0 && run.ColdObserved >= 0 && time >= run.ColdObserved + 12 && run.Target.Agent.InAssignedRoom)
                    {
                        Assert.That(simulation.Heaters.AssignRoom(HeaterId, 104).Success, Is.True);
                        Assert.That(simulation.Heaters.SetSwitchedOn(HeaterId, true).Success, Is.True);
                        simulation.RefreshElectrical();
                        run.HeaterOn = time;
                        run.PlacementTemperature = run.PretripWarmest = run.Room(104).Temperature;
                        run.PlacementDissatisfaction = run.BestPoweredDissatisfaction = run.Target.Needs.Temperature.Dissatisfaction;
                        run.Events.AppendLine(time.ToString("F1") + "s: staff place heater104; temperature=" + run.PlacementTemperature + ", circuitB demand=" + simulation.Electrical.Find("B").RequestedLoad);
                    }
                    var circuit = simulation.Electrical.Find("B");
                    if (run.HeaterOn >= 0 && !circuit.Tripped)
                    {
                        run.PretripWarmest = Math.Max(run.PretripWarmest, run.Room(104).Temperature);
                        run.BestPoweredDissatisfaction = Math.Min(run.BestPoweredDissatisfaction, run.Target.Needs.Temperature.Dissatisfaction);
                    }
                    if (circuit.Warning && run.Warning < 0) run.Warning = time;
                    if (circuit.Tripped && run.Trip < 0)
                    {
                        run.Trip = time;
                        Assert.That(circuit.DeliveredLoad, Is.Zero);
                        Assert.That(run.Room(106).HasPower, Is.False, "The downstream guest's real room must lose power.");
                        Assert.That(simulation.Heaters.Find(HeaterId).Powered, Is.False, "The same trip must disable the chosen workaround.");
                        run.Events.AppendLine(time.ToString("F1") + "s: natural B trip; requested=" + circuit.RequestedLoad + ", delivered=" + circuit.DeliveredLoad);
                    }
                    var secondaryCase = simulation.Incidents.Items.SingleOrDefault(incident => incident.GuestId == run.Secondary.GuestId &&
                        incident.Reason == IncidentReason.RoomCondition && incident.Cause?.SourceEntityId == "circuit/B/power");
                    if (secondaryCase != null)
                    {
                        run.SecondaryCaseExposure = Math.Max(run.SecondaryCaseExposure, secondaryCase.ExposureSeconds);
                        if (run.SecondaryObserved < 0)
                        {
                            Assert.That(run.Secondary.Agent.InAssignedRoom, Is.True);
                            Assert.That(run.Room(106).HasPower, Is.False);
                            Assert.That(secondaryCase.Stage, Is.EqualTo(SituationStage.Observed));
                            run.SecondaryObserved = time;
                            run.Events.AppendLine(time.ToString("F1") + "s: guest106 experiences circuit/B/power; observed without premature staff contact");
                        }
                    }
                    if (run.SecondaryComplaint < 0 && simulation.Requests.Items.Any(request => request.GuestId == run.Secondary.GuestId &&
                        request.Reason == IncidentReason.RoomCondition && !request.Resolved))
                    {
                        run.SecondaryComplaint = time;
                        run.Events.AppendLine(time.ToString("F1") + "s: secondary guest106 room-condition complaint; power=" + run.Room(106).HasPower);
                    }
                }
            }
            finally { simulation.Requests.OnRequestCreated -= recordRequest; }
            Assert.That(simulation.IsServiceComplete, Is.True);
            Assert.That(simulation.Guests.All(guest => guest.Agent.HasReachedRoom && guest.Elapsed > 0), Is.True);
            Assert.That(simulation.Boiler.LoadOverride, Is.Null);
            Assert.That(run.Rooms.All(room => !simulation.Noise.GetNoiseOverride(room.Profile.Id).HasValue), Is.True);
            run.EndTargetTemperature = run.Room(104).Temperature;
            run.Report = simulation.EndShift();
            TestContext.WriteLine("Historical shift trace (boiler rated4.2): distributed=" + distributed + ", heater=" + useHeater + ", seed=" + run.Seed +
                ", thermalTau=" + run.Settings.TemperatureTimeConstant + ", showers=" + run.NaturalShowers + ", peakBoilerLoad=" + run.MaximumBoilerLoad +
                ", minHeatOutput=" + run.MinimumHeatingOutput + ", coldObserved=" + run.ColdObserved + ", coldComplaint=" + run.Complaint + ", heaterAt=" + run.HeaterOn +
                ", temperatureAtPlacement=" + run.PlacementTemperature + ", poweredPeak=" + run.PretripWarmest +
                ", tempDissatisfaction=" + run.PlacementDissatisfaction + "→" + run.BestPoweredDissatisfaction +
                ", warning=" + run.Warning + ", trip=" + run.Trip + ", secondaryObserved=" + run.SecondaryObserved + ", secondaryComplaint=" + run.SecondaryComplaint +
                ", secondaryCaseExposure=" + run.SecondaryCaseExposure + ", secondaryPowerExposure=" + run.Secondary.PowerLossExposureSeconds + ", endTemp=" + run.EndTargetTemperature +
                ", gross=" + run.Report.Gross + ", refunds=" + run.Report.Compensation + ", net=" + run.Report.Net +
                ", secondaryScore=" + SecondaryReceipt(run).Satisfaction + ", secondaryReview=" + SecondaryReceipt(run).Review + "\n" + run.Events);
            return run;
        }

        static GuestReceipt SecondaryReceipt(Run run) => run.Report.Receipts.Single(receipt => receipt.GuestId == run.Secondary.GuestId);

        [Test]
        public void NaturalMeasuredColdAndChosenHeaterCreateRealSecondaryPowerLossWithoutAPrematureComplaint()
        {
            // Same real offers, prices, production seed and initial hotel. Only staff booking/tool decisions differ.
            var loaded = Simulate(false, true);
            var distributed = Simulate(true, true);
            var noHeater = Simulate(false, false);
            Assert.That(loaded.NaturalShowers, Is.GreaterThan(0));
            Assert.That(loaded.MaximumBoilerLoad, Is.GreaterThan(loaded.Settings.Boiler.SafeLoad));
            Assert.That(loaded.ColdObserved, Is.GreaterThan(0), "The staff response must follow sustained measured cold.");
            Assert.That(noHeater.Complaint, Is.GreaterThan(0), "Ignoring the same cold must still produce its natural guest complaint.");
            Assert.That(loaded.HeaterOn, Is.GreaterThan(loaded.ColdObserved));
            Assert.That(loaded.PretripWarmest, Is.GreaterThan(loaded.PlacementTemperature + 1), "The chosen heater must improve actual temperature before the trip.");
            Assert.That(loaded.Warning, Is.GreaterThan(loaded.HeaterOn));
            Assert.That(loaded.Trip, Is.GreaterThan(loaded.Warning));
            Assert.That(loaded.SecondaryObserved, Is.GreaterThanOrEqualTo(loaded.Trip), "The downstream case must identify the real electrical cause after its trip.");
            Assert.That(loaded.SecondaryCaseExposure, Is.GreaterThan(0));
            Assert.That(loaded.SecondaryCaseExposure, Is.LessThan(loaded.Simulation.NeedsSettings.ComplaintExposureSeconds),
                "This naturally late trip leaves less than a complaint interval before departure.");
            Assert.That(loaded.Secondary.PowerLossExposureSeconds, Is.GreaterThan(0));
            Assert.That(loaded.SecondaryComplaint, Is.LessThan(0), "Sub-threshold exposure must not fabricate an immediate staff complaint.");
            // Natural stays can have more than two actual problems. ReviewSystem reports its top two
            // exposures; the isolated Phase7 test checks the power phrase when it is a leading cause.
            Assert.That(SecondaryReceipt(loaded).Review, Is.Not.Empty);
            Assert.That(distributed.HeaterOn, Is.GreaterThan(distributed.ColdObserved));
            Assert.That(distributed.Trip, Is.LessThan(0), "Moving one booking to A should retain B headroom for the heater in this seeded scenario.");
            Assert.That(distributed.SecondaryObserved, Is.LessThan(0));
            Assert.That(distributed.Secondary.PowerLossExposureSeconds, Is.Zero);
            Assert.That(noHeater.Trip, Is.LessThan(0), "The same bookings without the added device must not create this electrical failure.");
            Assert.That(noHeater.SecondaryObserved, Is.LessThan(0));
            Assert.That(noHeater.SecondaryComplaint, Is.LessThan(0));
            Assert.That(SecondaryReceipt(noHeater).Review, Does.Not.Contain("electrical power"));
            Assert.That(SecondaryReceipt(loaded).Satisfaction, Is.LessThan(SecondaryReceipt(noHeater).Satisfaction));
            Assert.That(loaded.Report.Gross, Is.EqualTo(distributed.Report.Gross));
            Assert.That(loaded.Report.Gross, Is.EqualTo(noHeater.Report.Gross));
            Assert.That(loaded.Simulation.Guests.Select(guest => guest.GuestId), Is.EqualTo(noHeater.Simulation.Guests.Select(guest => guest.GuestId)));
        }
    }
}
