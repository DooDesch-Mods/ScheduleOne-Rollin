namespace Rollin.Net
{
    /// <summary>FishNet object ids: the name a vehicle or a player has on every peer.</summary>
    internal static class Ids
    {
        internal static int Of(Component c)
        {
            if (c == null) return -1;
            var nob = c.GetComponent<FishNetObject>();
            return nob != null ? nob.ObjectId : -1;
        }

        /// <summary>The player with this object id, from the game's player list.</summary>
        internal static Player PlayerById(int objectId)
        {
            if (objectId < 0) return null;
            var list = Player.PlayerList;
            if (list == null) { Core.Log.Warning("[Net] the player list does not exist yet - player net " + objectId + " not found."); return null; }
            for (int i = 0; i < list.Count; i++)
            {
                var p = list[i];
                if (p != null && Of(p) == objectId) return p;
            }
            return null;
        }

        /// <summary>Network objects found before, by id. A hit is checked again on every use.</summary>
        private static readonly System.Collections.Generic.Dictionary<int, FishNetObject> _known = new System.Collections.Generic.Dictionary<int, FishNetObject>();

        /// <summary>Ids that were not found, and when the next full search for them may run.</summary>
        private static readonly System.Collections.Generic.Dictionary<int, float> _missUntil = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>
        /// How long a missing id waits before the next full search. The host checks every swapped chair each frame,
        /// and a full search walks every network object of the world: tens of milliseconds in a big save.
        /// </summary>
        private const float MissRetry = 0.25f;

        /// <summary>
        /// A component on the network object with this id. A known object answers at once while it is still active
        /// with the same id; anything else walks every network object, at most every <see cref="MissRetry"/> seconds
        /// per id.
        /// </summary>
        internal static T Find<T>(int objectId) where T : Component
        {
            if (objectId < 0) return null;
            if (_known.TryGetValue(objectId, out var hit))
            {
                if (hit != null && hit.ObjectId == objectId && hit.gameObject.activeInHierarchy)
                {
                    var c = hit.GetComponent<T>();
                    if (c != null) return c;
                }
                _known.Remove(objectId);
            }
            if (_missUntil.TryGetValue(objectId, out var until) && Time.unscaledTime < until) return null;

            foreach (var nob in UnityEngine.Object.FindObjectsOfType<FishNetObject>())
            {
                if (nob != null && nob.ObjectId == objectId)
                {
                    var t = nob.GetComponent<T>();
                    if (t != null)
                    {
                        _known[objectId] = nob;
                        _missUntil.Remove(objectId);
                        return t;
                    }
                }
            }
            _missUntil[objectId] = Time.unscaledTime + MissRetry;
            return null;
        }

        /// <summary>Forgets every object of the world that was left.</summary>
        internal static void Reset()
        {
            _known.Clear();
            _missUntil.Clear();
        }
    }
}
