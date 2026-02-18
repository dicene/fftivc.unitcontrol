using fftivc.unitcontrol.Configuration;
using NenTools.ImGui.Interfaces;
using NenTools.ImGui.Interfaces.Shell;
using NenTools.ImGui.Interfaces.Shell.Textures;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace fftivc.unitcontrol
{
    public static class ExtensionMethods
    {
        public static Vector4 ToV4(this Color color) => new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    [ImGuiMenu(Category = "Mods", Priority = 0, Owner = "Unit Control")]
    public class UnitControlSettingsMenu : IImGuiComponent
    {
        public readonly Config _configuration;
        public readonly Mod _mod;

        public UnitControlSettingsMenu(Mod mod, Config configuration)
        {
            _mod = mod;
            _configuration = configuration;
        }

        public IImGui imGui { get; set; }

        public bool IsOverlay => false;
        public bool WindowOpen = true;
        //public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.ImGuiWindowFlags_NoBackground | ImGuiWindowFlags.ImGuiWindowFlags_NoInputs | ImGuiWindowFlags.ImGuiWindowFlags_AlwaysAutoResize;
        //public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.ImGuiWindowFlags_NoTitleBar | ImGuiWindowFlags.ImGuiWindowFlags_NoResize | ImGuiWindowFlags.ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags.ImGuiWindowFlags_NoInputs | ImGuiWindowFlags.ImGuiWindowFlags_AlwaysAutoResize;
        //public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.ImGuiWindowFlags_NoResize | ImGuiWindowFlags.ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags.ImGuiWindowFlags_NoCollapse | ImGuiWindowFlags.ImGuiWindowFlags_NoBackground | ImGuiWindowFlags.ImGuiWindowFlags_NoInputs | ImGuiWindowFlags.ImGuiWindowFlags_AlwaysAutoResize;
        //public ImGuiWindowFlags WindowFlags = ImGuiWindowFlags.ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags.ImGuiWindowFlags_NoBackground | ImGuiWindowFlags.ImGuiWindowFlags_NoInputs | ImGuiWindowFlags.ImGuiWindowFlags_AlwaysAutoResize;

        public int LastOverstockCarcassGil { get; set; }

        public void RenderMenu(IImGuiShell imGuiShell)
        {
            if (imGui.MenuItem("Unit Control Settings"))
            {
                WindowOpen = true;
            }
        }

        public void Render(IImGuiShell imGuiShell)
        {
            if (WindowOpen)
            {
                var size = imGui.GetMainViewport().Size;
                var workSize = imGui.GetMainViewport().WorkSize;

                imGui.SetNextWindowSize(new Vector2(400, workSize.Y * 0.8f), ImGuiCond.ImGuiCond_Appearing);
                imGui.SetNextWindowPos(new Vector2(size.X - 400 - 100, workSize.Y * 0.2f / 2f), ImGuiCond.ImGuiCond_Appearing);
                imGui.PushFontFloat(null, 18f);
                var result = imGui.Begin("Unit Control Settings", ref WindowOpen, ImGuiWindowFlags.ImGuiWindowFlags_None);
                imGui.PushStyleColorImVec4(ImGuiCol.ImGuiCol_WindowBg, Color.FromArgb(150, 0, 0, 0).ToV4());

                var changed = false;

                var loggingEnabled = _configuration.LoggingEnabled;
                var controlPlayerUnits = _configuration.ControlPlayerUnits;
                var controlGuests = _configuration.ControlGuests;
                var controlEnemies = _configuration.ControlEnemies;

                changed = changed || imGui.Checkbox("Enable Logging", ref loggingEnabled);
                changed = changed || imGui.Checkbox("Control Player Units", ref controlPlayerUnits);
                changed = changed || imGui.Checkbox("Control Guests", ref controlGuests);
                changed = changed || imGui.Checkbox("Control Enemies", ref controlEnemies);

                if (changed)
                {
                    _configuration.LoggingEnabled = loggingEnabled;
                    _configuration.ControlPlayerUnits = controlPlayerUnits;
                    _configuration.ControlGuests = controlGuests;
                    _configuration.ControlEnemies = controlEnemies;

                    _mod.UpdateUnitControl();
                }
                //var result = imGui.Begin("AutoPoach", ref WindowOpen, WindowFlags);
                //imGui.SetCursorPos(new Vector2(10, 10));

                //if (_chocoboImage is null)
                //{
                //    //byte[] xByte = (byte[])_imageConverter.ConvertTo(a, typeof(byte[]));
                //    //_chocoboImage = imGuiShell.TextureManager.LoadImage(xByte, (uint)a.Width, (uint)a.Height);
                //    byte[] pixelBytes = new byte[image.Width * image.Height * Unsafe.SizeOf<Rgba32>()];
                //    byte[] pixelBytes2 = new byte[image2.Width * image2.Height * Unsafe.SizeOf<Rgba32>()];
                //    image.CopyPixelDataTo(pixelBytes);
                //    image2.CopyPixelDataTo(pixelBytes2);
                //    _chocoboImage = imGuiShell.TextureManager.LoadImage(pixelBytes, (uint)image.Width, (uint)image.Height);
                //    _chocoboImage = imGuiShell.TextureManager.LoadImage(pixelBytes, (uint)image.Width, (uint)image.Height);
                //}
                //else
                //{
                //    imGuiShell.TextureManager.UpdateImage(_chocoboImage, _normalizedScreenBuffer);
                //}

                //imGui.ImageWithBgEx(imGui.CreateTextureRef(_chocoboImage.TexId), new Vector2(116, 142), Vector2.Zero, Vector2.One, Color.FromArgb(0, 0, 0, 0).ToV4(), Color.FromArgb(255, 50, 255, 255).ToV4());
                //imGui.Image(imGui.CreateTextureRef(_chocoboImage.TexId), new Vector2(116, 142));

                //foreach (MonsterID monster in Constants.Constants.MonsterID.GetValues(typeof(Constants.Constants.MonsterID)))
                //{
                //    imGui.Text($"{monster}: ");
                //    imGui.SameLine();
                //    bool val = MonsterIDToConfigMap?[monster]() ?? false;
                //    imGui.Checkbox("Enabled", ref val);
                //    imGui.SameLine();
                //    imGui.TextColored(Color.Gold.ToV4(), "Rare");
                //}

                //for (int i = 0; i < 4; i++)
                //{
                //    imGui.Text($"Item {i}: ");
                //    imGui.SameLine();
                //    imGui.TextColored(Color.LightGray.ToV4(), "Common");
                //    imGui.SameLine();
                //    imGui.TextColored(Color.Gold.ToV4(), "Rare");
                //}                

                imGui.PopFont();
                imGui.PopStyleColor();
                imGui.End();
            }
        }
    }
}
