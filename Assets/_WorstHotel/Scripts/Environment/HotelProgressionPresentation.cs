using UnityEngine;

namespace WorstHotel
{
    public sealed class HotelProgressionPresentation : MonoBehaviour
    {
        public GameObject wingBarrier, boilerBurner, insulatedWindow, electricalA, electricalB, wingCorridorLights;
        public TextMesh wingSign, lobbySign;

        void LateUpdate()
        {
            var model = GameSession.Instance ? GameSession.Instance.Simulation : null;
            if (model == null) return;
            bool opened = model.NorthWingRestored;
            Set(wingBarrier, !opened); Set(wingCorridorLights, opened);
            Set(boilerBurner, model.Boiler.CapacityUpgradePurchased); Set(insulatedWindow, model.Room102Insulated);
            Set(electricalA, model.Electrical?.UpgradedCircuitId == "A"); Set(electricalB, model.Electrical?.UpgradedCircuitId == "B");
            string status = opened ? "NORTH WING  /  107 - 110\nOPEN · SET ROOM SALES AT RECEPTION" :
                "NORTH WING  /  CLOSED\n4 ROOMS · RESTORE AT RECEPTION";
            if (wingSign && wingSign.text != status) wingSign.text = status;
            if (lobbySign)
            {
                string summary = "RENOVATION LEDGER\n" + model.OperationalRoomCount + " ROOMS OPERATIONAL\n" +
                    (opened ? "NORTH WING OPEN" : "NORTH WING RESTORATION  $" + GameSession.Instance.Economy.WingRestorationCost);
                if (lobbySign.text != summary) lobbySign.text = summary;
            }
        }
        static void Set(GameObject obj, bool active) { if (obj && obj.activeSelf != active) obj.SetActive(active); }
    }
}
