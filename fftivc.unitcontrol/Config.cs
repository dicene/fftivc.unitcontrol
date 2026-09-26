using fftivc.unitcontrol.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace fftivc.unitcontrol.Configuration
{
    public class Config : Configurable<Config>
    {
        [DisplayName("Logging Enabled")]
        [Description("Enable to log debug information.")]
        [DefaultValue(false)]
        public bool LoggingEnabled { get; set; } = false;

        [DisplayName("Control Guests")]
        [Description("Enable to control Guest units.")]
        [DefaultValue(false)]
        public bool ControlGuests { get; set; } = false;

        [DisplayName("Control Enemies")]
        [Description("Enable to control Enemy units.")]
        [DefaultValue(false)]
        public bool ControlEnemies { get; set; } = false;

        [DisplayName("Control Player Units")]
        [Description("Disable to have all Player units be controlled by AI.")]
        [DefaultValue(true)]
        public bool ControlPlayerUnits { get; set; } = true;

        /// <summary>
        /// Restores every setting to the value declared in its <see cref="DefaultValueAttribute"/>.
        /// <para>
        /// Defaults are: player units controlled, guests and enemies left to the AI.
        /// Derived from the attributes so that adding a new setting does not require updating this method.
        /// </para>
        /// </summary>
        public void ResetToDefaults()
        {
            foreach (var property in typeof(Config).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length > 0)
                    continue;

                if (property.GetCustomAttribute<DefaultValueAttribute>() is { } defaultValue)
                    property.SetValue(this, defaultValue.Value);
            }
        }
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
    }
}
