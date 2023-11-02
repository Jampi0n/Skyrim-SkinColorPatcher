using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins;
using Noggog;
using System.Drawing;
using Mutagen.Bethesda.FormKeys.SkyrimLE;

namespace SkinColorPatcher {

    public class SkinColor {
        public readonly Color color;
        public readonly float interpolate;

        public SkinColor(Color color, float interpolate) {
            this.color = color;
            this.interpolate = interpolate;
        }

        public Color Interpolate(float interpolate = -1) {
            if (interpolate < 0) {
                interpolate = this.interpolate;
            }

            var add = 127.5 * (1 - interpolate);
            return Color.FromArgb(0, (int)(color.R * interpolate + add), (int)(color.G * interpolate + add), (int)(color.B * interpolate + add));
        }
    }
    public class SkinData {
        public readonly int tintIndex;
        public readonly SkinColor defaultColor;
        public readonly int defaultIndex;
        public readonly Dictionary<int, SkinColor> additionalColors;

        private SkinData(int tintIndex, SkinColor defaultColor, int defaultIndex, Dictionary<int, SkinColor> additionalColors) {
            this.tintIndex = tintIndex;
            this.defaultColor = defaultColor;
            this.defaultIndex = defaultIndex;
            this.additionalColors = additionalColors;
        }

        public static SkinData? Get(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IRaceGetter race, bool female) {
            int defaultIndex = 0;
            SkinColor? defaultColor = null;
            if (race.HeadData == null) { return null; }

            var raceData = female ? race.HeadData.Female : race.HeadData.Male;
            var tintIndex = -1;
            if (raceData == null) { return null; }

            var skinTintMask = raceData!.TintMasks.FirstOrDefault(mask => mask!.MaskType == TintAssets.TintMaskType.SkinTone, null);
            if (skinTintMask == null) { return null; }
            var defaultPreset = skinTintMask.PresetDefault.TryResolve(state.LinkCache);
            if (defaultPreset == null) { return null; }

            tintIndex = skinTintMask.Index!.Value;
            var additionalColors = new Dictionary<int, SkinColor>();
            foreach (var preset in skinTintMask.Presets) {
                if (preset.Index.HasValue && preset.Color.TryResolve(state.LinkCache, out var presetColor)) {
                    var skinColor = new SkinColor(presetColor.Color, preset.DefaultValue.GetValueOrDefault(1));
                    additionalColors.Add(preset.Index.Value, skinColor);
                    if (preset.Color.FormKey == defaultPreset.FormKey) {
                        defaultColor = skinColor;
                        defaultIndex = preset.Index.GetValueOrDefault(0);
                    }
                }
            }
            if (defaultColor == null) { return null; }
            return new SkinData(tintIndex, defaultColor, defaultIndex, additionalColors);
        }
    }

    public class RaceData {
        public readonly SkinData? male;
        public readonly SkinData? female;

        private RaceData(SkinData? male, SkinData? female) {
            this.male = male;
            this.female = female;
        }

        private static readonly Dictionary<FormKey, RaceData?> dict = new();

        public static RaceData? Get(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, IFormLinkGetter<IRaceGetter> raceLink) {
            if (!dict.ContainsKey(raceLink.FormKey)) {
                var race = raceLink.Resolve(state.LinkCache);
                var male = SkinData.Get(state, race, false);
                var female = SkinData.Get(state, race, true);
                if (male != null || female != null) {
                    dict.Add(raceLink.FormKey, new RaceData(male, female));
                } else {
                    dict.Add(raceLink.FormKey, null);
                }
            }
            return dict[raceLink.FormKey];
        }

        public SkinData? GetSkinData(bool female) {
            return female ? this.female : male;
        }
    }

    public class NpcData {
        public readonly SkinData raceSkinData;
        public readonly ITintLayerGetter? skinLayer;

        private NpcData(SkinData raceSkinData, ITintLayerGetter? skinLayer) {
            this.raceSkinData = raceSkinData;
            this.skinLayer = skinLayer;
        }

        public static NpcData? Get(IPatcherState<ISkyrimMod, ISkyrimModGetter> state, INpcGetter npcGetter) {

            var female = npcGetter.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);

            var raceData = RaceData.Get(state, npcGetter.Race);
            if (raceData == null) { return null; }
            var raceSkinData = raceData.GetSkinData(female);
            if (raceSkinData == null) { return null; }

            if (npcGetter.TintLayers != null) {
                var skinLayer = npcGetter.TintLayers.FirstOrDefault(layer => layer!.Index == raceSkinData.tintIndex, null);
                return new NpcData(raceSkinData, skinLayer);
            } else { return new NpcData(raceSkinData, null); }
        }
    }

    public class Program {
        public static Lazy<Settings> _settings = null!;
        public static Settings settings => _settings.Value;
        public static async Task<int> Main(string[] args) {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetAutogeneratedSettings(
                    nickname: "Settings",
                    path: "settings.json",
                    out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, "SkinColorPatch.esp")
                .Run(args);
        }

        public static Color GetInterpolatedColor(Color color, float interpolate) {
            var add = 127.5 * (1 - interpolate);
            return Color.FromArgb((int)(color.R * interpolate + add), (int)(color.G * interpolate + add), (int)(color.B * interpolate + add));
        }


        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            if (settings.defaultVampireColorPatch) { DefaultVampireColorPatch(state); }
            UpdateTextureLighting(state);
        }

        public static void DefaultVampireColorPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            var tmp = state.LoadOrder.PriorityOrder.Npc().WinningOverrides().ToArray();
            var toRegularRace = new Dictionary<IFormLinkGetter<IRaceGetter>, IFormLinkGetter<IRaceGetter>> {
                { Skyrim.Race.NordRaceVampire, Skyrim.Race.NordRace },
                { Skyrim.Race.BretonRaceVampire, Skyrim.Race.BretonRace },
                { Skyrim.Race.ImperialRaceVampire, Skyrim.Race.ImperialRace },
                { Skyrim.Race.RedguardRaceVampire, Skyrim.Race.RedguardRace },
                { Skyrim.Race.WoodElfRaceVampire, Skyrim.Race.WoodElfRace },
                { Skyrim.Race.DarkElfRaceVampire, Skyrim.Race.DarkElfRace },
                { Skyrim.Race.HighElfRaceVampire, Skyrim.Race.HighElfRace },
                { Skyrim.Race.OrcRaceVampire, Skyrim.Race.OrcRace },
                { Skyrim.Race.KhajiitRaceVampire, Skyrim.Race.KhajiitRace },
                { Skyrim.Race.ArgonianRaceVampire, Skyrim.Race.ArgonianRace }
            };

            for (int i = 0; i < tmp.Length; i++) {
                var npcGetter = tmp[i];

                if (toRegularRace.TryGetValue(npcGetter.Race, out var regularRaceLink)) {
                    var female = npcGetter.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Female);

                    var vampireRaceData = RaceData.Get(state, npcGetter.Race);
                    if (vampireRaceData == null) { continue; }
                    var vampireSkinData = vampireRaceData.GetSkinData(female);
                    if (vampireSkinData == null) { continue; }

                    if (npcGetter.TintLayers == null || npcGetter.TintLayers.FirstOrDefault(layer => layer!.Index == vampireSkinData.tintIndex, null) == null) {
                        var regularRaceData = RaceData.Get(state, regularRaceLink);
                        if (regularRaceData == null) { continue; }
                        var regularSkinData = regularRaceData.GetSkinData(female);
                        if (regularSkinData == null) { continue; }
                        var npc = state.PatchMod.Npcs.GetOrAddAsOverride(npcGetter);

                        var regularColor = regularSkinData.defaultColor.color;
                        var vampireIndex = -1;
                        foreach (var kv in vampireSkinData.additionalColors) {
                            var color = kv.Value.color;

                            if (color.R == regularColor.R && color.G == regularColor.G && color.B == regularColor.B) {
                                vampireIndex = kv.Key;
                                break;
                            }
                        }

                        npc.TintLayers.Add(new TintLayer() {
                            Index = (ushort)regularSkinData.tintIndex,
                            Color = regularSkinData.defaultColor.color,
                            InterpolationValue = regularSkinData.defaultColor.interpolate,
                            Preset = (short)vampireIndex
                        });
                    }
                }
            }
        }

        public static void UpdateTextureLighting(IPatcherState<ISkyrimMod, ISkyrimModGetter> state) {
            var tmp = state.LoadOrder.PriorityOrder.Npc().WinningOverrides().ToArray();
            for (int i = 0; i < tmp.Length; i++) {
                var npcGetter = tmp[i];

                var npcData = NpcData.Get(state, npcGetter);
                if (npcData == null) { continue; }
                var skinLayer = npcData.skinLayer;
                var raceSkinData = npcData.raceSkinData;
                Color calculatedColor;
                if (skinLayer != null && skinLayer.Preset.HasValue) {
                    var presetIndex = skinLayer.Preset.Value;
                    if (presetIndex == -1) { continue; }
                    var interpolate = skinLayer.InterpolationValue.GetValueOrDefault(1);
                    if (raceSkinData.additionalColors.TryGetValue(presetIndex, out var color)) {
                        calculatedColor = color.Interpolate(interpolate);
                    } else { continue; }
                } else {
                    calculatedColor = raceSkinData.defaultColor.Interpolate();
                }

                var textureLighting = npcGetter.TextureLighting!.Value;
                if (textureLighting.R != calculatedColor.R || textureLighting.G != calculatedColor.G || textureLighting.B != calculatedColor.B) {
                    var npc = state.PatchMod.Npcs.GetOrAddAsOverride(npcGetter);
                    npc.TextureLighting = calculatedColor;

                    var skinLayerMod = npc.TintLayers.FirstOrDefault(layer => layer!.Index == raceSkinData.tintIndex, null);
                    if(skinLayerMod != null) {
                        skinLayerMod.Color = calculatedColor;
                    }
                }
            }
        }
    }
}
