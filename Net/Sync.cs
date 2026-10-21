using Rollin.Chair;

namespace Rollin.Net
{
    /// <summary>
    /// Routes Rollin's messages:
    ///   req.sit prop player              -> swap prop vehicle player     (host spawns the chair, everyone hides the prop)
    ///                                       unswap prop x y z yaw        (host: the chair emptied, the prop comes back)
    ///   req.push vehicle player 1|0      -> push vehicle player|-1
    ///   req.pushin vehicle fwd steer     -> pushin vehicle fwd steer
    ///   req.tune vehicle key value       -> (host applies it through the Chassis tuning API)
    ///   req.sync player                  -> swap ... / push ...          (host: the current state for a late joiner)
    /// </summary>
    internal static class Sync
    {
        private static bool _synced;

        internal static void Init()
        {
            RollinNet.OnRequest = OnRequest;
            RollinNet.OnState = OnState;
        }

        internal static void Reset() => _synced = false;

        /// <summary>A client asks once per world for the chairs and pushers that exist already.</summary>
        internal static void Tick()
        {
            if (_synced || Player.Local == null) return;
            _synced = true;
            if (RollinNet.IsHost) return;
            RollinNet.ToHost("req.sync", Ids.Of(Player.Local).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void OnRequest(RollinNet.Msg m)
        {
            switch (m.Op)
            {
                case "req.sit": PropSwap.HostSit(m.Arg(0), m.Int(1)); break;
                case "req.push": Push.HostRequest(m.Int(0), m.Int(1), m.Arg(2) == "1"); break;
                case "req.pushin": Push.HostInput(m.Int(0), m.Int(1), m.Int(2)); break;
                case "req.tune": Tuning.Garage.HostRequest(m.Int(0), m.Arg(1), m.Arg(2)); break;
                case "req.sync":
                    Core.Log.Msg("[Net] player " + m.Arg(0) + " asked for the current wheelchairs.");
                    PropSwap.HostReplay();
                    Push.HostReplay();
                    break;
                default: Core.Log.Warning("[Net] unknown request " + m + " - ignored."); break;
            }
        }

        private static void OnState(RollinNet.Msg m)
        {
            switch (m.Op)
            {
                case "swap": PropSwap.OnSwap(m.Arg(0), m.Int(1), m.Int(2)); break;
                case "unswap":
                    PropSwap.Parse(m.Args, out Vector3 pos, out float yaw);
                    PropSwap.OnUnswap(m.Arg(0), pos, yaw);
                    break;
                case "push": Push.OnPush(m.Int(0), m.Int(1)); break;
                case "pushin": Push.OnInput(m.Int(0), new Vector2(m.Int(1), m.Int(2))); break;
                default: Core.Log.Warning("[Net] unknown state " + m + " - ignored."); break;
            }
        }
    }
}
