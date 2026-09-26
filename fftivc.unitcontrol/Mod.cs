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
using System.Diagnostics;
using System.Drawing;

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

        /// <summary>
        /// Offset from the start of the <c>BattleUnitsBase</c> AOB match to the
        /// <c>lea r15, [rip+disp32]</c> instruction that computes the battle unit array address.
        /// </summary>
        private const int BattleUnitsPatternLeaOffset = 0x2A;

        // The hooked function is really `char f(void)` - it never reads any of these four argument
        // registers (it overwrites rcx/rdx/r8/r9 before use) and only defines the low byte of RAX.
        // Declaring it as a pass-through Int64 deliberately preserves the full RAX as the caller
        // would have seen it; narrowing this to `byte` would let the trampoline alter the upper
        // bits. See sub_140216C04 in the 1.5.2 IDB.
        [Function(CallingConventions.Microsoft)]
        private delegate Int64 TransitionIntoBattle(Int64 a1, Int64 a2, Int64 a3, Int64 a4);
        private IHook<TransitionIntoBattle> TransitionIntoBattle_Hook;
        UIntPtr TransitionIntoBattleAddress;

        private Int64 TransitionIntoBattle_Replacement(Int64 a1, Int64 a2, Int64 a3, Int64 a4)
        {
            var ret = TransitionIntoBattle_Hook.OriginalFunction(a1, a2, a3, a4);

            // Battle setup - runs once per battle, so dump the full roster here.
            UpdateUnitControl(verbose: true);

            return ret;
        }

        /// <summary>
        /// AOB for <c>NewEntry</c>. Matches exactly once in both supported builds:
        /// <c>0x1403041A4</c> in 1.5.2 and <c>0x14FA2BE60</c> in 1.2.0.
        /// </summary>
        private const string NewEntryPattern = "48 63 05 ?? ?? ?? ?? 4C 8D 15 ?? ?? ?? ?? 48 C1 E0 ?? 42 88 4C 10";

        // Real prototype (1.5.2):
        //   __int64 NewEntry(int x, int y, __int16 facingHi, __int16 facingLo, __int16 job,
        //                    __int16 a6, __int16 a7, BWORK *pBattleUnit, int flag);
        // Declared as pass-through Int64s so the trampoline cannot alter the argument slots.
        [Function(CallingConventions.Microsoft)]
        private delegate Int64 NewEntry(Int64 x, Int64 y, Int64 facingHi, Int64 facingLo, Int64 job, Int64 a6, Int64 a7, Int64 pBattleUnit, Int64 flag);
        private IHook<NewEntry> NewEntry_Hook;

        /// <summary>Period of the periodic re-apply backstop, in milliseconds.</summary>
        private const int ReapplyIntervalMs = 1000;

        private System.Threading.Timer? _reapplyTimer;

        /// <summary>
        /// Fires for every unit that appears on the field - battle start and mid-battle alike - and
        /// re-applies unit control so newly added units are covered.
        /// <para>
        /// <c>NewEntry</c> appends the unit to the 16-entry <c>gEntryArray</c> presentation table and
        /// is the one funnel shared by every placement path: <c>set_playerwork_common</c>,
        /// <c>set_playerwork_single</c> and <c>set_monsterwork</c> at battle start,
        /// <c>PlaceUnitAtActivePanel</c> mid-battle, and - the case the other hooks missed - the
        /// battle event script (<c>event_maincommon_0</c>) via <c>requestNewAnimation</c> /
        /// <c>requestEntryAnimation</c>.
        /// </para>
        /// <para>
        /// The unit pointer arrives in <c>pBattleUnit</c>, so a per-unit application is possible if
        /// the whole-array sweep ever becomes too chatty; for now the sweep keeps one code path.
        /// </para>
        /// </summary>
        private Int64 NewEntry_Replacement(Int64 x, Int64 y, Int64 facingHi, Int64 facingLo, Int64 job, Int64 a6, Int64 a7, Int64 pBattleUnit, Int64 flag)
        {
            var ret = NewEntry_Hook.OriginalFunction(x, y, facingHi, facingLo, job, a6, a7, pBattleUnit, flag);

            // Fires once per unit placed, so stay quiet - only actual control changes get logged.
            UpdateUnitControl(verbose: false);

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

                var lea_address = (nuint)(battleUnitsBase_address + BattleUnitsPatternLeaOffset);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] lea_address at 0x{lea_address:X}.", Color.LightGreen);
                Memory.Instance.Read<int>(lea_address + 3, out int offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at 0x{offsetAddress:X}.", Color.LightGreen);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] offsetAddress at {offsetAddress}.", Color.LightGreen);
                BattleUnitsBaseAddress = (nuint)((nint)lea_address + 7 + offsetAddress);
                _logger.WriteLineAsync($"[{_modConfig.ModId}] BattleUnitsBase Address found at 0x{BattleUnitsBaseAddress:X}.", Color.LightGreen);
            };
            // The instruction after the /GS cookie load is `xor rax, rsp`. MSVC encodes that as
            // 48 31 E0 in FFT_enhanced 1.2.0 but as 48 33 C4 in 1.5.2, so the encoding is masked
            // out. Both variants resolve to BattleUnits__bwork, and this pattern matches exactly
            // once in both versions.
            startupScanner.AddMainModuleScan("48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 56 41 57 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 ?? ?? 48 89 44 24 ?? 48 63 F1 4C 8D 3D", findBattleUnitsBase);

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

            // Unit appearance hook: covers reinforcements and event-script unit additions, which the
            // battle transition hook above does not see.
            Action<Reloaded.Memory.Sigscan.Definitions.Structs.PatternScanResult> NewEntryAction = result =>
            {
                if (!result.Found)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to find AoB pattern for NewEntry!", Color.OrangeRed);
                    return;
                }

                var newEntry_address = Process.GetCurrentProcess().MainModule.BaseAddress + result.Offset;

                _logger.WriteLineAsync($"[{_modConfig.ModId}] NewEntry AOB found at 0x{newEntry_address:X}.", Color.LightGreen);

                NewEntry_Hook = _hooks!.CreateHook<NewEntry>(NewEntry_Replacement, newEntry_address.ToInt64()).Activate();

                if (!NewEntry_Hook.IsHookEnabled)
                {
                    _logger.WriteLineAsync($"[{_modConfig.ModId}] Failed to hook NewEntry function at 0x{newEntry_address:X}.", Color.OrangeRed);
                    return;
                }

                _logger.WriteLineAsync($"[{_modConfig.ModId}] Hooked NewEntry function at 0x{newEntry_address:X}.", Color.LightGreen);
            };
            startupScanner.AddMainModuleScan(NewEntryPattern, NewEntryAction);

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

            settingsMenu = new UnitControlSettingsMenu(this);
            settingsMenu.imGui = imGui;
            imGuiShell.AddComponent(settingsMenu);

            // Backstop so a unit added through a placement path we have not hooked still gets covered.
            // Deliberately not driven from the ImGui component's Render(): that only ticks while the
            // overlay is actually visible (IsOverlay is false here), so closing the overlay would
            // silently disable it. See OnReapplyTick for why running it off-thread is cheap.
            _reapplyTimer = new System.Threading.Timer(_ => OnReapplyTick(), null, ReapplyIntervalMs, ReapplyIntervalMs);

            _logger.WriteLine($"[{_modConfig.ModId}] UnitControl loaded...");
        }

        /// <summary>
        /// The configuration instance currently in use.
        /// <para>
        /// The template swaps this out whenever the config file changes on disk, so always read it
        /// through this property instead of caching it - a cached copy becomes stale (and detached
        /// from its file watcher) as soon as anything writes to Config.json.
        /// </para>
        /// </summary>
        public Config Configuration => _configuration;

        /// <summary>
        /// Persists the current configuration to disk and re-applies it to the battle in progress.
        /// </summary>
        public void ApplyConfiguration()
        {
            _configuration.Save?.Invoke();

            // User-initiated, so show the full roster alongside any changes.
            UpdateUnitControl(verbose: true);
        }

        /// <summary>
        /// Restores every setting to its default value, saves the result and re-applies it.
        /// </summary>
        public void ResetConfigurationToDefaults()
        {
            _configuration.ResetToDefaults();
            ApplyConfiguration();
        }

        /// <summary>
        /// Periodic backstop that re-applies the configuration without depending on any hook firing.
        /// <para>
        /// The hooks only cover the unit placement paths that have been identified, so a unit added
        /// through a path we have not found - a scripted reinforcement, for example - would otherwise
        /// keep whatever control state the game gave it. <see cref="SetUnitControlled"/> returns early
        /// for units already in the requested state, so once everything has converged this performs
        /// zero writes and only the 21 x 6 flag reads.
        /// </para>
        /// </summary>
        private void OnReapplyTick()
        {
            try
            {
                UpdateUnitControl(verbose: false);
            }
            catch (Exception ex)
            {
                // A background tick must never take the game down with it.
                if (_configuration.LoggingEnabled) _logger.WriteLine($"[{_modConfig.ModId}] Reapply tick failed: {ex.Message}", Color.OrangeRed);
            }
        }

        /// <summary>
        /// Brings every battle unit in line with the configuration.
        /// </summary>
        /// <param name="verbose">
        /// When <c>true</c>, logs every live unit and its flags before applying anything. That listing
        /// is diagnostic output, so it is reserved for the rare entry points (battle transition and
        /// config change); the per-placement hook and the periodic backstop stay quiet and only log
        /// the control changes they actually make.
        /// </param>
        public void UpdateUnitControl(bool verbose = false)
        {
            if (BattleUnitsBaseAddress == 0)
            {
                return;
            }

            var configuration = _configuration;

            for (int i = 0; i < BattleUnits.Capacity; i++)
            {
                var pBattleUnit = BattleUnitsBaseAddress + (nuint)(BattleUnits.Stride * i);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetSpriteSet, out var spriteSet);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetIndex, out var unitIndex);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetJob, out var job);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetFlags2, out var flags2);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetFlags1, out var flags1);
                Memory.Instance.Read<byte>(pBattleUnit + BattleUnits.OffsetFlags2Mirror, out var flags2Mirror);

                if (unitIndex == BattleUnits.EmptyIndex)
                {
                    continue;
                }

                // Flags2 and its mirror at +0x1EE are two copies of the same byte for our purposes,
                // so treat either copy being set as "set".
                var combinedFlags2 = (byte)(flags2 | flags2Mirror);

                // Mirrors the game's own guest/special-unit test: (Flags2 & 0x04) || (Flags1 & 0x09).
                // See set_status_counter (0x140278CFE), unitwork_init2all (0x140278E55) and
                // check_tobe_crystal (0x14030FA73). Those all read the +0x05 copy of Flags2, so this
                // deliberately uses `flags2` rather than the combined byte used for the team test.
                var isGuest = (flags1 & BattleUnits.Flags1GuestMask) != 0
                           || (flags2 & BattleUnits.Flags2NonCrystalMask) != 0;

                if (isGuest)
                {
                    if (verbose && configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Guest        SpriteSet:0x{spriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{job:X2}, Flags1:0x{flags1:X2}, Flags2:0x{flags2:X2}, Flags2Copy:0x{flags2Mirror:X2}", Color.Goldenrod);
                    SetUnitControlled(pBattleUnit, flags2, flags2Mirror, combinedFlags2, configuration.ControlGuests, "guest", unitIndex);
                }
                else if ((combinedFlags2 & BattleUnits.Flags2EnemyMask) != 0)
                {
                    // Marked as Team 1 or Team 2
                    if (verbose && configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Enemy        SpriteSet:0x{spriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{job:X2}, Flags1:0x{flags1:X2}, Flags2:0x{flags2:X2}, Flags2Copy:0x{flags2Mirror:X2}", Color.Salmon);
                    SetUnitControlled(pBattleUnit, flags2, flags2Mirror, combinedFlags2, configuration.ControlEnemies, "enemy", unitIndex);
                }
                else
                {
                    if (verbose && configuration.LoggingEnabled) _logger.WriteLine($"{i:d2}.) Player       SpriteSet:0x{spriteSet:X2}, Index:0x{unitIndex:X2}, Job:0x{job:X2}, Flags1:0x{flags1:X2}, Flags2:0x{flags2:X2}, Flags2Copy:0x{flags2Mirror:X2}", Color.Green);
                    SetUnitControlled(pBattleUnit, flags2, flags2Mirror, combinedFlags2, configuration.ControlPlayerUnits, "player unit", unitIndex);
                }
            }
        }

        /// <summary>
        /// Brings <c>Flags2</c> and its mirror at <c>+0x1EE</c> in line with
        /// <paramref name="shouldControl"/>. Writes nothing when the unit already matches.
        /// </summary>
        private void SetUnitControlled(nuint pBattleUnit, byte flags2, byte flags2Mirror, byte combinedFlags2, bool shouldControl, string kind, byte unitIndex)
        {
            if (shouldControl == ((combinedFlags2 & BattleUnits.Flags2HumanControl) != 0))
            {
                return;
            }

            if (_configuration.LoggingEnabled) _logger.WriteLine($"                  {(shouldControl ? "Giving" : "Removing")} control of {kind} 0x{unitIndex:X2}...");

            var control = BattleUnits.Flags2HumanControl;
            Memory.Instance.Write<byte>(pBattleUnit + BattleUnits.OffsetFlags2, shouldControl ? (byte)(flags2 | control) : (byte)(flags2 & ~control));
            Memory.Instance.Write<byte>(pBattleUnit + BattleUnits.OffsetFlags2Mirror, shouldControl ? (byte)(flags2Mirror | control) : (byte)(flags2Mirror & ~control));
        }

        #region Standard Overrides
        public override void Disposing()
        {
            _reapplyTimer?.Dispose();
            _reapplyTimer = null;
        }

        public override void ConfigurationUpdated(Config configuration)
        {
            var previous = _configuration;
            _configuration = configuration;

            // The template reloads the config from disk on every write, including the writes we make
            // ourselves from the settings overlay. Re-applying only when a value actually changed
            // keeps a checkbox toggle down to a single pass over the units instead of running the
            // loop a second time on the file watcher's thread.
            var changed = previous.LoggingEnabled != configuration.LoggingEnabled
                       || previous.ControlGuests != configuration.ControlGuests
                       || previous.ControlEnemies != configuration.ControlEnemies
                       || previous.ControlPlayerUnits != configuration.ControlPlayerUnits;

            if (configuration.LoggingEnabled) _logger.WriteLine($"[{_modConfig.ModId}] Config Updated: {(changed ? "Applying" : "No change, skipping apply")}");

            if (changed)
            {
                UpdateUnitControl();
            }
        }
        #endregion

        #region For Exports, Serialization etc.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Mod() { }
#pragma warning restore CS8618
        #endregion
    }
}