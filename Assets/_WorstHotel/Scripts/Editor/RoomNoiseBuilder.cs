using UnityEditor;
using UnityEngine;
using static WorstHotel.Editor.HotelKitAssets;

namespace WorstHotel.Editor
{
    public static partial class PrototypeSceneBuilder
    {
        /// <summary>Separate fixed knocker targets preserve the ordinary moving-door interaction.</summary>
        public static void AddRoomNoiseInteractions(GameObject gameplay)
        {
            for (int roomId = 101; roomId <= 106; roomId++)
            {
                var door = GameObject.Find("Door" + roomId);
                if (door == null) throw new System.InvalidOperationException("Missing authored room door " + roomId);
                var target = Group("Room knocker " + roomId, door.transform, new Vector3(1.62f, 1f, -.40f));
                var interaction = target.AddComponent<RoomNoiseInteraction>();
                interaction.roomId = roomId;
                interaction.displayName = "Room " + roomId + " knocker";
                var collider = target.AddComponent<BoxCollider>();
                // Include the visible label in the same target; aiming at KNOCK must not open the door.
                collider.center = new Vector3(0, -.07f, 0);
                collider.size = new Vector3(.42f, .56f, .18f);
                Box("Knocker brass mounting plate", target.transform, Vector3.zero,
                    new Vector3(.34f, .38f, .06f), "Aged brass", true, false);
                foreach (int sign in new[] { -1, 1 })
                    Sphere("Knocker fixing", target.transform, new Vector3(sign * .125f, .145f, -.035f),
                        Vector3.one * .027f, "Pipe iron");
                Cylinder("Knocker hinge", target.transform, new Vector3(0, .105f, -.057f),
                    .025f, .17f, "Aged brass", new Vector3(0, 0, 90));
                Sphere("Knocker strike plate", target.transform, new Vector3(0, -.102f, -.037f),
                    new Vector3(.095f, .075f, .035f), "Pipe iron");
                var pivot = Group("Knocker ring pivot " + roomId, target.transform, new Vector3(0, .10f, -.065f));
                interaction.knocker = pivot.transform;
                var ring = Group("Brass knocker ring", pivot.transform, new Vector3(0, -.10f, 0));
                ring.transform.localScale = Vector3.one * .25f;
                ring.AddComponent<MeshFilter>().sharedMesh = Wheel;
                ring.AddComponent<MeshRenderer>().sharedMaterial = Mat("Aged brass");
                Box("Knocker label face", target.transform, new Vector3(0, -.28f, -.018f),
                    new Vector3(.40f, .14f, .055f), "Mahogany", true, false);
                Text("Knocker label", target.transform, "KNOCK", new Vector3(0, -.28f, -.052f), .084f, Lettering);
                foreach (var child in target.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            }
        }
    }
}
