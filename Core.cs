using MelonLoader;

[assembly: MelonInfo(typeof(Rollin.Core), "Rollin", DooDesch.ModVersion.Current, "DooDesch", "https://github.com/DooDesch-Mods/ScheduleOne-Rollin")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonAdditionalDependencies("Chassis")]

namespace Rollin
{
    /// <summary>
    /// MelonLoader entry point for Rollin: the placed wheelchairs become rideable, a second player can push, and the
    /// chair takes absurd tuning parts through the Chassis tuning API.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        private bool _inWorld;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            // No PatchAll here: MelonLoader applies the [HarmonyPatch] classes of a MelonMod itself, and a second
            // PatchAll runs every prefix and postfix twice per call.
            int patched = 0;
            foreach (var _ in HarmonyInstance.GetPatchedMethods()) patched++;
            Log.Msg("[Core] Harmony patches applied by MelonLoader on " + patched + " method(s).");
            Net.RollinNet.Apply(HarmonyInstance);
            Start("sync", Net.Sync.Init);
            Start("wheelchair profile", Chair.WheelchairProfile.Register);
            Start("garage", Tuning.Garage.Init);
            Start("tuning effects", Tuning.Effects.Init);
            Start("tuning menu", Tuning.Menu.Init);
            Start("stock trim", Tuning.StockTrim.Init);

#if DEBUG
            Log.Msg("Rollin (DEBUG) loaded. Type 'rollin help' in the console.");
#else
            Log.Msg("Rollin loaded.");
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _inWorld = sceneName == "Main";
            if (!_inWorld) ResetWorld();
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            _inWorld = false;
            ResetWorld();
        }

        private static void ResetWorld()
        {
            Net.Sync.Reset();
            Net.Ids.Reset();
            Chair.Push.Reset();
            Chair.PropSwap.Reset();
            Chair.WheelchairModel.Reset();
            Tuning.Garage.Reset();
            Tuning.Effects.Reset();
            Tuning.GameMeshes.Reset();
            Tuning.Menu.Reset();
            Tuning.Cargo.Reset();
            Tuning.StockTrim.Reset();
        }

        public override void OnLateUpdate()
        {
            if (!_inWorld) return;
            Guard("push placement", Chair.Push.LateTick);
            Guard("tuning effects", Tuning.Effects.LateTick);
        }

        public override void OnUpdate()
        {
            if (!_inWorld) return;
            Guard("sync", Net.Sync.Tick);
            Guard("prop swap", Chair.PropSwap.Tick);
            Guard("push", Chair.Push.Tick);
            Guard("garage", Tuning.Garage.Tick);
            Guard("cargo", Tuning.Cargo.Tick);
            Guard("tuning menu", Tuning.Menu.Tick);
#if DEBUG
            Debugging.ConsoleRegistration.EnsureRegistered();
            Debugging.DebugConsolePatch.FlushToConsole();
#endif
        }

        /// <summary>One subsystem that fails to start logs its error; the others still start.</summary>
        private static void Start(string what, System.Action a)
        {
            try { a(); }
            catch (System.Exception e) { Log.Error("[Core] " + what + " did not start and stays off: " + e); }
        }

        /// <summary>One failing subsystem logs its error and the others still run this frame.</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> _lastError = new System.Collections.Generic.Dictionary<string, float>();
        private static readonly System.Collections.Generic.Dictionary<string, int> _muted = new System.Collections.Generic.Dictionary<string, int>();

        private static void Guard(string what, System.Action a)
        {
            try { a(); }
            catch (System.Exception e)
            {
                // At most one log per subsystem per 5 s, so a broken frame loop does not flood the log.
                if (_lastError.TryGetValue(what, out float t) && UnityEngine.Time.time - t < 5f)
                {
                    _muted.TryGetValue(what, out int n); _muted[what] = n + 1;
                    return;
                }
                _lastError[what] = UnityEngine.Time.time;
                _muted.TryGetValue(what, out int muted); _muted[what] = 0;
                Log.Error("[Core] " + what + " failed this frame" + (muted > 0 ? " (and " + muted + " more time(s) in the last 5 s)" : "") + ": " + e);
            }
        }
    }
}
