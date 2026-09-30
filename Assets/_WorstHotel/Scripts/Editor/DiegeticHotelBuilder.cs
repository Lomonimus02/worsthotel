using System.Linq;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static void BuildDiegeticHotel(GameObject gameplay)
        {
            gameplay.AddComponent<HotelSubtitle>();
            RemoveObject("ReceptionTerminal");
            RemoveObject("Guest ledger");
            RemoveObject("Renovation notice at reception");
            RemoveObject("Sign STAFF NOTICE PLEASE LET THE NEXT SHIFT SLEEP");
            RemoveObject("Reception guest service board");
            RemoveObject("Sign BELL CART PUSH FROM HANDLE");
            var bellTarget = GameObject.Find("Service bell");
            if (bellTarget)
            {
                bellTarget.AddComponent<ServiceBellInteraction>().displayName = "Reception bell";
                if (!bellTarget.GetComponent<Collider>()) bellTarget.AddComponent<SphereCollider>();
            }
            var root = Group("Hotel books and clocks", gameplay.transform).transform;
            BuildHotelBook(root, HotelBook.Reservations, "RESERVATIONS", new Vector3(-5.40f, 1.62f, 2.30f), new Vector3(65, 0, 0), "Burgundy velvet");
            BuildHotelBook(root, HotelBook.Services, "RECEPTION NOTES", new Vector3(-7.48f, 1.62f, 2.22f), new Vector3(65, 0, 0), "Teal upholstery");
            BuildHotelBook(root, HotelBook.Accounts, "ACCOUNTS", new Vector3(-4.10f, 1.62f, 2.30f), new Vector3(65, 0, 0), "Mahogany");
            BuildHotelBook(root, HotelBook.Renovation, "RENOVATION", new Vector3(-2.80f, 1.62f, 2.30f), new Vector3(65, 0, 0), "Boiler enamel");
            var phone = Object.FindAnyObjectByType<ReceptionPhoneInteraction>();
            if (phone) phone.transform.position = new Vector3(-6.53f, 1.45f, 2.95f);
            foreach (string name in new[] { "Service bell", "Desk bell base" })
            { var bell = GameObject.Find(name); if (bell) { var pos = bell.transform.position; pos.z = 3.35f; bell.transform.position = pos; } }

            // The manual sits on the existing workbench, within the boiler room's clear aisle.
            BuildHotelBook(root, HotelBook.BoilerManual, "ENGINEER'S MANUAL", new Vector3(21.60f, 1.23f, 1.40f), new Vector3(65, -90, 0), "Mahogany");
            BuildSupplyLedger(root);
            BuildHotelClock(root, "Lobby wall clock", new Vector3(6f, 3.08f, 5.51f), 0);
            BuildHotelClock(root, "Staff room clock", new Vector3(18.25f, 2.35f, 4.62f), 0);
            BuildHotelClock(root, "Plant room clock", new Vector3(24.9f, 2.60f, 9.28f), 0);

            foreach (var plaque in Object.FindObjectsByType<RoomStatusPlaque>(FindObjectsSortMode.None))
                if (plaque.label) { plaque.label.text = plaque.roomId.ToString(); plaque.label.characterSize *= 1.5f; }
            foreach (var employee in Object.FindObjectsByType<HousekeeperPresentation>(FindObjectsSortMode.None))
                if (employee.statusLabel) employee.statusLabel.gameObject.SetActive(false);
            foreach (var lamp in Object.FindObjectsByType<RoomLampInteraction>(FindObjectsSortMode.None))
                if (lamp.statusLabel) lamp.statusLabel.gameObject.SetActive(false);
            foreach (var bed in Object.FindObjectsByType<LinenBedInteraction>(FindObjectsSortMode.None))
                if (bed.statusLabel) bed.statusLabel.gameObject.SetActive(false);
            foreach (var label in Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None))
                if (label.name == "Blanket bed label" || label.name == "Boiler inspection instruction") label.gameObject.SetActive(false);
            // Remove complete explanatory boards, including their backing, not just their ink.
            foreach (var part in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (part && (part.name.StartsWith("Sign BOILER REPAIR") || part.name.StartsWith("Sign 1  RELIEF") || part.name.StartsWith("Sign 3  BOILER")))
                    Object.DestroyImmediate(part.gameObject);
            var panelLabel = GameObject.Find("Panel label"); if (panelLabel) panelLabel.GetComponent<TextMesh>().text = "SERVICE";
            var restartLabel = GameObject.Find("Restart label"); if (restartLabel) restartLabel.GetComponent<TextMesh>().text = "RESTART";
            var breaker = GameObject.Find("BreakerAnchor");
            if (breaker) Text("Printed boiler isolator label", breaker.transform, "POWER", new Vector3(0, .42f, -.026f), .061f, Mat("Ink").color);
            var valve = GameObject.Find("ValveAnchor");
            if (valve)
            {
                Text("Small relief valve label", valve.transform, "RELIEF", new Vector3(0, -.28f, -.10f), .049f, Lettering);
                var catchFlag = Box("Relief catch flag", valve.transform, new Vector3(.25f, -.17f, -.10f), new Vector3(.085f, .12f, .035f), "Safety red", false, false);
                gameplay.GetComponent<BoilerReadout>().reliefCatchFlag = catchFlag.GetComponent<Renderer>();
                Text("Catch label", valve.transform, "CATCH", new Vector3(.25f, -.29f, -.12f), .026f, Lettering);
            }
            var readout = gameplay.GetComponent<BoilerReadout>();
            if (readout && readout.capacityReadout)
            {
                readout.capacityReadout.characterSize *= .9f;
                readout.capacityReadout.text = "LOAD    NORMAL\nSERVICE";
            }
        }

        static void RemoveObject(string name) { var obj = GameObject.Find(name); if (obj) Object.DestroyImmediate(obj); }

        static void BuildHotelBook(Transform parent, HotelBook kind, string title, Vector3 position, Vector3 rotation, string cover)
        {
            var obj = Group(title + " book", parent, position, rotation);
            var book = obj.AddComponent<DiegeticBookInteraction>(); book.kind = kind; book.displayName = title;
            Box("Worn book cover", obj.transform, new Vector3(0, 0, .025f), new Vector3(1.18f, .80f, .055f), cover, true);
            foreach (int side in new[] { -1, 1 })
            {
                Box("Cream paper pages", obj.transform, new Vector3(side * .278f, 0, -.012f), new Vector3(.54f, .74f, .046f), "Gauge ivory", true, false);
                for (int row = 0; row < 9; row++)
                    Box("Faint ruled page", obj.transform, new Vector3(side * .278f, .20f - row * .045f, -.037f), new Vector3(.46f, .0012f, .001f), "Aged brass", false, false);
            }
            Box("Book spine", obj.transform, new Vector3(0, 0, -.033f), new Vector3(.016f, .76f, .015f), cover, false, false);
            Text("Printed book title", obj.transform, title, new Vector3(-.275f, .27f, -.041f), .030f, Mat("Ink").color);
            Text("Hotel book imprint", obj.transform, "THE WORST HOTEL EVER", new Vector3(.275f, .27f, -.041f), .020f, Mat("Ink").color);
            Box("Brass page tab", obj.transform, new Vector3(.58f, .20f, .012f), new Vector3(.10f, .12f, .03f), "Aged brass", false, false);
        }

        static void BuildHotelClock(Transform parent, string name, Vector3 position, float yaw)
        {
            var obj = Group(name, parent, position, new Vector3(0, yaw, 0));
            var clock = obj.AddComponent<HotelWallClock>();
            Cylinder("Clock brass case", obj.transform, Vector3.zero, .49f, .10f, "Aged brass", new Vector3(90, 0, 0));
            Cylinder("Clock dial", obj.transform, new Vector3(0, 0, -.06f), .455f, .025f, "Gauge ivory", new Vector3(90, 0, 0));
            for (int i = 1; i <= 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                Text("Clock numeral", obj.transform, i.ToString(), new Vector3(Mathf.Sin(angle) * .35f, Mathf.Cos(angle) * .35f, -.08f), .055f, Mat("Ink").color);
            }
            clock.hourHand = Group("Clock hour hand", obj.transform, new Vector3(0, 0, -.095f)).transform;
            clock.minuteHand = Group("Clock minute hand", obj.transform, new Vector3(0, 0, -.105f)).transform;
            Box("Hour pointer", clock.hourHand, new Vector3(0, .105f, 0), new Vector3(.034f, .25f, .01f), "Ink", false, false);
            Box("Minute pointer", clock.minuteHand, new Vector3(0, .145f, 0), new Vector3(.021f, .34f, .01f), "Ink", false, false);
            Box("Clock calendar card", obj.transform, new Vector3(0, -.60f, -.03f), new Vector3(.82f, .18f, .035f), "Gauge ivory", false, false);
            clock.dateCard = Text("Clock date and time", obj.transform, "DAY 1   08:00", new Vector3(0, -.60f, -.055f), .047f, Mat("Ink").color);
        }
    }
}
