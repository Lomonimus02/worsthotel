using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        static partial void BuildGameplay(GameObject gameplay)
        {
            var config = EnsureConfiguration();
            var session = gameplay.AddComponent<GameSession>(); session.config = config;
            gameplay.AddComponent<ManagementUI>();
            gameplay.AddComponent<DeveloperPanel>();
            gameplay.AddComponent<WaitController>().config = Asset<WaitConfig>("Assets/_WorstHotel/ScriptableObjects/Wait.asset", _ => { });
            gameplay.AddComponent<ShiftHUD>();
            AddLivingGuestPresentation(gameplay);
            AddLivingGuestAudio(gameplay);
            GameObject.Find("ReceptionTerminal").AddComponent<ReceptionTerminal>();
            AddRoomPlaques();
            AddBoilerReadout(gameplay);
            AddRepairControls(gameplay);
            AddPortableHeater(gameplay);
            AddStaffRoom(gameplay);
            var grabConfig = EnsureGrabConfiguration();
            foreach (var pickup in UnityEngine.Object.FindObjectsByType<PhysicsPickup>(FindObjectsSortMode.None))
                pickup.grabConfig = grabConfig;
        }

        public static GrabPhysicsConfig EnsureGrabConfiguration() =>
            Asset<GrabPhysicsConfig>("Assets/_WorstHotel/ScriptableObjects/GrabPhysics.asset", _ => { });

        static void AddBoilerReadout(GameObject gameplay)
        {
            var readout = gameplay.AddComponent<BoilerReadout>();
            readout.needle = GameObject.Find("GaugeAnchor").transform.Find("Needle");
            var lens = HotelKitAssets.Sphere("Pressure warning beacon", gameplay.transform,
                new Vector3(-.52f, 3.59f, 36.30f), new Vector3(.28f, .24f, .25f), "Warm lamp");
            readout.warningLens = lens.GetComponent<Renderer>();
            readout.warningLight = lens.AddComponent<Light>();
            readout.warningLight.type = LightType.Point; readout.warningLight.range = 4;
            readout.warningLight.color = new Color(1, .2f, .05f); readout.warningLight.intensity = 0;
            // A separate capacity plate leaves the existing dial calibrated to actual pressure.
            readout.capacityDisplay = new GameObject("Boiler capacity display");
            readout.capacityDisplay.transform.SetParent(gameplay.transform, false);
            HotelKitAssets.Box("Boiler capacity display backing", readout.capacityDisplay.transform, new Vector3(-.52f, 2.15f, 36.12f),
                new Vector3(2.00f, .47f, .045f), "Gauge ivory", true, false);
            readout.capacityReadout = HotelKitAssets.Text("Boiler actual capacity readout", readout.capacityDisplay.transform,
                "DEMAND / EFFECTIVE CAPACITY\nRATED · RESERVE\nLOAD · STRESS", new Vector3(-.52f, 2.15f, 36.085f), .061f, HotelKitAssets.Mat("Ink").color);
            var loadMeter = HotelKitAssets.Group("Boiler load meter", readout.capacityDisplay.transform, new Vector3(-1.12f, 2.15f, 36.04f)).transform;
            HotelKitAssets.Box("Load meter ivory face", loadMeter, Vector3.zero, new Vector3(.65f, .32f, .03f), "Gauge ivory", false, false);
            for (int mark = 0; mark < 7; mark++)
                HotelKitAssets.Box("Boiler load scale", loadMeter, new Vector3(-.26f + mark * .087f, .10f, -.024f), new Vector3(.012f, .04f, .01f), mark > 4 ? "Safety red" : "Ink", false, false);
            readout.loadNeedle = HotelKitAssets.Group("Load needle", loadMeter, new Vector3(0, -.11f, -.034f)).transform;
            HotelKitAssets.Box("Boiler load pointer", readout.loadNeedle, new Vector3(0, .115f, 0), new Vector3(.015f, .23f, .012f), "Safety red", false, false);
            readout.capacityReadout.transform.localPosition = new Vector3(-.08f, 2.15f, 36.00f);
            // Reuse the visible plate. Its front hitbox is clear of the gauge and the
            // emergency cabinet; the complete inspection surface is hidden in legacy mode.
            var inspectionCollider = readout.capacityDisplay.AddComponent<BoxCollider>();
            inspectionCollider.center = new Vector3(-.52f, 2.15f, 36.075f);
            inspectionCollider.size = new Vector3(2.00f, .47f, .06f);
            var inspection = readout.capacityDisplay.AddComponent<BoilerServiceInteraction>();
            inspection.displayName = "BOILER INSPECTION / SERVICE";
            inspection.instruction = "Inspect condition and choose Basic or Full Service";
            HotelKitAssets.Text("Boiler inspection instruction", readout.capacityDisplay.transform,
                "INSPECT / SERVICE", new Vector3(-.52f, 1.96f, 36.035f), .048f, HotelKitAssets.Mat("Ink").color);
        }

        public static SessionConfig EnsureConfiguration()
        {
            const string root = "Assets/_WorstHotel/ScriptableObjects/";
            Directory.CreateDirectory(root);
            AssetDatabase.Refresh();
            var budget = Asset<GuestArchetypeDefinition>(root + "BudgetTraveler.asset", x =>
            { x.kind = GuestKind.Budget; x.label = "Budget traveler"; x.description = "Easygoing about the building; sensitive to price."; x.referencePrice = 180; x.heatingDemand = .85f; x.coldThreshold = 18; x.patience = 90; x.noiseTolerance = .55f; x.coldPenaltyWeight = .70f; x.priceSensitivity = 28; });
            var cold = Asset<GuestArchetypeDefinition>(root + "ColdSensitiveGuest.asset", x =>
            { x.kind = GuestKind.ColdSensitive; x.label = "Cold-sensitive guest"; x.description = "Pays fairly. Needs a warm room, especially at night."; x.referencePrice = 300; x.heatingDemand = 1.05f; x.coldThreshold = 20; x.patience = 65; x.noiseTolerance = .40f; x.coldPenaltyWeight = 1.35f; x.priceSensitivity = 16; });
            var business = Asset<GuestArchetypeDefinition>(root + "BusinessGuest.asset", x =>
            { x.kind = GuestKind.Business; x.label = "Business guest"; x.description = "Pays well. Expects quiet, working fixtures and a quick response."; x.referencePrice = 450; x.heatingDemand = 1; x.coldThreshold = 19.5f; x.patience = 45; x.noiseTolerance = .25f; x.coldPenaltyWeight = 1; x.priceSensitivity = 10; });
            string[] names = { "Warm & quiet", "Cold tendency", "Pipe-side / noisy", "Degraded fixtures", "Quiet courtyard", "Drafty corner", "North garden", "North courtyard", "North quiet end", "North corner" };
            float[] losses = { 0, 1.4f, .2f, .4f, .3f, .7f, .4f, .6f, .3f, .8f };
            var rooms = new RoomDefinition[HotelLayout.RoomCount];
            for (int i = 0; i < rooms.Length; i++)
            {
                int index = i;
                rooms[i] = Asset<RoomDefinition>(root + "Room" + (101 + i) + ".asset", x =>
                { x.id = 101 + index; x.label = names[index]; x.heatLoss = losses[index]; x.temperature = 21; x.noise = index == 2 ? .6f : .10f; x.cleanliness = Cleanliness.Clean; x.repairState = index == 3 ? RepairState.Degraded : RepairState.Working; });
            }
            var boiler = Asset<BoilerConfig>(root + "Boiler.asset", _ => { });
            var economy = Asset<EconomyConfig>(root + "Economy.asset", _ => { });
            var session = Asset<SessionConfig>(root + "PrototypeSession.asset", x =>
            { x.guestArchetypes = new[] { budget, cold, business }; x.rooms = rooms; x.boiler = boiler; x.economy = economy; x.tickRate = 5; x.serviceSeconds = 300; x.totalDays = 3; x.day3BusinessReferencePrice = 525;
                x.continuousOperations = true; x.hotelDaySeconds = 720; x.openingHour = 8; x.reportHour = 6;
                x.ownershipContractEnabled = true; x.initialContractPayment = 250; x.contractDailyIncrease = 25; x.contractExtraRoomCharge = 60;
                x.contractPaymentHour = 22; x.firstContractPaymentDay = 2; x.operatingCostHour = 6;
                x.automaticBookings = true; x.initiallyOpenRooms = 4; x.roomSalePrice = 180;
                x.bookingBaseDemand = .9f; x.bookingPriceElasticity = 1.5f; x.firstDayBookingHour = 8.5f;
                x.advanceBookingHour = 16; x.bookingDecisionSpacingHours = .4f; x.bookingSeed = 73129; });
            // Extend older scene definitions without replacing any designer-tuned 0.1 field.
            if (!session.living)
            {
                session.living = Asset<LivingHotelConfig>(root + "LivingHotel.asset", living => { living.naturalDailyRhythm = true; });
                EditorUtility.SetDirty(session);
            }
            if (!session.needs)
            {
                session.needs = Asset<NeedConfig>(root + "GuestNeeds.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.noise)
            {
                session.noise = Asset<NoiseConfig>(root + "RoomNoise.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.heater)
            {
                session.heater = Asset<HeaterConfig>(root + "PortableHeater.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.electricity)
            {
                session.electricity = Asset<ElectricityConfig>(root + "Electricity.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.housekeeping)
            {
                session.housekeeping = Asset<HousekeepingConfig>(root + "Housekeeping.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.soloAssist)
            {
                session.soloAssist = Asset<SoloAssistConfig>(root + "SoloAssist.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            if (!session.services)
            {
                session.services = Asset<GuestServiceConfig>(root + "GuestServices.asset", services => services.naturalCommunicationEnabled = true);
                EditorUtility.SetDirty(session);
            }
            if (!session.infrastructure)
            {
                session.infrastructure = Asset<RoomInfrastructureConfig>(root + "RoomInfrastructure.asset", _ => { });
                EditorUtility.SetDirty(session);
            }
            session.rooms = rooms;
            EditorUtility.SetDirty(session);
            return session;
        }

        static T Asset<T>(string path, Action<T> initialize) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing; // Hand-tuned immutable definitions survive scene rebuilds.
            var created = ScriptableObject.CreateInstance<T>(); initialize(created);
            AssetDatabase.CreateAsset(created, path); return created;
        }
    }
}
