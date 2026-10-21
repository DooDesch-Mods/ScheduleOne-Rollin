using Chassis.Api;

namespace Rollin.Chair
{
    /// <summary>
    /// The wheelchair's Chassis profile, in the space of the placed wheelchair prop: the rider sits with the hands on the
    /// push rims and the feet on the footrests, a pusher stands behind with the hands on the handles.
    /// </summary>
    internal static class WheelchairProfile
    {
        internal static void Register()
        {
            bool used = ChassisApi.Register(Build());
            Core.Log.Msg("[Profile] '" + WheelchairType.Code + "' " + (used ? "registered with Chassis." : "registered, but a JSON file in UserData/Chassis overrides it."));
        }

        internal static ChassisProfile Build() => new ChassisProfile
        {
            Code = WheelchairType.Code,
            Frame = WheelchairType.FrameName,
            Seats =
            {
                new SeatProfile
                {
                    Index = 0,
                    // Chassis seats a body upright (not crouched); the root sits low enough that the hips rest on the cushion.
                    Root = new Vector3(0f, -0.3f, -0.1f),
                    Eyes = new Vector3(0f, 1.34f, 0.1f),
                    Pose = "Sitting",
                    Hands = new HandProfile
                    {
                        Mode = "rim",
                        LeftWheel = WheelchairType.RimLeft,
                        RightWheel = WheelchairType.RimRight,
                        GripStart = new Vector3(0.04f, 0.26f, 0.15f),
                        GripEnd = new Vector3(0.04f, 0.15f, 0.27f),
                        Recovery = 0.35f,
                        Elbow = new Vector3(0.55f, 0.85f, -0.4f),
                        // The spine target sits at chest height, a little ahead of the backrest: at rest the rider sits with a
                        // slight forward lean, at the end of a push stroke a bit more. Without it (weight 0) the body sits
                        // bolt upright; a target lower down (y 1.15) folds the back over the knees.
                        LeanStart = new Vector3(0f, 1.35f, 0.05f), LeanStartWeight = 0.3f,
                        LeanEnd = new Vector3(0f, 1.3f, 0.12f), LeanEndWeight = 0.4f,
                    },
                    Feet = new FootProfile
                    {
                        Position = new Vector3(0.165f, 0.17f, 0.46f),
                        Rotation = new Vector3(-92f, 180f, 0f),
                        RotationWeight = 1f,
                        Knee = new Vector3(0.14f, 0.75f, 0.9f),
                    },
                },
            },
            Push = new PushProfile
            {
                Stand = new Vector3(0f, 0f, -0.95f),
                Handle = new Vector3(0.2f, 0.95f, -0.45f),
                Elbow = new Vector3(0.45f, 0.9f, -1.2f),
                Reach = 1.2f,
            },
        };
    }
}
