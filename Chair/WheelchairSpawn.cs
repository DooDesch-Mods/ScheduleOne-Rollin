using System;
using HarmonyLib;
using Rollin.Net;

namespace Rollin.Chair
{
    /// <summary>
    /// Host side: puts a wheelchair vehicle into the world and takes it out again. Spawned through FishNet directly,
    /// not through VehicleManager, so it never enters the save.
    /// </summary>
    internal static class WheelchairSpawn
    {
        /// <summary>Raised on every peer once a wheelchair's look and handles are built.</summary>
        internal static event Action<LandVehicle> Started;

        internal static LandVehicle Spawn(Vector3 position, Quaternion rotation, string why)
        {
            if (!InstanceFinder.IsServer) { Core.Log.Warning("[Wheelchair] spawn asked on a client (" + why + ") - only the host spawns."); return null; }
            var prefab = WheelchairType.Prefab;
            if (prefab == null) { Core.Log.Error("[Wheelchair] spawn failed (" + why + "): the hull was never built."); return null; }

            var go = UnityEngine.Object.Instantiate(prefab.gameObject, position, rotation);
            go.name = "RollinWheelchair";
            go.SetActive(true);
            InstanceFinder.ServerManager.Spawn(go, null);
            var lv = go.GetComponent<LandVehicle>();
            Core.Log.Msg("[Wheelchair] spawned at " + position.ToString("F2") + " yaw " + rotation.eulerAngles.y.ToString("F0") + " as net " + Ids.Of(lv) + " (" + why + ").");
            return lv;
        }

        internal static void Despawn(LandVehicle lv, string why)
        {
            if (lv == null) return;
            if (!InstanceFinder.IsServer) { Core.Log.Warning("[Wheelchair] despawn asked on a client (" + why + ")."); return; }
            int id = Ids.Of(lv);
            Compat.Interop.Despawn(lv.gameObject);
            Core.Log.Msg("[Wheelchair] despawned net " + id + " (" + why + ").");
        }

        /// <summary>Every peer builds the look of its own copy when the vehicle starts.</summary>
        [HarmonyPatch(typeof(LandVehicle), "Start")]
        private static class BuildLookOnStart
        {
            private static void Postfix(LandVehicle __instance)
            {
                if (!WheelchairType.Is(__instance)) return;
                try { WheelchairModel.Build(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] building the look failed: " + e); }
                try { Push.AddHandles(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] adding the push handles failed: " + e); }
                // Start registers every vehicle for weather updates (wet tyre grip). The environment walks a snapshot of
                // that list over half a second, so a wheelchair despawned in that window throws a NullReferenceException
                // there. The wheelchair keeps its own grip and leaves the list right away.
                try
                {
                    WeatherHandler.UnregisterWeatherEntity(__instance.Cast<IWeatherEntity>());
                    Core.Log.Msg("[Wheelchair] net " + Ids.Of(__instance) + " left the weather updates.");
                }
                catch (Exception e) { Core.Log.Error("[Wheelchair] leaving the weather updates failed: " + e); }
                if (Started == null) return;
                try { Started(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] a start handler failed: " + e); }
            }
        }
    }
}
