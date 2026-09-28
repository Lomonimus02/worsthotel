using System.Linq;
using UnityEngine;

namespace WorstHotel
{
    public enum HotelBook { Reservations, Services, Accounts, Renovation, BoilerManual }

    public sealed class DiegeticBookInteraction : HotelInteractable
    {
        public HotelBook kind;
        public string ShortTitle => kind switch {
            HotelBook.Reservations => "reservations", HotelBook.Services => "notes",
            HotelBook.Accounts => "accounts", HotelBook.Renovation => "renovation plans", _ => "manual" };
        public static DiegeticBookInteraction Find(HotelBook kind) =>
            FindObjectsByType<DiegeticBookInteraction>(FindObjectsSortMode.None).FirstOrDefault(book => book.kind == kind);
        public override string GetPrompt(PlayerInteractor actor) => "Read " + ShortTitle;
        public override void Interact(PlayerInteractor actor)
        {
            var lan = LanSession.Instance;
            if (lan && lan.Role == LanRole.Host && actor.ActorId == 1) lan.RequestRemoteBook(kind);
            else ManagementUI.Instance?.OpenBook(actor.ActorId, kind);
        }

        public Rect ScreenPage(Camera camera)
        {
            var a = camera.WorldToScreenPoint(transform.TransformPoint(new Vector3(-.54f, .36f, -.045f)));
            var b = camera.WorldToScreenPoint(transform.TransformPoint(new Vector3(.54f, -.36f, -.045f)));
            return new Rect(a.x, Screen.height - a.y, b.x - a.x, a.y - b.y);
        }
    }
}
