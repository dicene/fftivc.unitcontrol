using fftivc.unitcontrol.Template.Configuration;
using Reloaded.Mod.Interfaces.Structs;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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
        [DefaultValue(true)]
        public bool ControlGuests { get; set; } = true;

        [DisplayName("Control Enemies")]
        [Description("Enable to control Enemy units.")]
        [DefaultValue(false)]
        public bool ControlEnemies { get; set; } = false;

        [DisplayName("Control Player Units")]
        [Description("Disable to have all Player units be controlled by AI.")]
        [DefaultValue(true)]
        public bool ControlPlayerUnits { get; set; } = true;
    }

    /// <summary>
    /// Allows you to override certain aspects of the configuration creation process (e.g. create multiple configurations).
    /// Override elements in <see cref="ConfiguratorMixinBase"/> for finer control.
    /// </summary>
    public class ConfiguratorMixin : ConfiguratorMixinBase
    {
    }
}
