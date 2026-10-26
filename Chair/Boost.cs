using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using HarmonyLib;
using MelonLoader;
using Rollin.Net;

namespace Rollin.Chair
{
    /// <summary>
    /// The chair's three speeds, run on the driver's peer, which simulates the vehicle:
    ///   normal - always, a factor on the chair's speed (<c>SpeedNormal</c>);
    ///   sprint - the game's sprint key, paid with stamina like the skateboard push (<c>SpeedSprint</c>);
    ///   nitro  - the nitro key while the Jerry Can Rocket is fitted, from a tank that refills by itself (<c>SpeedNitro</c>).
    ///
    /// A factor stretches the motor curve over speed and scales its pull by the same amount, so the chair reaches
    /// factor x its top speed with factor x its pull. The curve itself is never written (Chassis owns it for the engine
    /// parts); the drive wheels' torque is recomputed right after vanilla's <c>ApplyThrottle</c> set it.
    /// </summary>
    internal static class Boost
    {
        internal const string BoosterPart = "rollin_jerrycan";

        // The skateboard push (Skateboard.cs): one push at most each second, only with 12.5 stamina left; the push
        // starts 0.35 s after the key and drains its 12.5 over 0.4 s.
        private const float PushCooldown = 1f;
        private const float PushStamina = 12.5f;
        private const float PushDelay = 0.35f;
        private const float PushDuration = 0.4f;

        /// <summary>How fast the factor moves to a new speed, per second; vanilla's sprint multiplier uses 4.</summary>
        private const float FactorRate = 4f;

        /// <summary>Seconds after the nitro key is let go before the tank starts to refill.</summary>
        private const float RechargeDelay = 2f;

        private static MelonPreferences_Entry<float> _normal, _sprint, _nitro, _nitroSeconds, _rechargeSeconds;
        private static MelonPreferences_Entry<KeyCode> _nitroKey;

        // Driver-peer state of the chair the local player drives.
        private static int _chair = -1;
        private static float _factor = 1f;
        private static float _sinceLastPush = PushCooldown;
        private static float _pushDrainLeft;
        private static bool _sprinting, _nitroOn;
        private static float _sinceNitro = RechargeDelay;
        private static int _shownTank = -1;

        /// <summary>Nitro tank per chair net id, 0..1; local to this peer.</summary>
        private static readonly Dictionary<int, float> _tank = new Dictionary<int, float>();

        /// <summary>Console stand-ins for held keys, until this time.</summary>
        internal static float ForceSprintUntil = -1f, ForceNitroUntil = -1f;

        internal static float NormalFactor => Clamp(_normal?.Value ?? 1.2f);
        internal static float SprintFactor => Clamp(_sprint?.Value ?? 1.7f);
        internal static float NitroFactor => Clamp(_nitro?.Value ?? 2.2f);
        internal static KeyCode NitroKey => _nitroKey?.Value ?? KeyCode.LeftControl;
        private static float NitroSeconds => Mathf.Max(0.5f, _nitroSeconds?.Value ?? 10f);
        private static float RechargeSeconds => Mathf.Max(0.5f, _rechargeSeconds?.Value ?? 30f);

        private static float Clamp(float f) => Mathf.Clamp(f, 0.1f, 10f);

        internal static void Init()
        {
            var cat = MelonPreferences.CreateCategory("Rollin", "Rollin");
            _normal = cat.CreateEntry("SpeedNormal", 1.2f, "Normal speed", "Speed of the chair without sprint or nitro. 1 is the speed of Rollin 1.0.");
            _sprint = cat.CreateEntry("SpeedSprint", 1.7f, "Sprint speed", "Speed while you hold the sprint key. It costs stamina like the skateboard push.");
            _nitro = cat.CreateEntry("SpeedNitro", 2.2f, "Nitro speed", "Speed while you hold the nitro key. Needs the Jerry Can Rocket booster.");
            _nitroKey = cat.CreateEntry("NitroKey", KeyCode.LeftControl, "Nitro key", "Hold it while you drive a wheelchair with the Jerry Can Rocket.");
            _nitroSeconds = cat.CreateEntry("NitroSeconds", 10f, "Nitro duration", "Seconds a full nitro tank lasts.");
            _rechargeSeconds = cat.CreateEntry("NitroRechargeSeconds", 30f, "Nitro recharge", "Seconds an empty nitro tank needs to fill up again.");
            Core.Log.Msg("[Boost] speeds normal x" + F(NormalFactor) + ", sprint x" + F(SprintFactor) + ", nitro x" + F(NitroFactor)
                + "; nitro key " + NitroKey + ", " + F(NitroSeconds) + " s per tank, " + F(RechargeSeconds) + " s to refill.");
            ChassisApi.AddKeyHint("rollin.nitro", "Nitro", c => c.Driver && WheelchairType.Is(c.Vehicle) && HasBooster(c.Vehicle), () => NitroKey);
        }

        internal static void Reset()
        {
            if (_nitroOn && _chair >= 0) SendNitro(_chair, false);
            _chair = -1;
            _factor = 1f;
            _sprinting = _nitroOn = false;
            _sinceLastPush = PushCooldown;
            _pushDrainLeft = 0f;
            _sinceNitro = RechargeDelay;
            _shownTank = -1;
            _tank.Clear();
            ForceSprintUntil = ForceNitroUntil = -1f;
        }

        internal static bool HasBooster(LandVehicle lv) =>
            lv != null && string.Equals(ChassisApi.InstalledPart(lv, "booster"), BoosterPart, StringComparison.OrdinalIgnoreCase);

        /// <summary>The speed factor for this chair on this peer; 1 where this peer does not drive it.</summary>
        private static float FactorFor(LandVehicle lv) => _chair >= 0 && Ids.Of(lv) == _chair ? _factor : 1f;

        internal static void Tick()
        {
            var lv = Tuning.Menu.LocalChair();
            int id = lv != null ? Ids.Of(lv) : -1;
            if (id != _chair)
            {
                if (_nitroOn && _chair >= 0) SendNitro(_chair, false);
                _nitroOn = _sprinting = false;
                _pushDrainLeft = 0f;
                _factor = lv != null ? NormalFactor : 1f;
                if (lv != null) Core.Log.Msg("[Boost] driving net " + id + ": normal x" + F(NormalFactor) + ", sprint x" + F(SprintFactor)
                    + ", nitro x" + F(NitroFactor) + (HasBooster(lv) ? ", tank " + Pct(Tank(id)) : ", no booster - no nitro") + ".");
                else if (_chair >= 0) Core.Log.Msg("[Boost] no longer driving net " + _chair + ".");
                _chair = id;
                _shownTank = -1;
            }
            if (lv == null) return;

            float dt = Time.deltaTime;
            bool input = !GameInput.IsTyping && !Tuning.Menu.IsOpen;
            bool forward = input && GameInput.VehicleDriveAxis > 0.01f;

            Sprint(lv, dt, input, forward);
            Nitro(lv, id, dt, input, forward);

            float target = _nitroOn ? NitroFactor : _sprinting ? Mathf.Max(SprintFactor, NormalFactor) : NormalFactor;
            _factor = Mathf.MoveTowards(_factor, target, dt * FactorRate);
        }

        /// <summary>
        /// The skateboard push on the chair: holding sprint while driving forward starts a push each second as long as
        /// 12.5 stamina is left; each push drains its 12.5 over 0.4 s. The chair sprints while pushes keep coming.
        /// </summary>
        private static void Sprint(LandVehicle lv, float dt, bool input, bool forward)
        {
            var move = PlayerSingleton<PlayerMovement>.Instance;
            if (move == null) { if (_sprinting) Core.Log.Warning("[Boost] no PlayerMovement - the sprint stops."); _sprinting = false; return; }

            _sinceLastPush += dt;
            if (_pushDrainLeft > 0f && _sinceLastPush >= PushDelay)
            {
                float take = Mathf.Min(_pushDrainLeft, PushStamina * dt / PushDuration);
                move.ChangeStamina(-take);
                _pushDrainLeft -= take;
            }

            bool held = (input && GameInput.GetButton(GameInput.ButtonCode.Sprint)) || Time.time < ForceSprintUntil;
            bool want = held && (forward || Time.time < ForceSprintUntil) && !Player.Local.IsTased;
            if (want && _sinceLastPush >= PushCooldown && _pushDrainLeft <= 0f)
            {
                if (move.CurrentStaminaReserve >= PushStamina)
                {
                    _sinceLastPush = 0f;
                    _pushDrainLeft = PushStamina;
                }
                else if (_sprinting) Core.Log.Msg("[Boost] out of stamina (" + F(move.CurrentStaminaReserve) + ") - the sprint ends.");
            }
            // A push carries the sprint until the next one is due.
            bool now = want && _sinceLastPush < PushCooldown + 0.05f;
            if (now != _sprinting)
            {
                _sprinting = now;
                Core.Log.Msg("[Boost] net " + _chair + " sprint " + (now ? "on" : "off") + ", stamina " + F(move.CurrentStaminaReserve) + ".");
            }
        }

        /// <summary>NFS style: the chair boosts while the key is held and the tank has some left; it refills by itself.</summary>
        private static void Nitro(LandVehicle lv, int id, float dt, bool input, bool forward)
        {
            float tank = Tank(id);
            bool booster = HasBooster(lv);
            bool held = (input && Input.GetKey(NitroKey)) || Time.time < ForceNitroUntil;
            bool want = booster && held && (forward || Time.time < ForceNitroUntil) && tank > 0f;

            if (want)
            {
                tank = Mathf.Max(0f, tank - dt / NitroSeconds);
                _sinceNitro = 0f;
            }
            else
            {
                _sinceNitro += dt;
                if (_sinceNitro >= RechargeDelay && tank < 1f) tank = Mathf.Min(1f, tank + dt / RechargeSeconds);
            }
            _tank[id] = tank;

            if (want != _nitroOn)
            {
                _nitroOn = want;
                SendNitro(id, want);
                Core.Log.Msg("[Boost] net " + id + " nitro " + (want ? "on" : "off") + ", tank " + Pct(tank) + (!want && tank <= 0f ? " (empty)" : "") + ".");
            }

            // The key hint shows the tank in steps of 10 %.
            int step = booster ? Mathf.RoundToInt(tank * 10f) : -1;
            if (step != _shownTank)
            {
                _shownTank = step;
                if (booster) ChassisApi.AddKeyHint("rollin.nitro", "Nitro " + (step * 10) + "%", c => c.Driver && WheelchairType.Is(c.Vehicle) && HasBooster(c.Vehicle), () => NitroKey);
            }
        }

        private static float Tank(int id) => _tank.TryGetValue(id, out float t) ? t : 1f;

        private static void SendNitro(int id, bool on) =>
            RollinNet.ToHost("req.nitro", id.ToString(CultureInfo.InvariantCulture), on ? "1" : "0");

        /// <summary>Host: a driver switched nitro; every peer shows the flames.</summary>
        internal static void HostNitro(int vehicle, bool on)
        {
            var lv = Ids.Find<LandVehicle>(vehicle);
            if (lv == null || !WheelchairType.Is(lv)) { Core.Log.Warning("[Boost] nitro for net " + vehicle + ", which is no wheelchair here - dropped."); return; }
            RollinNet.ToAll("nitro", vehicle.ToString(CultureInfo.InvariantCulture), on ? "1" : "0");
        }

        internal static string Describe()
        {
            var move = PlayerSingleton<PlayerMovement>.Instance;
            string state = _chair < 0 ? "not driving a wheelchair" : "net " + _chair + " x" + F(_factor) + (_sprinting ? ", sprinting" : "") + (_nitroOn ? ", nitro" : "")
                + ", tank " + Pct(Tank(_chair)) + ", stamina " + (move != null ? F(move.CurrentStaminaReserve) : "?");
            return "boost: " + state + "; speeds x" + F(NormalFactor) + "/" + F(SprintFactor) + "/" + F(NitroFactor) + ", nitro key " + NitroKey;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        private static string Pct(float t) => Mathf.RoundToInt(t * 100f) + "%";

        /// <summary>
        /// Recomputes the drive torque vanilla just set, with the curve stretched by the factor: pull = throttle x factor
        /// x curve(speed / factor). Sprint and nitro push only forward; reversing keeps the normal factor.
        /// </summary>
        [HarmonyPatch(typeof(LandVehicle), "ApplyThrottle")]
        private static class ThrottlePatch
        {
            private static bool _failed;

            private static void Postfix(LandVehicle __instance)
            {
                if (_failed || !__instance.LocalPlayerIsDriver || !WheelchairType.Is(__instance)) return;
                try
                {
                    float f = FactorFor(__instance);
                    float thr = __instance.currentThrottle;
                    if (thr == 0f || __instance.HandbrakeApplied) return;
                    float speed = __instance.Speed_Kmh;
                    // Vanilla only drives in this case; otherwise it brakes, and the brake stays its own.
                    if (!(Mathf.Abs(speed) < 4f || Mathf.Sign(speed) == Mathf.Sign(thr))) return;
                    if (thr < 0f) f = Mathf.Min(f, NormalFactor);
                    if (Mathf.Abs(f - 1f) < 0.001f) return;
                    float at = Mathf.Abs(speed) / f;
                    if (speed < -0.1f && thr < 0f) at /= __instance.reverseMultiplier;
                    float torque = thr * f * __instance.motorTorque.Evaluate(at) * __instance.diffGearing / 2f;
                    foreach (var wc in __instance.driveWheels) if (wc != null) wc.motorTorque = torque;
                }
                catch (Exception e)
                {
                    _failed = true;
                    Core.Log.Error("[Boost] scaling the drive torque failed - the chair keeps its stock speed from now on: " + e);
                }
            }
        }
    }
}
