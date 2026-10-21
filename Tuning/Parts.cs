using System.Collections.Generic;
using Chassis.Api;
using Rollin.Chair;

namespace Rollin.Tuning
{
    /// <summary>
    /// Rollin's tuning parts for the wheelchair: performance stages far past anything sensible, horns, and the visual
    /// parts from <see cref="Models"/>. Every peer registers the same parts; the tuning state carries only their ids.
    /// </summary>
    internal static class Parts
    {
        internal static readonly List<TuningPart> All = new List<TuningPart>();

        internal static void RegisterAll()
        {
            // The chair pulls 100 at standstill and stops pulling at 11.5 km/h; gearing stretches that over speed.
            Stage("rollin_engine_1", "engine", "Caffeine Drip", 500, new TuningStats { Torque = 1.6f, Gearing = 1.4f, EnginePitch = 1.1f });
            Stage("rollin_engine_2", "engine", "Lawnmower Swap", 1500, new TuningStats { Torque = 2.5f, Gearing = 2.2f, EnginePitch = 1.25f });
            Stage("rollin_engine_3", "engine", "Jet Turbine", 4000, new TuningStats { Torque = 4f, Gearing = 4f, EnginePitch = 1.5f });
            Stage("rollin_engine_4", "engine", "Illegal Rocket", 9999, new TuningStats { Torque = 7f, Gearing = 7f, EnginePitch = 1.9f });
            Stage("rollin_brakes_1", "brakes", "Heel Drag Pads", 300, new TuningStats { Brake = 2f });
            Stage("rollin_brakes_2", "brakes", "Anchor Brakes", 1200, new TuningStats { Brake = 5f });
            Stage("rollin_suspension_1", "suspension", "Slammed", 800, new TuningStats { RideHeight = -0.03f, Spring = 1.3f });
            Stage("rollin_suspension_2", "suspension", "Monster Lift", 1500, new TuningStats { RideHeight = 0.25f, Spring = 0.7f, Damper = 0.7f });
            Stage("rollin_tyres_drift", "tyres", "Buttered Tyres", 600, new TuningStats { Grip = 0.45f });
            Stage("rollin_tyres_grip", "tyres", "Velcro Tyres", 900, new TuningStats { Grip = 1.6f });
            Stage("rollin_steer_1", "steering", "Shopping Cart Steering", 400, new TuningStats { Steer = 1.6f });
            Stage("rollin_mass_1", "weight", "Carbon Fibre Cushion", 2000, new TuningStats { Mass = 0.6f });
            Stage("rollin_mass_2", "weight", "Lead Underpants", 200, new TuningStats { Mass = 2.5f });

            foreach (var p in Models.Parts()) Register(p);
            Horns.RegisterAll();
            Sounds.RegisterHorns();
            Core.Log.Msg("[Parts] " + All.Count + " wheelchair tuning part(s) registered with Chassis.");
        }

        private static void Stage(string id, string slot, string label, int price, TuningStats stats)
        {
            var part = new TuningPart { Id = id, Slot = slot, Label = label, Price = price, Stats = stats };
            // A part without a model fits every car unless it names its codes; these are for the wheelchair only.
            part.Fits[WheelchairType.Code] = new PartFit();
            Register(part);
        }

        internal static void Register(TuningPart part)
        {
            ChassisApi.RegisterTuningPart(part);
            All.Add(part);
        }
    }
}
