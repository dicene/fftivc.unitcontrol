using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using System.Drawing;
using System.Numerics;

namespace fftivc.unitcontrol
{
    public static class ExtensionMethods
    {
        public static Vector4 ToV4(this Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    [ImGuiMenu(Category = "Mods", Priority = 0, Owner = "Unit Control")]
    public class UnitControlSettingsMenu : IImGuiComponent
    {
        public readonly Mod _mod;

        public UnitControlSettingsMenu(Mod mod)
        {
            _mod = mod;
        }

        public IImGui imGui { get; set; }

        public bool IsOverlay => false;
        public bool WindowOpen = true;

        public void RenderMenu(IImGuiShell imGuiShell)
        {
            if (imGui.MenuItem("Unit Control Settings"))
            {
                WindowOpen = true;
            }
        }

        public void Render(IImGuiShell imGuiShell)
        {
            if (!WindowOpen)
                return;

            // Always read the live config: the template replaces the instance whenever Config.json changes.
            var configuration = _mod.Configuration;

            var size = imGui.GetMainViewport().Size;
            var workSize = imGui.GetMainViewport().WorkSize;

            imGui.SetNextWindowSize(new Vector2(400, workSize.Y * 0.8f), ImGuiCond.ImGuiCond_Appearing);
            imGui.SetNextWindowPos(new Vector2(size.X - 400 - 100, workSize.Y * 0.2f / 2f), ImGuiCond.ImGuiCond_Appearing);
            imGui.PushFontFloat(null, 18f);
            // Must be pushed before Begin() for it to apply to the window background itself.
            imGui.PushStyleColorImVec4(ImGuiCol.ImGuiCol_WindowBg, Color.FromArgb(150, 0, 0, 0).ToV4());

            var visible = imGui.Begin("Unit Control Settings", ref WindowOpen, ImGuiWindowFlags.ImGuiWindowFlags_None);

            if (visible)
            {
                var loggingEnabled = configuration.LoggingEnabled;
                var controlPlayerUnits = configuration.ControlPlayerUnits;
                var controlGuests = configuration.ControlGuests;
                var controlEnemies = configuration.ControlEnemies;

                var changed = false;
                changed |= imGui.Checkbox("Enable Logging", ref loggingEnabled);
                changed |= imGui.Checkbox("Control Player Units", ref controlPlayerUnits);
                changed |= imGui.Checkbox("Control Guests", ref controlGuests);
                changed |= imGui.Checkbox("Control Enemies", ref controlEnemies);

                if (changed)
                {
                    configuration.LoggingEnabled = loggingEnabled;
                    configuration.ControlPlayerUnits = controlPlayerUnits;
                    configuration.ControlGuests = controlGuests;
                    configuration.ControlEnemies = controlEnemies;

                    _mod.ApplyConfiguration();
                }

                imGui.Separator();

                if (imGui.Button("Reset to Defaults"))
                {
                    _mod.ResetConfigurationToDefaults();
                }
            }

            imGui.End();
            imGui.PopStyleColor();
            imGui.PopFont();
        }
    }
}
