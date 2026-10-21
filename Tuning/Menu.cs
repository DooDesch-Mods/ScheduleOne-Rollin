using System;
using System.Collections.Generic;
using System.Text;
using Chassis.Api;
using Il2CppTMPro;
using Rollin.Chair;
using UnityEngine.UI;

namespace Rollin.Tuning
{
    /// <summary>
    /// The tuning menu: the garage key (R, <c>GarageKey</c> in MelonPreferences) while driving a wheelchair. W/S picks a
    /// row, A/D changes it, the garage key closes it. Every change goes
    /// through <see cref="Garage.Request"/>, so it reaches the host and every peer sees it. Tuning is free. While the
    /// menu is open the chair ignores the driving keys, which the menu borrows.
    /// </summary>
    internal static class Menu
    {
        private static MelonLoader.MelonPreferences_Entry<KeyCode> _key;

        /// <summary>Opens and closes the menu. Not T: the game opens the phone with it.</summary>
        internal static KeyCode Key => _key != null ? _key.Value : KeyCode.R;

        private sealed class Row
        {
            internal string Label;
            internal string Key;
            internal readonly List<KeyValuePair<string, string>> Options = new List<KeyValuePair<string, string>>();
        }

        private static readonly string[] PartSlots = { "spoiler", "booster", "exhaust", "rims", "hat", "mascot", "roof", "lights", "audio", "cargo" };
        private static readonly string[] StageSlots = { "engine", "brakes", "suspension", "tyres", "steering", "weight" };

        private static readonly (string Label, string Value)[] Paints =
        {
            ("Stock", ""),
            ("Hot Pink", "FF3EA5,metallic"),
            ("Toxic Lime", "7CFF2E,classic"),
            ("Gold", "FFC423,chrome"),
            ("Chrome", "D9DDE3,chrome"),
            ("Matte Black", "151515,matte"),
            ("Candy Red", "C8102E,metallic"),
            ("Baby Blue", "8FD3FF,classic"),
            ("Royal Purple", "7A2BD9,metallic"),
            ("Hospital Green", "8FBFA5,matte"),
        };

        private static readonly (string Label, string Value)[] Neons =
        {
            ("Off", ""),
            ("Pink", "FF2EC4,fblr"),
            ("Cyan", "2EF2FF,fblr"),
            ("Green", "39FF14,fblr"),
            ("Red", "FF1A1A,fblr"),
            ("Purple", "9B30FF,fblr"),
            ("White", "FFFFFF,fblr"),
        };

        private static List<Row> _rows;
        private static LandVehicle _lv;
        private static int _row;
        private static GameObject _canvas;
        private static TextMeshProUGUI _body;
        private static readonly Dictionary<string, (string Value, float Until)> _pending = new Dictionary<string, (string, float)>(StringComparer.OrdinalIgnoreCase);

        internal static bool IsOpen => _lv != null;

        internal static void Init()
        {
            var cat = MelonLoader.MelonPreferences.CreateCategory("Rollin", "Rollin");
            _key = cat.CreateEntry("GarageKey", KeyCode.R, "Garage key", "Opens and closes the tuning menu while you drive a wheelchair.");
            Core.Log.Msg("[Menu] garage key: " + Key + ".");
            ChassisApi.AddKeyHint("rollin.garage", "Tuning", c => c.Driver && WheelchairType.Is(c.Vehicle), () => Key);
        }

        internal static void Reset()
        {
            if (_lv != null) Lock(_lv, false, "reset");
            _lv = null;
            _orbit = false;
            if (_canvas != null) _canvas.SetActive(false);
        }

        internal static void Tick()
        {
            if (_lv == null)
            {
                if (!Input.GetKeyDown(Key) || GameInput.IsTyping) return;
                var lv = LocalChair();
                if (lv == null) return;
                Open(lv, Key + " pressed");
                return;
            }

            if (!_lv || !_lv.LocalPlayerIsDriver) { Close("left the wheelchair"); return; }
            if (GameInput.IsTyping) return;
            if (Input.GetKeyDown(Key)) { Close(Key + " pressed"); return; }
            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) Move(-1);
            if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Move(1);
            if (Input.GetKeyDown(KeyCode.A)) Cycle(-1);
            if (Input.GetKeyDown(KeyCode.D)) Cycle(1);
            Draw();
            HoldOrbit();
        }

        // ---- camera ------------------------------------------------------------------------------------------------

        /// <summary>True while the menu holds the game's right-click down for the vehicle camera.</summary>
        private static bool _orbit;
        private static bool _orbitWarned;

        /// <summary>
        /// With mouse and keyboard the vehicle camera orbits only while the right button is held. The menu holds that
        /// button in the game's input state, so the mouse turns the camera around the chair. A real release of the right
        /// button drops it from the list; the next frame puts it back.
        /// </summary>
        private static void HoldOrbit()
        {
            if (!Singleton<GameInput>.InstanceExists)
            {
                if (!_orbitWarned) { _orbitWarned = true; Core.Log.Warning("[Menu] no GameInput - the camera orbits only with the right button held."); }
                return;
            }
            var gi = Singleton<GameInput>.Instance;
            var code = GameInput.ButtonCode.SecondaryClick;
            if (gi.buttonsDown.Contains(code)) return;
            gi.buttonsDown.Add(code);
            // The first frame of a press makes the camera start the orbit from where it looks now.
            if (!gi.buttonsDownThisFrame.Contains(code)) gi.buttonsDownThisFrame.Add(code);
            if (!_orbit) Core.Log.Msg("[Menu] the camera orbits with the mouse while the menu is open.");
            _orbit = true;
        }

        private static void ReleaseOrbit(string why)
        {
            if (!_orbit) return;
            _orbit = false;
            if (!Singleton<GameInput>.InstanceExists) { Core.Log.Warning("[Menu] GameInput is gone - nothing to release (" + why + ")."); return; }
            if (Input.GetMouseButton(1)) { Core.Log.Msg("[Menu] right button is really held - the camera keeps orbiting (" + why + ")."); return; }
            var gi = Singleton<GameInput>.Instance;
            gi.buttonsDown.Remove(GameInput.ButtonCode.SecondaryClick);
            gi.buttonsUpThisFrame.Add(GameInput.ButtonCode.SecondaryClick);
            Core.Log.Msg("[Menu] camera back to right-click orbit (" + why + ").");
        }

        /// <summary>The wheelchair the local player drives, or null.</summary>
        internal static LandVehicle LocalChair()
        {
            var p = Player.Local;
            var nob = p != null ? p.CurrentVehicle : null;
            var lv = nob != null ? nob.GetComponent<LandVehicle>() : null;
            return lv != null && WheelchairType.Is(lv) && lv.LocalPlayerIsDriver ? lv : null;
        }

        internal static bool Open(LandVehicle lv, string why)
        {
            if (lv == null) { Core.Log.Warning("[Menu] open (" + why + "): you are not driving a wheelchair."); return false; }
            BuildRows();
            if (!EnsureCanvas()) return false;
            _lv = lv;
            Lock(lv, true, why);
            _row = Mathf.Clamp(_row, 0, _rows.Count - 1);
            _canvas.SetActive(true);
            Draw();
            Core.Log.Msg("[Menu] opened for net " + Net.Ids.Of(lv) + " (" + why + "), " + _rows.Count + " rows.");
            return true;
        }

        internal static void Close(string why)
        {
            var lv = _lv;
            _lv = null;
            if (lv != null) Lock(lv, false, why);
            ReleaseOrbit(why);
            if (_canvas != null) _canvas.SetActive(false);
            Core.Log.Msg("[Menu] closed (" + why + ").");
        }

        /// <summary>Moves the selection; for the console.</summary>
        internal static void Move(int by)
        {
            if (_rows == null || _rows.Count == 0) return;
            _row = (_row + by + _rows.Count) % _rows.Count;
        }

        /// <summary>Changes the selected row to its previous or next option.</summary>
        internal static void Cycle(int by)
        {
            if (_lv == null || _rows == null) return;
            var row = _rows[_row];
            int i = IndexOf(row, Current(row.Key));
            int next = ((i < 0 ? 0 : i) + by + row.Options.Count) % row.Options.Count;
            var opt = row.Options[next];
            _pending[row.Key] = (opt.Value, Time.time + 2f);
            if (!Garage.Request(_lv, row.Key, opt.Value, "menu"))
                Core.Log.Warning("[Menu] " + row.Label + " -> " + opt.Key + " was refused (reason above).");
            else Core.Log.Msg("[Menu] " + row.Label + " -> " + opt.Key + " (" + row.Key + " = '" + opt.Value + "').");
        }

        private static string Current(string key)
        {
            if (_pending.TryGetValue(key, out var p) && Time.time < p.Until) return p.Value;
            return ChassisApi.GetTuning(_lv, key) ?? "";
        }

        private static int IndexOf(Row row, string value)
        {
            for (int i = 0; i < row.Options.Count; i++)
                if (string.Equals(row.Options[i].Value, value ?? "", StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        private static void BuildRows()
        {
            if (_rows != null) return;
            _rows = new List<Row>();
            var paint = new Row { Label = "Paint", Key = "paint" };
            var paint2 = new Row { Label = "Trim Paint", Key = "paint2" };
            foreach (var p in Paints) { paint.Options.Add(new KeyValuePair<string, string>(p.Label, p.Value)); paint2.Options.Add(new KeyValuePair<string, string>(p.Label, p.Value)); }
            _rows.Add(paint);
            _rows.Add(paint2);
            var neon = new Row { Label = "Neon", Key = "neon" };
            foreach (var n in Neons) neon.Options.Add(new KeyValuePair<string, string>(n.Label, n.Value));
            _rows.Add(neon);
            var horn = new Row { Label = "Horn", Key = "horn" };
            horn.Options.Add(new KeyValuePair<string, string>("Stock", ""));
            foreach (var h in Horns.All) horn.Options.Add(new KeyValuePair<string, string>(h.Label, h.Name));
            horn.Options.Add(new KeyValuePair<string, string>("Bicycle Horn", Sounds.BicycleHorn));
            _rows.Add(horn);
            foreach (string slot in PartSlots) AddSlot(slot);
            foreach (string slot in StageSlots) AddSlot(slot);
        }

        private static void AddSlot(string slot)
        {
            var row = new Row { Label = Title(slot), Key = "part." + slot };
            row.Options.Add(new KeyValuePair<string, string>("Stock", ""));
            foreach (var p in Parts.All) if (string.Equals(p.Slot, slot, StringComparison.OrdinalIgnoreCase)) row.Options.Add(new KeyValuePair<string, string>(p.Label, p.Id));
            if (row.Options.Count > 1) _rows.Add(row);
            else Core.Log.Warning("[Menu] slot '" + slot + "' has no parts - no row for it.");
        }

        private static string Title(string slot) => slot.Length == 0 ? slot : char.ToUpperInvariant(slot[0]) + slot.Substring(1);

        // ---- drawing -----------------------------------------------------------------------------------------------

        private static void Draw()
        {
            if (_body == null || _lv == null) return;
            var sb = new StringBuilder();
            sb.Append("<size=130%><b>ROLLIN GARAGE</b></size>\n<color=#9AA3AD><size=80%>W/S row   A/D change   " + Key + " close   Free of charge</size></color>\n\n");
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                string value = Current(row.Key);
                int idx = IndexOf(row, value);
                string label = idx >= 0 ? row.Options[idx].Key : value;
                bool sel = i == _row;
                if (i == 4 || i == 4 + PartSlotsShown()) sb.Append("<size=40%> </size>\n");
                sb.Append(sel ? "<color=#FFD24A>> " : "<color=#D8DEE6>  ").Append(row.Label).Append("</color>");
                sb.Append("<pos=46%>").Append(sel ? "<color=#FFD24A>< " : "<color=#8D96A0>").Append(label).Append(sel ? " ></color>" : "</color>").Append('\n');
            }
            var stats = ChassisApi.TuningStatsOf(_lv);
            if (stats != null) sb.Append("\n<color=#9AA3AD><size=75%>").Append(stats.ToString()).Append("</size></color>");
            _body.text = sb.ToString();
        }

        private static int PartSlotsShown()
        {
            int n = 0;
            foreach (var r in _rows) foreach (var s in PartSlots) if (r.Key == "part." + s) n++;
            return n;
        }

        private static bool EnsureCanvas()
        {
            if (_canvas != null) return true;
            try
            {
                _canvas = new GameObject("RollinTuningMenu");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                var canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 400;
                var scaler = _canvas.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;

                var panel = new GameObject("Panel");
                panel.transform.SetParent(_canvas.transform, false);
                var pr = panel.AddComponent<RectTransform>();
                // Right side: Chassis' key hints sit bottom left, the speedometer bottom right below the panel.
                pr.anchorMin = pr.anchorMax = new Vector2(1f, 0.5f);
                pr.pivot = new Vector2(1f, 0.5f);
                pr.anchoredPosition = new Vector2(-48f, 50f);
                pr.sizeDelta = new Vector2(560f, 820f);
                var bg = panel.AddComponent<Image>();
                bg.color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
                bg.raycastTarget = false;

                var stripe = new GameObject("Accent");
                stripe.transform.SetParent(panel.transform, false);
                var sr = stripe.AddComponent<RectTransform>();
                sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(0f, 1f);
                sr.pivot = new Vector2(0f, 0.5f);
                sr.sizeDelta = new Vector2(6f, 0f);
                var si = stripe.AddComponent<Image>();
                si.color = new Color(1f, 0.82f, 0.29f, 1f);
                si.raycastTarget = false;

                var text = new GameObject("Body");
                text.transform.SetParent(panel.transform, false);
                var tr = text.AddComponent<RectTransform>();
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(30f, 22f); tr.offsetMax = new Vector2(-22f, -22f);
                _body = text.AddComponent<TextMeshProUGUI>();
                var font = Font();
                if (font != null) _body.font = font;
                else Core.Log.Warning("[Menu] no game font found - the menu uses TextMeshPro's default.");
                _body.fontSize = 21f;
                _body.richText = true;
                _body.enableWordWrapping = false;
                _body.alignment = TextAlignmentOptions.TopLeft;
                _body.color = Color.white;
                _body.raycastTarget = false;
                _canvas.SetActive(false);
                Core.Log.Msg("[Menu] built the menu canvas, font " + (font != null ? font.name : "default") + ".");
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Menu] building the menu failed - tuning stays on the console: " + e);
                if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
                _canvas = null;
                _body = null;
                return false;
            }
        }

        private static TMP_FontAsset Font()
        {
            var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            foreach (string want in new[] { "OpenSans-SemiBold SDF", "OpenSans-Regular SDF", "LiberationSans SDF" })
                foreach (var f in all) if (f != null && f.name == want) return f;
            return all.Length > 0 ? all[0] : null;
        }

        // ---- the menu borrows the driving keys ---------------------------------------------------------------------

        /// <summary>
        /// W, A, S and D move through the menu, so the chair stands still while it is open: the game's own control
        /// override (LandVehicle.Update) drives it with no throttle and straight wheels.
        /// </summary>
        private static void Lock(LandVehicle lv, bool on, string why)
        {
            lv.overrideControls = on;
            lv.throttleOverride = 0f;
            lv.steerOverride = 0f;
            Core.Log.Msg("[Menu] net " + Net.Ids.Of(lv) + (on ? " stands still while the menu is open" : " drives again") + " (" + why + ").");
        }
    }
}
