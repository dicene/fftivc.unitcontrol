using fftivc.unitcontrol.Configuration;
using fftivc.unitcontrol.Template;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Hooks.ReloadedII.Interfaces;
using Reloaded.Memory;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using Reloaded.Mod.Interfaces;

#if DEBUG
using System.Diagnostics;
using System.Drawing;
#endif

namespace fftivc.unitcontrol
{
    /// <summary>
    /// Your mod logic goes here.
    /// </summary>
    public class Mod : ModBase // <= Do not Remove.
    {
        /// <summary>
        /// Provides access to the mod loader API.
        /// </summary>
        private readonly IModLoader _modLoader;

        /// <summary>
        /// Provides access to the Reloaded.Hooks API.
        /// </summary>
        /// <remarks>This is null if you remove dependency on Reloaded.SharedLib.Hooks in your mod.</remarks>
        private readonly Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks? _hooks;

        /// <summary>
        /// Provides access to the Reloaded logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Entry point into the mod, instance that created this class.
        /// </summary>
        private readonly IMod _owner;

        /// <summary>
        /// Provides access to this mod's configuration.
        /// </summary>
        private Config _configuration;

        /// <summary>
        /// The configuration of the currently executing mod.
        /// </summary>
        private readonly IModConfig _modConfig;

        UIntPtr BattleUnitsBaseAddress;

        [Function(CallingConventions.Microsoft)]
        private delegate Int64 TransitionIntoBattle(Int64 a1, Int64 a2, Int64 a3, Int64 a4);
        private IHook<TransitionIntoBattle> TransitionIntoBattle_Hook;
        UIntPtr TransitionIntoBattleAddress;

        private Int64 TransitionIntoBattle_Replacement(Int64 a1, Int64 a2, Int64 a3, Int64 a4)
        {
            var ret = TransitionIntoBattle_Hook.OriginalFunction(a1, a2, a3, a4);

            UpdateUnitControl();

            return ret;
        }

        private IImGui _imGui;
        private IImGuiShell _imGuiShell;

        private UnitControlSettingsMenu settingsMenu;

        public Mod(ModContext context)
        {
            _modLoader = context.ModLoader;
            _hooks = context.Hooks;
            _logger = context.Logger;
            _owner = context.Owner;
            _configuration = context.Configuration;
            _modConfig = context.ModConfig;

#if DEBUG
            // Attaches debugger in debug mode; ignored in release.
            Debugger.Launch();
#endif

            _logger.WriteLine($"[{_modConfig.ModId}] Loading UnitControl...");

            var startupScannerController = _modLoader.GetController<IStartupScanner>();
            if (startupScannerController == null || !startupScannerController.TryGetTarget(out var startupScanner))
            {
                _logger.WriteLineAsync($"[{_modConfig.ModId}] Unable to find startupScanner. Ensure Reloaded.Memory.SigScan is installed.", Color.OrangeRed);

                return;
            }

            Action<Reloaded.Memory.Sigscan.Definitions.Structs.PatternScanResult> findBattleUnitsBase = result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for BattleUnitsBase!", Color.OrangeRed);
                    return;
                }

                var battleUnitsBase_address = Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset;

                _logger.WriteLineAsync($"[{_modConfig.ModId}] BattleUnitsBase AOB found at 0x{battleUnitsBase_address:X}.", Color.LightGreen);

                var lea_address = (nuint)(battleUnitsBase_address + 0x2a);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] lea_address at 0x{lea_address:X}.", Color.LightGreen);
                Memory.Instance.Read<int>(lea_address + 3, out int offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at 0x{offsetAddress:X}.", Color.LightGreen);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at {offsetAddress}.", Color.LightGreen);
                BattleUnitsBaseAddress = (nuint)((nint)lea_address + 7 + offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] BattleUnitsBase Address found at 0x{BattleUnitsBaseAddress:X}.", Color.LightGreen);
            };
            startupScanner.AddMainModuleScan("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 31 E0 48 89 44 24 ?? 48 63 F1 4C 8D 3D", findBattleUnitsBase);

            Action<Reloaded.Memory.Sigscan.Definitions.Structs.PatternScanResult> TransitionIntoBattleAction = result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for TransitionIntoBattle!", Color.OrangeRed);
                    return;
                }

                var TransitionIntoBattle_address = Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset;

                _logger.WriteLineAsync($"[{_modConfig.ModId}] TransitionIntoBattle AOB found at 0x{TransitionIntoBattle_address:X}.", Color.LightGreen);

                var call_address = (nuint)(TransitionIntoBattle_address);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] call at 0x{call_address:X}.", Color.LightGreen);
                Memory.Instance.Read<int>(call_address + 1, out int offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at 0x{offsetAddress:X}.", Color.LightGreen);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at {offsetAddress}.", Color.LightGreen);
                TransitionIntoBattleAddress = (nuint)((nint)call_address + 5 + offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] TransitionIntoBattle Address found at 0x{TransitionIntoBattleAddress:X}.", Color.LightGreen);

                TransitionIntoBattle_Hook = _hooks!.CreateHook<TransitionIntoBattle>(TransitionIntoBattle_Replacement, (long)TransitionIntoBattleAddress).Activate();

                if (!TransitionIntoBattle_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook TransitionIntoBattle function at 0x{TransitionIntoBattleAddress:X}.", Color.OrangeRed);
                    return;
                }

                _logger.WriteLineAsync($"[{_modConfig.ModId}] Hooked TransitionIntoBattle function at 0x{TransitionIntoBattleAddress:X}.", Color.LightGreen);
            };
            startupScanner.AddMainModuleScan("E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? E8 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 74", TransitionIntoBattleAction);

            var imGuiController = _modLoader.GetController<IImGui>();

            if (imGuiController?.TryGetTarget(out IImGui imGui) != true)
            {
                _logger.WriteLineAsync($"[{_modConfig.ModId}] ImGui not found.");
                return;
            }

            _imGui = imGui;

            var imGuiShellController = _modLoader.GetController<IImGuiShell>();

            if (imGuiShellController?.TryGetTarget(out IImGuiShell imGuiShell) != true)
            {
                _logger.WriteLineAsync($"[{_modConfig.ModId}] ImGuiShell not found.");
                return;
            }

            _imGuiShell = imGuiShell;

            _logger.WriteLineAsync($"[{_modConfig.ModId}] {imGui}.");
            _logger.WriteLineAsync($"[{_modConfig.ModId}] {imGuiShell}.");

            settingsMenu = new UnitControlSettingsMenu(this, _configuration);
            settingsMenu.imGui = imGui;
            imGuiShell.AddComponent(settingsMenu);

            _logger.WriteLine($"[{_modConfig.ModId}] UnitControl loaded...");
        }

        public void UpdateUnitControl()
        {
            if (BattleUnitsBaseAddress > 0)
            {
                for (int i = 0; i < 23; i++)
                {
                    var pBattleUnit = BattleUnitsBaseAddress + (nuint)(0x200 * i);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x0, out var unitSpriteSet);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x1, out var unitIndex);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x3, out var unitJob);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x5, out var battleUnitFlags2);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x6, out var battleUnitFlags1);
                    Memory.Instance.Read<byte>(pBattleUnit + 0x1EE, out var battleUnitFlags2Copy);

                    if (unitIndex == 0xFF)
                    {
                        continue;
                    }

                    if ((battleUnitFlags1 & 0x9) != 0)
                    {
                        if (_configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Guest        SpriteSet:0x{unitSpriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{unitJob:X2}, Flags1:0x{battleUnitFlags1:X2}, Flags2:0x{battleUnitFlags2:X2}, Flags2Copy:0x{battleUnitFlags2Copy:X2}", Color.Goldenrod);
                        if (_configuration.ControlGuests && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 0)
                        {
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Giving control of guest 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 | 0x8));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy | 0x8));
                        }
                        else if (!_configuration.ControlGuests && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 8)
                        {
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Removing control of guest 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 & 0xF7));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy & 0xF7));
                        }
                    }
                    else if (((battleUnitFlags2 | battleUnitFlags2Copy) & 0x30) != 0)
                    {
                        if (_configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Enemy        SpriteSet:0x{unitSpriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{unitJob:X2}, Flags1:0x{battleUnitFlags1:X2}, Flags2:0x{battleUnitFlags2:X2}, Flags2Copy:0x{battleUnitFlags2Copy:X2}", Color.Salmon);
                        if (_configuration.ControlEnemies && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 0)
                        {
                            // Marked as Team 1 or Team 2
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Giving control of enemy 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 | 0x8));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy | 0x8));
                        }
                        else if (!_configuration.ControlEnemies && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 8)
                        {
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Removing control of enemy 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 & 0xF7));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy & 0xF7));
                        }
                    }
                    else
                    {
                        if (_configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Player       SpriteSet:0x{unitSpriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{unitJob:X2}, Flags1:0x{battleUnitFlags1:X2}, Flags2:0x{battleUnitFlags2:X2}, Flags2Copy:0x{battleUnitFlags2Copy:X2}", Color.Green);
                        if (_configuration.ControlPlayerUnits && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 0)
                        {
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Giving control of player unit 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 | 0x8));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy | 0x8));
                        }
                        else if (!_configuration.ControlPlayerUnits && ((battleUnitFlags2 | battleUnitFlags2Copy) & 0x8) == 8)
                        {
                            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  Removing control of player unit 0x{unitIndex:X2}...");
                            Memory.Instance.Write<byte>(pBattleUnit + 0x5, (byte)(battleUnitFlags2 & 0xF7));
                            Memory.Instance.Write<byte>(pBattleUnit + 0x1EE, (byte)(battleUnitFlags2Copy & 0xF7));
                        }
                    }
                }
            }
        }

        #region Standard Overrides
        public override void ConfigurationUpdated(Config configuration)
        {
            // Apply settings from configuration.
            // ... your code here.
            _configuration = configuration;
            if (_configuration.LoggingEnabled) _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: Applying");

            UpdateUnitControl();
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}