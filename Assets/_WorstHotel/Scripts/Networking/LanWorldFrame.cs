using System;
using UnityEngine;

namespace WorstHotel
{
    [Serializable]
    public sealed class LanWorldFrame
    {
        public long epoch, sequence;
        public LanWorldPlayer[] players = Array.Empty<LanWorldPlayer>();
        public LanWorldObject[] objects = Array.Empty<LanWorldObject>();
        public LanWorldVisual[] visuals = Array.Empty<LanWorldVisual>();
        public LanWorldText[] texts = Array.Empty<LanWorldText>();
        public LanWorldLight[] lights = Array.Empty<LanWorldLight>();
        public LanWorldGuest[] guests = Array.Empty<LanWorldGuest>();
    }
    [Serializable]
    public sealed class LanWorldPlayer
    {
        public int actorId;
        public Vector3 position;
        public Quaternion rotation, cameraLocalRotation;
        public Vector2 movement;
        public bool usingHands, usable, pickup, uiBlocked;
        public string caption, detailedCaption, heldId, subtitle;
    }
    [Serializable]
    public sealed class LanWorldObject
    {
        public string id;
        public Vector3 position;
        public Quaternion rotation;
        public bool active;
        public bool isDoor, doorOpen;
    }
    [Serializable]
    public sealed class LanWorldVisual
    {
        public string id;
        public bool active, enabled, hasColor, hasEmission;
        public Color color, emission;
    }
    [Serializable]
    public sealed class LanWorldText
    {
        public string id, text;
        public Color color;
    }
    [Serializable]
    public sealed class LanWorldLight
    {
        public string id;
        public bool enabled;
        public float intensity;
        public Color color;
    }
    [Serializable]
    public sealed class LanWorldGuest
    {
        public string id, name;
        public GuestKind kind;
        public int appearanceIndex;
        public bool bodyVisible = true;
        public bool phoneVisible;
        public Vector3 position, bodyOffset;
        public Quaternion rotation, bodyRotation, leftArm, rightArm, leftLeg, rightLeg;
    }
    public sealed class LanGuestView
    {
        public Transform root, body, leftArm, rightArm, leftLeg, rightLeg, phone;
        public void Apply(LanWorldGuest pose)
        {
            root.SetPositionAndRotation(pose.position, pose.rotation);
            body.gameObject.SetActive(pose.bodyVisible);
            if (phone) phone.gameObject.SetActive(pose.phoneVisible);
            body.localPosition = pose.bodyOffset; body.localRotation = pose.bodyRotation;
            leftArm.localRotation = pose.leftArm; rightArm.localRotation = pose.rightArm;
            leftLeg.localRotation = pose.leftLeg; rightLeg.localRotation = pose.rightLeg;
        }
    }
}
