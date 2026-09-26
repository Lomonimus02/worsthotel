#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        HotelSimulation agencyModel;
        readonly Dictionary<string, GuestActivity> agencyActivities = new Dictionary<string, GuestActivity>();

        // Explicit diagnostic fixture: sustain selected activities, without changing the production
        // source outputs, acoustic transmission, complaint thresholds, memory or recovery settings.
        // ForceActivity still requires the real presenter to reach and acknowledge its activity anchor.
        void MaintainAgencyActivities()
        {
            if (agencyModel == null || !ReferenceEquals(agencyModel, session.Simulation) || !agencyModel.Running) return;
            foreach (var pair in agencyActivities)
            {
                var guest = agencyModel.Guests.FirstOrDefault(g => g.GuestId == pair.Key);
                var agent = guest?.Agent;
                if (agent == null || !agent.IsRoomState || !agent.ActivityStaged) continue;
                if (agent.Activity != pair.Value || agent.NextActivityTime - agencyModel.Elapsed < 3)
                    Require(agencyModel.ForceActivity(pair.Key, pair.Value).Success, "sustain explicit agency activity fixture");
            }
        }

        IEnumerator SetAgencyActivity(GuestStay guest, GuestActivity activity)
        {
            agencyActivities[guest.GuestId] = activity;
            Require(agencyModel.ForceActivity(guest.GuestId, activity).Success, "request diagnostic " + activity);
            yield return Until(() => guest.Agent.Activity == activity && guest.Agent.ActivityStaged && AtAnchor(guest.GuestId),
                25, "real agency activity anchor: " + activity);
        }

        IEnumerator StartAgencyHotel(bool lifeCase)
        {
            Require(session.Phase == DayPhase.Planning, "agency fixture begins with a fresh disposable hotel");
            var specifications = lifeCase ? new[] { (GuestKind.Budget, 101), (GuestKind.Business, 103), (GuestKind.ColdSensitive, 105) } :
                new[] { (GuestKind.Budget, 101), (GuestKind.Business, 103) };
            foreach (var specification in specifications)
            {
                var offer = session.Plan.Applications.First(a => a.Archetype.Kind == specification.Item1);
                int rate = session.Economy.MinPrice + Mathf.RoundToInt((offer.ReferencePrice - session.Economy.MinPrice) /
                    (float)session.Economy.PriceStep) * session.Economy.PriceStep;
                session.Assign(0, offer.Id, specification.Item2, rate);
            }
            session.CommitPlan(0); ManagementUI.Instance.Close();
            Require(session.Phase == DayPhase.Service, "agency fixture plan opens service");
            agencyModel = session.Simulation; driveSpeed = 8;
            if (lifeCase)
                foreach (var guest in agencyModel.Guests)
                    Require(agencyModel.SetRoomTemperature(guest.RoomId, (guest.Application.Archetype.Needs.PreferredTemperatureMin +
                        guest.Application.Archetype.Needs.PreferredTemperatureMax) * .5f).Success, "explicit comfortable-temperature setup for quiet-life fixture");
            Position(coop.Players[0], new Vector3(.2f, .08f, 3), new Vector3(0, 1.5f, 8));
            foreach (var guest in agencyModel.Guests.OrderBy(g => g.Agent.ArrivalTime))
            {
                yield return Until(() => guest.Agent.State == GuestAgentState.WaitingForCheckIn, 25, "agency guest physically reaches reception");
                Require(agencyModel.Keys.PickUp(0, guest.RoomId).Success && agencyModel.CheckIn(0, guest.GuestId).Success,
                    "labelled agency model key pickup and handoff adapter");
                yield return Until(() => guest.Agent.InAssignedRoom && guest.Agent.ActivityStaged, 30,
                    "agency guest walks through their actual room door");
                yield return SetAgencyActivity(guest, GuestActivity.QuietRest);
            }
        }

        IEnumerator ResetAgencyHotel()
        {
            agencyActivities.Clear(); agencyModel = null;
            ManagementUI.Instance.Close();
            observedSimulation.Housekeeping.Changed -= ObserveCleaning;
            session.NewGame();
            for (int i = 0; i < 15; i++) yield return null;
            observedSimulation = session.Simulation;
            observedSimulation.Housekeeping.Changed += ObserveCleaning;
            session.Wait.Stop("Separate developer fixture / natural tour boundary"); session.Wait.enabled = false;
            driveSpeed = 8;
            VerifySoloComposition();
        }

        HotelIncident AgencyNoiseCase(string affected, string source) => agencyModel.Incidents.Items.FirstOrDefault(i =>
            i.GuestId == affected && i.Reason == IncidentReason.Noise && i.Cause?.SourceGuestId == source);

        IEnumerator FaceAgencyDoor(int roomId)
        {
            ManagementUI.Instance.Close(); driveSpeed = 1;
            var target = FindObjectsByType<RoomNoiseInteraction>(FindObjectsSortMode.None).Single(d => d.roomId == roomId);
            var collider = target.GetComponent<BoxCollider>();
            Require(collider, "agency physical door knocker collider");
            Vector3 aim = collider.bounds.center;
            Position(coop.Players[0], new Vector3(Mathf.Sign(aim.x) * .75f, .08f, aim.z), aim);
            yield return Until(() => coop.Players[0].Interactor.Focused == target ||
                coop.Players[0].Interactor.Focused == target.GetComponentInParent<DoorInteractable>(), 4,
                "actual corridor raycast focuses the guest entrance");
        }

        IEnumerator OpenAgencyConversation(int roomId, string capture = null)
        {
            yield return FaceAgencyDoor(roomId);
            var door = FindObjectsByType<RoomNoiseInteraction>(FindObjectsSortMode.None).Single(d => d.roomId == roomId);
            yield return PressMenu(GamepadButton.South);
            Require(door.HasAnswered(0), "first real controller press only knocks");
            Require(!ManagementUI.Instance.IsGuestContextOpen, "knock never silently chooses a response");
            yield return PressMenu(GamepadButton.South);
            Require(ManagementUI.Instance.IsGuestContextOpen, "second real controller press opens contextual choices");
            if (capture != null) yield return Capture(capture, "DIAGNOSTIC setup / actual door knock and controller-operated guest choices");
            else
            {
                // Hidden windows need a rendered original IMGUI frame to populate button actions.
                var frame = VerificationOffscreenCapture.Capture(new[] { coop.Players[0] });
                Destroy(frame);
                for (int i = 0; i < 3; i++) yield return null;
            }
        }

        IEnumerator VerifyGuestAgency()
        {
            Require(soloTour, "agency fixtures use an actual one-player SOLO composition");
            facts.Add("AGENCY FIXTURES: two separate disposable hotels. Explicit ForceActivity refresh sustains selected sources; SetRoomTemperature prepares the cold case. " +
                "Production noise output, transmission, needs, complaint/recovery and warning settings stay unchanged. Model key adapters are labelled; all guest arrival/activity/exit/relocation callbacks are production routes. Staff viewpoints use diagnostic repositioning; conversation choices use actual owned controller input.");
            yield return StartAgencyHotel(false);
            var source = agencyModel.Guests.Single(g => g.RoomId == 101);
            var affected = agencyModel.Guests.Single(g => g.RoomId == 103);
            yield return SetAgencyActivity(source, GuestActivity.LoudRoom);
            yield return Until(() => GuestLabels.IsActionable(AgencyNoiseCase(affected.GuestId, source.GuestId)), 20,
                "A: sustained identifiable TV source produces one measured complaint");
            var incident = AgencyNoiseCase(affected.GuestId, source.GuestId);
            Require(agencyModel.Incidents.Items.Count(i => i.GuestId == affected.GuestId && i.Reason == IncidentReason.Noise) == 1,
                "A: single persistent cause yields one situation");
            string identity = incident.Id;
            yield return FaceAgencyDoor(101);
            var roomAudio = FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Single(a => a.gameObject.name == "Room 101 activity sound");
            yield return Until(() => roomAudio.isPlaying && roomAudio.volume > .001f, 3, "A: the real TV loop is audible through the closed door");
            float raw = incident.Cause.RawIntensity, received = incident.Cause.ReceivedIntensity;
            yield return Capture("agency-noise-case", "DIAGNOSTIC sustained TV in101 / real received noise and persistent complaint in103");
            yield return OpenAgencyConversation(101, "agency-conversation");
            Require(ReadMenuField<int>(ManagementUI.Instance, "focus") == 0, "source conversation initially selects ask for quiet");
            yield return PressMenu(GamepadButton.South);
            Require(source.Memory.PreviousNoiseWarnings == 1 && source.Agent.QuietUntil > agencyModel.Elapsed,
                "A: actual contextual quiet choice changes source agreement and warning memory");
            yield return PressMenu(GamepadButton.East); driveSpeed = 8;
            yield return Until(() => incident.Resolved, 8, "A: lower actual source naturally resolves the measured situation");
            Require(affected.Memory.ProblemsResolvedSuccessfully > 0, "A: successful resolution is remembered");
            facts.Add("AGENCY A PASS: cause=" + identity + "; raw=" + raw.ToString("F3") + "; received=" + received.ToString("F3") +
                "; audibleAudioSource=True; realKnockAndContextQuiet=True; naturalRecovery=True.");

            yield return Until(() => incident.EpisodeCount >= 2 && GuestLabels.IsActionable(incident), 20,
                "B: noisy guest resumes after finite agreement and affected guest recalls the repeated category");
            Require(incident.Id == identity && affected.Memory.RepeatedProblemCount[IncidentReason.Noise] >= 1 &&
                incident.EffectivePatienceMultiplier < 1, "B: same case identity, repeat memory and lower patience");
            yield return OpenAgencyConversation(101, "agency-repeat");
            yield return PressMenu(GamepadButton.South);
            Require(source.Memory.PreviousNoiseWarnings == 2, "B: repeated real conversation records second warning");
            Require(source.Agent.QuietUntil - agencyModel.Elapsed < agencyModel.NoiseSettings.QuietRequestSeconds,
                "B: noisy source's second agreement has reduced duration");
            yield return PressMenu(GamepadButton.East); driveSpeed = 8;
            yield return Until(() => incident.Resolved, 8, "B: second measured recovery");
            facts.Add("AGENCY B PASS: persistentCase=" + identity + "; episodes=" + incident.EpisodeCount +
                "; complaints=" + affected.Memory.NumberOfComplaints + "; repeatedNoise=" + affected.Memory.RepeatedProblemCount[IncidentReason.Noise] +
                "; sourceWarnings=" + source.Memory.PreviousNoiseWarnings + "; effectivePatience=" + incident.EffectivePatienceMultiplier.ToString("F2") + ".");

            yield return Until(() => incident.EpisodeCount >= 3 && GuestLabels.IsActionable(incident), 20,
                "D: renewed source exposure produces the next episode before compensation");
            yield return OpenAgencyConversation(103);
            yield return Until(() => source.Agent.ActivityStaged && session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise > .5f,
                3, "D: sample a staged loud source before compensation");
            float beforeCredit = session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise;
            yield return PressMenu(GamepadButton.South); // Non-source complainant: compensation is the first contextual choice.
            Require(affected.Compensated && affected.Memory.CompensationReceived > 0 && incident.Active && !incident.Resolved,
                "D: selected compensation improves response but leaves the real problem active");
            yield return Capture("agency-credit", "DIAGNOSTIC renewed noise case / actual contextual compensation selected / credit recorded while cause persists");
            yield return PressMenu(GamepadButton.East); driveSpeed = 8;
            yield return Until(() => source.Agent.ActivityStaged && session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise > beforeCredit * .95f,
                3, "D: source continues at its prior output after compensation");
            facts.Add("AGENCY D PASS: credit=" + affected.Memory.CompensationReceived + "; caseStillActive=True; sourceBefore=" + beforeCredit.ToString("F3") +
                "; sourceAfter=" + session.Rooms.Single(r => r.Profile.Id == 101).SourceNoise.ToString("F3") + "; sourceNoiseUnchanged=True.");

            Require(agencyModel.RequestGuestMove(0, affected.GuestId, 106).Success, "C: reserve a genuinely free prepared destination");
            Require(agencyModel.Keys.PickUp(0, 106).Success && agencyModel.MoveGuest(0, affected.GuestId, 106).Success,
                "C: explicitly labelled model destination-key pickup and exchange adapter");
            yield return Until(() => affected.RoomId == 106 && affected.Agent.InAssignedRoom && !affected.Agent.IsRelocating && AtAnchor(affected.GuestId),
                25, "C: guest physically traverses origin door, corridor and destination door");
            yield return Until(() => incident.Resolved, 8, "C: changed exposure resolves the old case without a completion command");
            yield return Until(() => agencyModel.Noise.Sources.Any(s => s.SourceGuestId == source.GuestId && s.NoiseOutput > .5f),
                3, "C: original source remains physically staged and loud after relocation");
            Require(affected.Perception.Noise <= agencyModel.NeedsSettings.RecoverySeverityThreshold &&
                agencyModel.Noise.Sources.Any(s => s.SourceGuestId == source.GuestId && s.Active), "C: guest is out of exposure while the original source remains active");
            Position(coop.Players[0], new Vector3(.5f, .08f, 22), new Vector3(3, 1.6f, 22));
            yield return Capture("agency-relocation", "DIAGNOSTIC model key exchange / real guest route103→106 / original TV still active / exposure recovered");
            facts.Add("AGENCY C PASS: modelKeyAdapter=True; physicalRelocation=103->106; destinationReceived=" + affected.Perception.Noise.ToString("F3") +
                "; originalSourceStillActive=True; naturallyResolved=True.");
            yield return ResetAgencyHotel();

            yield return StartAgencyHotel(true);
            var budget = agencyModel.Guests.Single(g => g.RoomId == 101);
            var business = agencyModel.Guests.Single(g => g.RoomId == 103);
            var cold = agencyModel.Guests.Single(g => g.RoomId == 105);
            yield return SetAgencyActivity(budget, GuestActivity.WatchTV);
            yield return SetAgencyActivity(business, GuestActivity.Work);
            yield return SetAgencyActivity(cold, GuestActivity.Shower);
            Require(agencyModel.Incidents.Items.All(i => !GuestLabels.IsActionable(i)), "E: quiet background life does not manufacture complaint notifications");
            Position(coop.Players[0], new Vector3(-4.0f, .08f, 17), guests.roomMarkers.Single(m => m.roomId == 103).deskAnchor.position + Vector3.up);
            yield return Capture("agency-life", "DIAGNOSTIC varied quiet TV, desk work and shower / real anchors / no actionable guest case");
            facts.Add("AGENCY E PASS: stagedQuietTV=True; stagedDeskWork=True; stagedShower=True; actionableCases=0; natural seeded schedules are separately exercised by the three-day tour.");
            yield return SetAgencyActivity(budget, GuestActivity.QuietRest);
            yield return SetAgencyActivity(cold, GuestActivity.QuietRest);
            yield return SetAgencyActivity(business, GuestActivity.LoudRoom);
            Require(agencyModel.SetRoomTemperature(105, 10).Success, "F: explicit cold-room diagnostic setup");
            yield return Until(() => agencyModel.Incidents.Items.Any(i => i.GuestId == cold.GuestId && i.Reason == IncidentReason.Temperature && GuestLabels.IsActionable(i)),
                20, "F: cold-sensitive guest experiences actual cold and contacts staff");
            Position(coop.Players[0], new Vector3(-.6f, .08f, 22), new Vector3(-3, 1.4f, 22));
            yield return Capture("agency-cold", "DIAGNOSTIC thermometer set to10C / cold-sensitive guest105 / actual temperature complaint");
            Require(agencyModel.ForceLeaveRoom(cold.GuestId).Success, "F: request hotel exit without losing room ownership");
            driveSpeed = 8;
            yield return Until(() => cold.Agent.State == GuestAgentState.GuestAway, 25, "F: guest physically exits hotel through lobby");
            float coldExposure = cold.Needs.Temperature.ExposureSeconds, noiseExposure = cold.Needs.Noise.ExposureSeconds;
            float temperatureDissatisfaction = cold.Needs.Temperature.Dissatisfaction, noiseDissatisfaction = cold.Needs.Noise.Dissatisfaction;
            float awayUntil = agencyModel.Elapsed + Mathf.Min(10, agencyModel.LivingSettings.AwayDurationMin * .35f);
            yield return Until(() => agencyModel.Elapsed >= awayUntil, 6, "F: observe physically-away perception for several hotel seconds");
            Require(cold.Agent.State == GuestAgentState.GuestAway && cold.Agent.CheckedIn &&
                session.Rooms.Single(r => r.Profile.Id == 105).GuestId == cold.GuestId, "F: away guest retains occupied room and key");
            Require(Mathf.Abs(cold.Needs.Temperature.ExposureSeconds - coldExposure) < .001f && Mathf.Abs(cold.Needs.Noise.ExposureSeconds - noiseExposure) < .001f &&
                cold.Needs.Temperature.Dissatisfaction <= temperatureDissatisfaction + .001f && cold.Needs.Noise.Dissatisfaction <= noiseDissatisfaction + .001f,
                "F: no room temperature/noise exposure or dissatisfaction accumulates while physically away");
            Position(coop.Players[0], new Vector3(0, .08f, 3.8f), new Vector3(0, 1.5f, -2));
            yield return Capture("agency-away", "DIAGNOSTIC requested exit / actual exterior route / retained room / room perception suspended");
            Require(agencyModel.ForceReturnRoom(cold.GuestId).Success, "F: request return through actual hotel entrance");
            yield return Until(() => cold.Agent.InAssignedRoom && AtAnchor(cold.GuestId), 25, "F: real lobby-to-room return");
            Require(agencyModel.SetRoomTemperature(105, 10).Success, "F: restore explicit cold condition for return measurement");
            yield return Until(() => cold.Needs.Temperature.ExposureSeconds > coldExposure + 1 && cold.Needs.Noise.ExposureSeconds > noiseExposure + 1,
                8, "F: perception resumes only after the guest returns to the actual room");
            Position(coop.Players[0], new Vector3(-4.4f, .08f, 21), guests.roomMarkers.Single(m => m.roomId == 105).rest.position + Vector3.up);
            yield return Capture("agency-return", "DIAGNOSTIC return / actual room anchor / cold and nearby noise exposure resume");
            facts.Add("AGENCY F PASS: coldSensitive=True; physicalExitReturn=True; awayTemperatureExposureDelta=0; awayNoiseExposureDelta=0; roomAndKeyRetained=True; perceptionResumed=True.");
            facts.Add("GuestAgencyVerified=True Scenarios=A,B,C,D,E,F NoSyntheticGuestRouteCallbacks=True");
            yield return ResetAgencyHotel();
        }
    }
}
#endif
