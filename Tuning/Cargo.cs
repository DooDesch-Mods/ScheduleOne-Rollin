using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Storage;
using Rollin.Chair;
using Rollin.Net;
using UnityEngine.Events;

namespace Rollin.Tuning
{
    /// <summary>
    /// The Moving Box as a boot. The hull is a Shitbox clone and keeps the car's trunk (a StorageEntity on its root),
    /// cut down to <see cref="Slots"/> slots and opened from a trigger around the box instead of the hidden trunk lid.
    /// The trunk syncs its slots by itself; the host writes the contents into the garage store and loads them back
    /// when the chair comes back as a vehicle. Without the box the items stay in the chair until a box is fitted again.
    /// </summary>
    internal static class Cargo
    {
        internal const int Slots = 4;
        internal const string BoxName = "Moving Box";
        internal const string TriggerName = "rollin_box_open";

        /// <summary>The trunk is opened only below this speed.</summary>
        private const float StandKmh = 1f;

        /// <summary>Contents to load into a chair once it is spawned here: net id, contents and the earliest time.</summary>
        private static readonly Dictionary<int, KeyValuePair<string, float>> _pending = new Dictionary<int, KeyValuePair<string, float>>();

        /// <summary>Seconds after the spawn before the contents go in, so the trunk's RPCs reach the clients.</summary>
        private const float LoadDelay = 1f;

        internal static void Reset() => _pending.Clear();

        /// <summary>
        /// Every peer, right after the hull exists: the trunk gets its slot count, its name and full access. The car's
        /// own access follows its owner flag and the clone starts closed.
        /// </summary>
        internal static void Setup(LandVehicle lv)
        {
            var st = lv != null ? lv.Storage : null;
            if (st == null) { Core.Log.Warning("[Cargo] net " + Ids.Of(lv) + " has no trunk - the Moving Box stores nothing."); return; }
            var slots = st.ItemSlots;
            int before = slots != null ? slots.Count : 0;
            if (slots != null) while (slots.Count > Slots) slots.RemoveAt(slots.Count - 1);
            st.SlotCount = Slots;
            st.StorageEntityName = BoxName;
            st.AccessSettings = StorageEntity.EAccessSettings.Full;
            st.MaxAccessDistance = 3f;
            Core.Log.Msg("[Cargo] net " + Ids.Of(lv) + ": trunk is the Moving Box, " + before + " -> " + (slots != null ? slots.Count : 0) + " slot(s), open to everyone.");
        }

        internal static bool HasItems(LandVehicle lv) => lv != null && lv.Storage != null && lv.Storage.ItemCount > 0;

        /// <summary>The trunk contents as the game's item set JSON, or null when it is empty.</summary>
        internal static string Serialize(LandVehicle lv)
        {
            if (!HasItems(lv)) return null;
            return new ItemSet(lv.Storage.ItemSlots).GetJSON();
        }

        /// <summary>Host: puts saved contents into a chair once it is spawned.</summary>
        internal static void LoadLater(LandVehicle lv, string json)
        {
            if (lv == null || string.IsNullOrEmpty(json)) return;
            _pending[Ids.Of(lv)] = new KeyValuePair<string, float>(json, Time.time + LoadDelay);
        }

        internal static void Tick()
        {
            if (_pending.Count == 0 || !RollinNet.IsHost) return;
            foreach (var id in new List<int>(_pending.Keys))
            {
                var entry = _pending[id];
                if (Time.time < entry.Value) continue;
                var lv = Ids.Find<LandVehicle>(id);
                if (lv == null) { _pending.Remove(id); Core.Log.Warning("[Cargo] net " + id + " is gone before its box contents came back - they stay in the store."); continue; }
                if (!lv.IsSpawned) continue;
                _pending.Remove(id);
                Load(lv, entry.Key);
            }
        }

        private static void Load(LandVehicle lv, string json)
        {
            if (lv.Storage == null) { Core.Log.Error("[Cargo] net " + Ids.Of(lv) + " has no trunk - its saved box contents are not loaded."); return; }
            if (!ItemSet.TryDeserialize(json, out DeserializedItemSet set) || set == null)
            {
                Core.Log.Error("[Cargo] the saved box contents of net " + Ids.Of(lv) + " do not read as an item set - not loaded.");
                return;
            }
            set.LoadTo(lv.Storage.ItemSlots);
            Core.Log.Msg("[Cargo] net " + Ids.Of(lv) + ": " + lv.Storage.ItemCount + " item(s) back in the Moving Box.");
        }

        // ---- the box as the trunk's handle ---------------------------------------------------------------------------

        /// <summary>
        /// Adds the "Open" trigger around a built box. It sits on the hull's interaction layer and under the seat, where
        /// the hull's own "get in" trigger ends (<see cref="WheelchairType"/>).
        /// </summary>
        internal static void AddTrigger(Transform root, Bounds box)
        {
            var go = new GameObject(TriggerName);
            go.transform.SetParent(root, false);
            go.layer = WheelchairType.InteractionLayer;
            var size = box.size + new Vector3(0.04f, 0.02f, 0.04f);
            // The trigger stays below the get-in trigger, so looking at the seat still gets the player in.
            float top = Mathf.Min(box.max.y + 0.01f, WheelchairType.EnterFloor - 0.005f);
            float bottom = box.min.y - 0.01f;
            size.y = Mathf.Max(0.05f, top - bottom);
            go.transform.localPosition = new Vector3(box.center.x, (top + bottom) * 0.5f, box.center.z);
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.size = size;
            var io = go.AddComponent<InteractableObject>();
            io.MaxInteractionRange = 2.5f;
            io.SetMessage("Open " + BoxName);
            io.SetInteractableState(InteractableObject.EInteractableState.Default);
            io.onHovered.AddListener((UnityAction)(() => Hovered(io)));
            io.onInteractStart.AddListener((UnityAction)(() => Open(io)));
        }

        /// <summary>
        /// Whether the local player's look ray hits the box trigger of a chair. The hull's "get in" trigger is wider than
        /// the box and catches a ray from beside the chair before it reaches the box below the seat.
        /// </summary>
        private static bool Aimed(LandVehicle lv) => AimedBox(lv) != null;

        /// <summary>The box trigger the local player looks at, or null.</summary>
        private static BoxCollider AimedBox(LandVehicle lv)
        {
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            if (cam == null || lv == null) return null;
            foreach (var col in lv.GetComponentsInChildren<BoxCollider>(false))
            {
                if (col.gameObject.name != TriggerName) continue;
                var eye = cam.transform;
                return col.Raycast(new Ray(eye.position, eye.forward), out _, 2.5f) ? col : null;
            }
            return null;
        }

        /// <summary>Where the hull's prompt stood before it moved to the box, per hull.</summary>
        private static readonly Dictionary<IntPtr, Collider> _labelHome = new Dictionary<IntPtr, Collider>();

        /// <summary>
        /// The hull's prompt says "Open Moving Box" while the player looks at the box through it, and stands at the box
        /// instead of the seat. Looking away puts it back on the seat.
        /// </summary>
        [HarmonyPatch(typeof(LandVehicle), "Hovered")]
        private static class HullHover
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(LandVehicle __instance)
            {
                var io = __instance.intObj;
                if (!WheelchairType.Is(__instance) || io == null) return;
                var box = Push.LocalPushing ? null : AimedBox(__instance);
                if (box == null)
                {
                    if (_labelHome.TryGetValue(io.Pointer, out var home)) { io.displayLocationCollider = home; _labelHome.Remove(io.Pointer); }
                    return;
                }
                if (!_labelHome.ContainsKey(io.Pointer)) _labelHome[io.Pointer] = io.displayLocationCollider;
                // The label goes to the point of the box nearest the camera (InteractableObject.ShowMessage).
                io.displayLocationCollider = box;
                Hovered(io);
            }
        }

        /// <summary>E on the hull opens the box instead of getting in while the player looks at the box.</summary>
        [HarmonyPatch(typeof(LandVehicle), "Interacted")]
        private static class HullInteract
        {
            private static bool Prefix(LandVehicle __instance)
            {
                if (!WheelchairType.Is(__instance) || __instance.intObj == null || Push.LocalPushing || !Aimed(__instance)) return true;
                Core.Log.Msg("[Cargo] E on net " + Ids.Of(__instance) + " aims at the Moving Box, opening it instead of getting in.");
                Open(__instance.intObj);
                return false;
            }
        }

        private static void Hovered(InteractableObject io)
        {
            string why = Blocked(io, out _);
            io.SetMessage(why ?? "Open " + BoxName);
            io.SetInteractableState(why == null ? InteractableObject.EInteractableState.Default : InteractableObject.EInteractableState.Invalid);
        }

        /// <summary>Why the box cannot be opened now, or null.</summary>
        private static string Blocked(InteractableObject io, out StorageEntity st)
        {
            st = null;
            var lv = io != null ? io.GetComponentInParent<LandVehicle>() : null;
            if (lv == null || lv.Storage == null) return "No storage";
            st = lv.Storage;
            if (Mathf.Abs(lv.Speed_Kmh) > StandKmh) return "Stop to open";
            if (!st.CanBeOpened()) return "In use";
            return null;
        }

        private static void Open(InteractableObject io)
        {
            string why = Blocked(io, out var st);
            if (why != null) { Core.Log.Msg("[Cargo] box not opened: " + why + "."); return; }
            var local = Player.Local;
            if (local == null || local.CurrentVehicle != null) { Core.Log.Msg("[Cargo] box not opened: the local player is in a vehicle or not there."); return; }
            st.Open();
            Core.Log.Msg("[Cargo] opened the Moving Box of net " + Ids.Of(io.GetComponentInParent<LandVehicle>()) + " (" + st.ItemCount + " item(s)).");
        }
    }
}
