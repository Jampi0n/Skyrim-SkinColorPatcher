using Mutagen.Bethesda.Synthesis.Settings;

namespace SkinColorPatcher {
    public class Settings {
        [SynthesisTooltip("Changes the skin color for vampires with default skin color to the default skin color of the regular race. This allows USSEP to fix player vampire skin color without affecting NPCs.")]
        public bool defaultVampireColorPatch = true;
    }
}
