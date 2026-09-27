using UnityEngine;
using static WorstHotel.HotelTheme;

namespace WorstHotel
{
    public sealed partial class ManagementUI
    {
        void UpdateOperationsUpgrades()
        {
            var model = Session.Simulation; var boiler = model.Boiler;
            bool boilerAffordable = model.Economy.Cash >= Session.Economy.BoilerUpgradeCost;
            AddOperationsChoice(58, 358, 673, 43, boiler.CapacityUpgradePurchased ? "Boiler upgrade installed" :
                "Buy boiler capacity upgrade · $" + Session.Economy.BoilerUpgradeCost + (boilerAffordable ? "" : " · insufficient cash"),
                () => Session.PurchaseBoilerUpgrade(owner), !boiler.CapacityUpgradePurchased && boilerAffordable);
            if (model.Electrical == null) return;
            bool electricalAvailable = string.IsNullOrEmpty(model.Electrical.UpgradedCircuitId);
            bool electricalAffordable = model.Economy.Cash >= Session.Economy.ElectricalUpgradeCost;
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? "A" : "B";
                var circuit = model.Electrical.Find(id);
                AddOperationsChoice(58, 550 + i * 59, 673, 43, !electricalAvailable ?
                    "Circuit " + id + (model.Electrical.UpgradedCircuitId == id ? " · upgrade installed" : " · upgrade used on circuit " + model.Electrical.UpgradedCircuitId) :
                    "Upgrade circuit " + id + " · " + circuit.Capacity.ToString("F2") + " → " +
                    (circuit.Capacity + model.Electrical.Settings.CapacityUpgradeAmount).ToString("F2") + " u · $" + Session.Economy.ElectricalUpgradeCost +
                    (electricalAffordable ? "" : " · insufficient cash"), () => Session.PurchaseElectricalUpgrade(owner, id), electricalAvailable && electricalAffordable);
            }
        }

        void DrawOperationsUpgrades()
        {
            var model = Session.Simulation; var boiler = model.Boiler;
            float multiplier = boiler.CapacityUpgradePurchased ? 1 : Session.BoilerSettings.Capacity.CapacityUpgradeMultiplier;
            Fill(new Rect(42, 196, 705, 221), LightPaper);
            Label(new Rect(58, 207, 673, 32), "BOILER · ONE CAPACITY UPGRADE", Heading);
            Label(new Rect(58, 252, 673, 98), "Rated capacity " + boiler.RatedCapacity.ToString("F2") + " → " + (boiler.RatedCapacity * multiplier).ToString("F2") +
                " u · effective now " + boiler.EffectiveCapacity.ToString("F2") + " → " + (boiler.EffectiveCapacity * multiplier).ToString("F2") + " u\n" +
                "Condition " + boiler.Condition.ToString("F0") + "% · " + BoilerMaintenanceLabels.State(model) +
                "\nIncreases capacity permanently. Existing wear, stress, patch penalty and maintenance downtime remain.", Small, Muted);
            Fill(new Rect(42, 435, 705, 234), LightPaper);
            Label(new Rect(58, 446, 673, 32), "ELECTRICITY · CHOOSE ONE CIRCUIT", Heading);
            Label(new Rect(58, 489, 673, 54), "One electrical upgrade for the hotel: A or B. Adds capacity without removing consumer demand.\nA tripped breaker still needs a physical reset; an upgrade is not a repair.", Small, Muted);
            Label(new Rect(42, 676, 705, 22), "Capital purchases this reporting period: $" + model.PeriodCapitalSpend, Small, Teal);
        }
    }
}
