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

        /// <summary>A component on the network object with this id. Walks every network object: not for a per-frame path.</summary>
        internal static T Find<T>(int objectId) where T : Component
        {
            if (objectId < 0) return null;
            foreach (var nob in UnityEngine.Object.FindObjectsOfType<FishNetObject>())
            {
                if (nob != null && nob.ObjectId == objectId)
                {
                    var t = nob.GetComponent<T>();
                    if (t != null) return t;
                }
            }
            return null;
        }
    }
}
