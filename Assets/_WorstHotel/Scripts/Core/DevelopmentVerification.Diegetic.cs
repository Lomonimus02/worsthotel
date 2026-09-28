#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace WorstHotel
{
    public sealed partial class DevelopmentVerification
    {
        // Opt-in Windows presentation capture using the existing runner, not a human-play claim.
        IEnumerator VerifyDiegeticPresentation()
        {
            driveSpeed = 1;
            facts.Add("DIEGETIC PRESENTATION FIXTURE: explicit viewpoint placement, book opening and known guest setup. Physical input is checked separately by three focused PlayMode scenarios. Human walk-through remains required.");
            var ui = ManagementUI.Instance; var actor = coop.Players[0];
            ui.Close();
            Position(actor, new Vector3(-1.1f, .08f, -.6f), new Vector3(-5, 1.5f, 2.5f));
            yield return Capture("01-clean-reception", "Normal HUD / current reception layout");
            foreach (var kind in new[] { HotelBook.Reservations, HotelBook.Services, HotelBook.Accounts, HotelBook.Renovation, HotelBook.BoilerManual })
            {
                yield return PresentBook(kind);
                yield return Capture("book-" + kind, "Physical " + kind + " / actual Windows IMGUI");
                if (kind == HotelBook.Reservations)
                {
                    for (int i = 0; i < 3; i++) yield return PressMenu(GamepadButton.DpadDown);
                    yield return PressMenu(GamepadButton.South);
                    yield return Capture("room-sales", "Reservation book sales page");
                }
                if (kind == HotelBook.BoilerManual)
                {
                    yield return PressMenu(GamepadButton.South);
                    yield return Capture("service-record", "Manual inspection and service page");
                }
            }
            ui.Close(); yield return new WaitForSecondsRealtime(.35f);
            Position(actor, new Vector3(25, .08f, 3.5f), new Vector3(24.5f, 2.0f, 6.0f));
            yield return Capture("boiler-instruments", "Physical pressure / load / catch / labels");
            electricalPanel.cover.RequestOpen();
            yield return new WaitForSecondsRealtime(.8f);
            Position(actor, new Vector3(26.4f, .08f, 2.3f), new Vector3(28.9f, 1.65f, 2.3f));
            yield return Capture("electrical-instruments", "Local ammeters and actual A/B circuit labels");
            Require(session.Simulation.Electrical.ForceTrip("A").Success, "explicit branch A blackout fixture");
            Position(actor, new Vector3(0, .08f, 7.2f), new Vector3(0, 1.6f, 20));
            yield return Capture("blackout-A", "A off / B on: actual lights and surfaces");
            Require(session.Simulation.Electrical.ForceTrip("B").Success, "explicit branch B blackout fixture");
            yield return Capture("blackout-both", "Both branches off / no failure banner");

            session.NewGame(); guests.enabled = false;
            var model = session.Simulation;
            session.AdvanceTime(70);
            var reservation = model.Reservations.First();
            session.AdvanceTime(Mathf.Max(0, reservation.Offer.ArrivalAt - model.Elapsed + .2f));
            var guest = model.Guests.First(g => g.GuestId == reservation.Id);
            if (guest.Agent.State == GuestAgentState.Arriving) session.ReportGuestReachedReception(guest.GuestId);
            Require(model.Keys.PickUp(0, guest.RoomId).Success && model.CheckIn(0, guest.GuestId).Success, "labelled guest key fixture");
            Require(session.ReportGuestReachedRoom(guest.GuestId).Success, "labelled guest room fixture");
            Require(session.DebugSetMildCold(guest.GuestId).Success, "labelled cold fixture"); session.AdvanceTime(.3f);
            Require(session.DebugForceService(guest.GuestId, ServiceKind.ExtraBlanket).Success, "labelled private blanket request");
            var request = model.Services.Cases.First(c => c.GuestId == guest.GuestId && c.Kind == ServiceKind.ExtraBlanket && c.Active);
            Require(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, "labelled phone contact fixture");
            Require(model.SignalGuestResponseAnchorReached(guest.GuestId, request.Response.Id, request.Response.ActionVersion, GuestResponseAnchor.RoomPhone).Success, "labelled phone anchor fixture");
            var phone = FindAnyObjectByType<ReceptionPhoneInteraction>();
            var aim = phone.GetComponent<Collider>().bounds.center;
            Position(actor, new Vector3(aim.x, .08f, aim.z - 1.45f), aim); actor.enabled = true;
            // Keep the actual physical ray at the phone; OpenWakePhone grants no authority without it.
            yield return null; yield return null;
            actor.PlayerCamera.transform.LookAt(aim);
            yield return null;
            actor.enabled = false;
            actor.PlayerCamera.transform.LookAt(aim);
            yield return null;
            Require(actor.Interactor.Focused == phone, "actual phone ray in presentation fixture");
            phone.Interact(actor.Interactor);
            Require(ui.IsWakePhoneOpen && request.IsKnownToHotel, "first physical phone use answers immediately");
            yield return Capture("phone-answer", "Caller disclosed only after answering / physical lifted handset");
            Require(session.RespondToService(0, request.Id, true).Success, "explicit promise accepted");
            ui.Close();
            yield return PresentBook(HotelBook.Services);
            yield return Capture("known-service-note", "Accepted communicated promise in physical notes");
            ui.Close(); finalCash = model.Economy.Cash;
        }

        IEnumerator PresentBook(HotelBook kind)
        {
            var ui = ManagementUI.Instance; ui.Close();
            yield return new WaitForSecondsRealtime(.3f);
            var book = DiegeticBookInteraction.Find(kind);
            Require(book, "physical book exists: " + kind);
            var forward = book.transform.forward; forward.y = 0; forward.Normalize();
            var point = book.transform.position - forward * 1.45f; point.y = .08f;
            Position(coop.Players[0], point, book.transform.position);
            ui.OpenBook(0, kind);
            yield return new WaitForSecondsRealtime(.6f);
        }
    }
}
#endif
