using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        public static void AddHousekeeper(GameObject gameplay)
        {
            var root = Group("Housekeeping", gameplay.transform);
            var view = root.AddComponent<HousekeeperPresentation>();
            view.guests = gameplay.GetComponent<GuestPresentation>();
            view.standby = Group("Housekeeper standby", root.transform, new Vector3(-2.3f, .01f, 31.2f), new Vector3(0, 180, 0)).transform;
            view.worker = Group("Mara the housekeeper", root.transform, view.standby.position, new Vector3(0, 180, 0)).transform;
            view.body = Group("Housekeeper body", view.worker).transform;
            Sphere("Teal uniform", view.body, new Vector3(0, 1.02f, 0), new Vector3(.67f, .90f, .48f), "Teal upholstery");
            Box("Cream apron", view.body, new Vector3(0, .99f, .23f), new Vector3(.49f, .66f, .035f), "Cream linen", true, false);
            Box("Apron pocket", view.body, new Vector3(0, .87f, .261f), new Vector3(.27f, .15f, .027f), "Ivory moulding", true, false);
            Sphere("Housekeeper face", view.body, new Vector3(0, 1.65f, .015f), new Vector3(.62f, .67f, .56f), "Repair copper");
            Sphere("Swept hair", view.body, new Vector3(0, 1.89f, -.06f), new Vector3(.64f, .33f, .56f), "Mahogany");
            Sphere("Hair bun", view.body, new Vector3(0, 1.96f, -.30f), Vector3.one * .28f, "Mahogany");
            Box("Housekeeping cap", view.body, new Vector3(0, 1.99f, .13f), new Vector3(.44f, .12f, .21f), "Cream linen", true, false);
            foreach (int sign in new[] { -1, 1 })
            {
                Sphere("Housekeeper eye", view.body, new Vector3(sign * .145f, 1.7f, .274f), new Vector3(.17f, .20f, .09f), "Gauge ivory");
                Sphere("Housekeeper pupil", view.body, new Vector3(sign * .145f, 1.7f, .322f), new Vector3(.061f, .082f, .026f), "Ink");
                var leg = Group("Housekeeper leg", view.body, new Vector3(sign * .17f, .67f, 0)).transform;
                Box("Uniform trouser", leg, new Vector3(0, -.23f, 0), new Vector3(.23f, .49f, .26f), "Teal upholstery", true, false);
                Sphere("Practical shoe", leg, new Vector3(0, -.55f, .09f), new Vector3(.28f, .19f, .42f), "Ink");
                var arm = Group("Housekeeper arm", view.body, new Vector3(sign * .37f, 1.28f, 0)).transform;
                Box("Uniform sleeve", arm, new Vector3(0, -.18f, 0), new Vector3(.22f, .39f, .22f), "Teal upholstery", true, false);
                Sphere("Housekeeper hand", arm, new Vector3(0, -.47f, .03f), new Vector3(.22f, .24f, .21f), "Repair copper");
                if (sign < 0) { view.leftLeg = leg; view.leftArm = arm; }
                else { view.rightLeg = leg; view.rightArm = arm; }
            }
            Sphere("Housekeeper nose", view.body, new Vector3(0, 1.62f, .316f), new Vector3(.14f, .15f, .14f), "Repair copper");
            Box("Housekeeper smile", view.body, new Vector3(0, 1.52f, .27f), new Vector3(.15f, .026f, .026f), "Mahogany", true, false);
            view.broom = Group("Cleaning brush", view.worker, new Vector3(.43f, .11f, .30f), new Vector3(0, 0, -8)).transform;
            Cylinder("Wooden brush handle", view.broom, new Vector3(0, .55f, 0), .027f, 1.05f, "Walnut panels");
            Box("Brush head", view.broom, Vector3.zero, new Vector3(.36f, .085f, .15f), "Mahogany", true, false);
            Box("Brush bristles", view.broom, new Vector3(0, -.053f, 0), new Vector3(.36f, .055f, .15f), "Luggage mustard", false, false);
            view.statusLabel = Text("Housekeeper task label", view.worker, "HOUSEKEEPING\nReady", new Vector3(0, 2.38f, 0), .075f, Lettering, new Vector3(0, 180, 0));
            // These parts move as one original character and must never enter the static batch.
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.isStatic = false;
        }
    }
}
