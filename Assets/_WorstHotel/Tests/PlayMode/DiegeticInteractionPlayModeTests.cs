using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace WorstHotel.Tests
{
    public sealed partial class Phase1PlayModeTests
    {
        IEnumerator ReadPhysicalBook(HotelBook kind)
        {
            var ui = ManagementUI.Instance; ui.Close();
            yield return new WaitForSecondsRealtime(.3f);
            var book = DiegeticBookInteraction.Find(kind);
            Assert.That(book, Is.Not.Null);
            var actor = bootstrap.Players[0];
            // Labelled approach fixture; the ray, use input and camera transition remain production code.
            var pose = new GameObject("Book approach fixture");
            var forward = book.transform.forward; forward.y = 0; forward.Normalize();
            var position = book.transform.position - forward * 1.45f; position.y = .08f;
            pose.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            actor.ResetToSpawn(pose.transform); Object.Destroy(pose);
            yield return WaitForGroundContact(actor);
            yield return AimAtKeyScenarioPoint(actor, padA, () => book.transform.position);
            Assert.That(actor.Interactor.Focused, Is.SameAs(book), "The visible book must receive the nearest ray, not its desk or another book.");
            yield return DiegeticPress(GamepadButton.South);
            Assert.That(ui.ReadingBook, Is.EqualTo(kind));
            yield return new WaitForSecondsRealtime(.5f);
            Assert.That(Vector3.Distance(actor.PlayerCamera.transform.position, book.transform.TransformPoint(new Vector3(0, 0, -1.17f))), Is.LessThan(.05f));
            var page = book.ScreenPage(actor.PlayerCamera);
            Assert.That(page.width, Is.GreaterThan(200));
            Assert.That(page.height, Is.GreaterThan(150));
        }

        IEnumerator DiegeticPress(GamepadButton button)
        {
            InputSystem.QueueStateEvent(padA, new GamepadState().WithButton(button));
            yield return new WaitForSecondsRealtime(.08f);
            InputSystem.QueueStateEvent(padA, new GamepadState());
            yield return new WaitForSecondsRealtime(.22f);
        }

        IEnumerator ChooseBook(string title)
        {
            var ui = ManagementUI.Instance;
            Assert.That(ui.BookOptionTitles.Any(t => t.StartsWith(title, StringComparison.Ordinal)), Is.True, title);
            for (int i = 0; i < 30 && !ui.FocusedBookOption.StartsWith(title, StringComparison.Ordinal); i++)
                yield return DiegeticPress(GamepadButton.DpadDown);
            Assert.That(ui.FocusedBookOption, Does.StartWith(title));
            yield return DiegeticPress(GamepadButton.South);
        }

        IEnumerator DiegeticCapture(string name)
        {
            // Editor capture can omit IMGUI; only the built-player presentation tour is
            // visual evidence for page text. These images are optional world diagnostics.
            for (int i = 0; i < 12; i++) yield return null;
            Directory.CreateDirectory("docs/screenshots/diegetic060");
            var texture = VerificationOffscreenCapture.Capture(bootstrap.Players.Where(p => p && p.PlayerCamera.enabled).ToArray());
            Assert.That(VerificationOffscreenCapture.LastOverlaySubmitted, Is.True);
            File.WriteAllBytes("docs/screenshots/diegetic060/" + name + ".png", texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        [UnityTest, Category("ContinuousOperations"), Category("AutomaticSales")]
        public IEnumerator DiegeticBooksUsePhysicalTargetsAndCannotBuyWingAtSpawn()
        {
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var ui = ManagementUI.Instance;
            Assert.That(ui.IsOpen, Is.False, "No automatic dashboard on arrival.");
            Assert.That(session.Economy.WingRestorationCost, Is.EqualTo(4500));
            Assert.That(session.Cash, Is.LessThan(session.Economy.WingRestorationCost));
            Assert.That(Object.FindObjectsByType<HotelWallClock>(FindObjectsSortMode.None).Length, Is.EqualTo(3));
            yield return DiegeticCapture("01-clean-hotel");
            yield return ReadPhysicalBook(HotelBook.Reservations);
            yield return DiegeticCapture("02-reservations");
            yield return ChooseBook("Room sales / rates");
            yield return ChooseBook("105 ·");
            yield return ChooseBook("Open to new sales");
            yield return ChooseBook("Apply sales policy");
            Assert.That(session.Simulation.RoomSalesPolicies.Single(p => p.RoomId == 105).OpenForSale, Is.True);
            yield return DiegeticCapture("03-room-rate");
            yield return ReadPhysicalBook(HotelBook.Accounts);
            yield return DiegeticCapture("04-accounts");
            yield return ReadPhysicalBook(HotelBook.Services);
            yield return DiegeticCapture("05-notes");
            yield return ReadPhysicalBook(HotelBook.Renovation);
            int cash = session.Simulation.Economy.Cash;
            Assert.That(session.RestoreNorthWing(0).Success, Is.False);
            Assert.That(session.Simulation.Economy.Cash, Is.EqualTo(cash));
            Assert.That(session.Simulation.NorthWingRestored, Is.False);
            yield return DiegeticCapture("06-renovation");
            yield return ReadPhysicalBook(HotelBook.BoilerManual);
            yield return DiegeticCapture("07-boiler-manual");
            yield return ChooseBook("Inspection and planned service");
            yield return DiegeticCapture("08-service-record");
            yield return DiegeticPress(GamepadButton.East);
            Assert.That(ui.IsOpen, Is.False);
            yield return new WaitForSecondsRealtime(.3f);
            Assert.That(bootstrap.Players[0].PlayerCamera.transform.localPosition.y, Is.EqualTo(1.61f).Within(.02f));
            Assert.That(bootstrap.Players[0].IsUIBlocked, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DiegeticPhoneAnswersOnFirstUseAndDoorKnockOpensCompactConversation()
        {
            yield return PrepareServiceGuestFixture(true);
            bootstrap.ConfigureSolo(); InputSystem.RemoveDevice(padB); padB = null;
            var session = GameSession.Instance; var ui = ManagementUI.Instance;
            var guest = session.Simulation.Guests.Single();
            var request = PreparePrivateColdConversation();
            yield return ReadPhysicalBook(HotelBook.Services);
            Assert.That(request.IsKnownToHotel, Is.False);
            yield return DiegeticCapture("09-private-request-not-written");
            ui.Close(); yield return new WaitForSecondsRealtime(.3f);
            Object.FindAnyObjectByType<GuestPresentation>().enabled = false;
            Assert.That(session.DebugBeginGuestContact(guest.GuestId, GuestContactChannel.Phone).Success, Is.True);
            Assert.That(session.Simulation.SignalGuestResponseAnchorReached(guest.GuestId, request.Response.Id,
                request.Response.ActionVersion, GuestResponseAnchor.RoomPhone).Success, Is.True);
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            var receiver = phone.GetComponent<PhoneHandsetPresentation>().handset;
            Vector3 rest = receiver.position;
            yield return FaceStation(bootstrap.Players[0], padA, phone, phone.GetComponent<Collider>().bounds.center);
            Assert.That(InteractionWords.Caption(bootstrap.Players[0]), Does.Contain("Answer").And.Not.Contain("101"));
            yield return DiegeticPress(GamepadButton.South);
            Assert.That(ui.IsWakePhoneOpen, Is.True);
            Assert.That(request.IsKnownToHotel, Is.True, "Answering the physical phone must immediately disclose the caller.");
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.Requested), "Picking up never silently promises help.");
            Assert.That(ui.ServiceOptionTitles, Has.None.EqualTo("Answer reception call"));
            Assert.That(Vector3.Distance(receiver.position, rest), Is.GreaterThan(.25f));
            yield return DiegeticCapture("10-answered-phone");
            yield return DiegeticPress(GamepadButton.South);
            Assert.That(request.Status, Is.EqualTo(ServiceStatus.InProgress));
            ui.Close(); yield return new WaitForSecondsRealtime(.4f);
            Assert.That(session.PhoneHolder, Is.EqualTo(-1));
            yield return ReadPhysicalBook(HotelBook.Services);
            yield return DiegeticCapture("11-accepted-promise");
            ui.Close(); yield return new WaitForSecondsRealtime(.3f);
            var door = GameObject.Find("Door101").GetComponent<DoorInteractable>();
            door.RequestClose(); yield return new WaitForSecondsRealtime(1);
            yield return ApproachPrivacyDoor(bootstrap.Players[0], false, door);
            yield return DiegeticPress(GamepadButton.South);
            Assert.That(ui.IsGuestContextOpen, Is.True, "First knock gives the guest's answer and contextual replies.");
            Assert.That(door.IsOpen, Is.False, "Conversation still requires permission to enter.");
            yield return DiegeticCapture("12-knock-conversation");
            ui.Close();
            LogAssert.NoUnexpectedReceived();
        }
    }
}
