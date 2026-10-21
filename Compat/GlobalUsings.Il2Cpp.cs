// IL2CPP global usings: the game namespaces and types the source names, so the rest uses unqualified names.
// Because UnityEngine is imported here and System implicitly, write `UnityEngine.Object` / `UnityEngine.Random`.

global using UnityEngine;
global using Il2CppScheduleOne.DevUtilities;    // NetworkSingleton<T>, Singleton<T>, PlayerSingleton<T>
global using Il2CppScheduleOne.PlayerScripts;   // Player (Player.Local), PlayerCamera, PlayerMovement
global using Il2CppScheduleOne.Vehicles;        // LandVehicle, VehicleSeat, VehicleManager, Wheel
global using Il2CppScheduleOne.Interaction;     // InteractableObject
global using Il2CppFishNet;                     // InstanceFinder

global using FishNetObject = Il2CppFishNet.Object.NetworkObject;
global using GameConsole = Il2CppScheduleOne.Console;
global using GameInput = Il2CppScheduleOne.GameInput;
global using GameQuestManager = Il2CppScheduleOne.Quests.QuestManager;
global using EQuestState = Il2CppScheduleOne.Quests.EQuestState;
global using GameStringList = Il2CppSystem.Collections.Generic.List<string>;
global using WeatherHandler = Il2CppScheduleOne.Core.Weather.EnvironmentHandler;
global using IWeatherEntity = Il2CppScheduleOne.Core.Weather.IWeatherEntity;
global using GameSaveManager = Il2CppScheduleOne.Persistence.SaveManager;
global using GameLoadManager = Il2CppScheduleOne.Persistence.LoadManager;
