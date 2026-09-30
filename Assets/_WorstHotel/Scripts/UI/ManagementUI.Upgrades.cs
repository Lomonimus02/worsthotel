using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        void UpdateOperationsUpgrades()
        {
            var model = Session.Simulation;
            var costs = Session.Economy;
            AddOperationsChoice(58, 293, 673, 37, model.Boiler.CapacityUpgradePurchased ? "Boiler upgrade installed" :
                "Install new burner · $" + costs.BoilerUpgradeCost, () => Session.PurchaseBoilerUpgrade(owner),
                !model.Boiler.CapacityUpgradePurchased && model.Economy.Cash >= costs.BoilerUpgradeCost);
            if (model.Electrical != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    string id = i == 0 ? "A" : "B";
                    bool available = !model.Electrical.IsCapacityUpgraded(id);
                    AddOperationsChoice(58 + i * 343, 428, 330, 37, available ? "Upgrade " + id + " · $" + costs.ElectricalUpgradeCost :
                        "Upgrade installed on " + id, () => Session.PurchaseElectricalUpgrade(owner, id),
                        available && model.Economy.Cash >= costs.ElectricalUpgradeCost);
                }
            }
            AddOperationsChoice(58, 558, 673, 37, model.Room102Insulated ? "Room 102 window work complete" :
                "Seal Room 102 windows · $" + costs.InsulationUpgradeCost, () => Session.PurchaseInsulation(owner),
                !model.Room102Insulated && model.Economy.Cash >= costs.InsulationUpgradeCost);
            AddOperationsChoice(58, 702, 673, 39, model.NorthWingRestored ? "North Wing restored" :
                "Restore North Wing · $" + costs.WingRestorationCost, () =>
                { Session.RestoreNorthWing(owner); },
                !model.NorthWingRestored && model.Economy.Cash >= costs.WingRestorationCost);
        }

        void DrawOperationsUpgrades()
        {
            var model = Session.Simulation; var boiler = model.Boiler;
            Fill(new Rect(42, 196, 705, 144), LightPaper);
            Label(new Rect(58, 207, 673, 30), "HEATING / NEW BURNER", Heading);
            float rated = boiler.CapacityUpgradePurchased ? boiler.RatedCapacity : boiler.RatedCapacity * Session.BoilerSettings.Capacity.CapacityUpgradeMultiplier;
            Label(new Rect(58, 245, 673, 45), "Rated " + boiler.RatedCapacity.ToString("F2") + " → " + rated.ToString("F2") +
                " u. Same guests, more reserve. Existing wear still needs service.", Small, Muted);
            Fill(new Rect(42, 350, 705, 125), LightPaper);
            Label(new Rect(58, 359, 673, 30), "ELECTRICITY / INDEPENDENT A + B UPGRADES", Heading);
            Label(new Rect(58, 394, 673, 32), "Permanent +" + model.Electrical.Settings.CapacityUpgradeAmount.ToString("F2") +
                " u per branch, purchased separately. Reset tripped breakers separately.", Small, Muted);
            Fill(new Rect(42, 485, 705, 120), LightPaper);
            Label(new Rect(58, 493, 673, 30), "COLD ROOM 102 / WINDOW INSULATION", Heading);
            Label(new Rect(58, 528, 673, 30), "80% less window heat loss and 25% lower base heating demand. Permanent.", Small, Muted);
            Fill(new Rect(42, 615, 705, 137), LightPaper);
            Label(new Rect(58, 623, 673, 30), "NORTH WING / FOUR MORE ROOMS", Heading);
            Label(new Rect(58, 660, 673, 39), "Rooms 107–110, beyond the plant hall. Restoration opens the barrier immediately.\nNew rooms start closed for sale. Existing A/B circuits and boiler supply them.", Small, Muted);
            Label(new Rect(42, 762, 705, 38), model.OperationalRoomCount + "/10 rooms operational · cash $" + model.Economy.Cash +
                " · spent this period $" + model.PeriodCapitalSpend, Small, Teal);
        }
    }
}
