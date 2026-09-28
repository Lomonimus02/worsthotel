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
            string status = opened ? "NORTH WING  /  107 - 110\nGUEST ROOMS" :
                "NORTH WING  /  CLOSED\nRESTORATION PENDING";
            if (wingSign && wingSign.text != status) wingSign.text = status;
            if (lobbySign) lobbySign.text = "NORTH WING" + (opened ? "\nROOMS 107–110" : "\nCLOSED FOR RENOVATION");
        }
        static void Set(GameObject obj, bool active) { if (obj && obj.activeSelf != active) obj.SetActive(active); }
    }
}
