using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace Rollin.Compat
{
    /// <summary>The IL2CPP interop calls the rest of the source needs: casts, identity, game arrays and FishNet's despawn.</summary>
    internal static class Interop
    {
        /// <summary>Runtime cast of a Unity object; null when it is not a <typeparamref name="T"/>.</summary>
        internal static T As<T>(this UnityEngine.Object o) where T : UnityEngine.Object => o == null ? null : o.TryCast<T>();

        /// <summary>True when both wrappers stand for the same game object (IL2CPP wraps one object many times).</summary>
        internal static bool Same(UnityEngine.Object a, UnityEngine.Object b) => a != null && b != null && a.Pointer == b.Pointer;

        /// <summary>Name of the object's game-side type (the IL2CPP class, not the interop wrapper).</summary>
        internal static string TypeName(UnityEngine.Object o) => o == null ? "null" : o.GetIl2CppType().Name;

        /// <summary>A game-side array field value from managed elements.</summary>
        internal static Il2CppReferenceArray<T> Arr<T>(params T[] items) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
            => new Il2CppReferenceArray<T>(items);

        /// <summary>Server despawn without a despawn type, as FishNet's optional parameter.</summary>
        internal static void Despawn(GameObject go)
            => InstanceFinder.ServerManager.Despawn(go, new Il2CppSystem.Nullable<Il2CppFishNet.Object.DespawnType>());
    }
}
