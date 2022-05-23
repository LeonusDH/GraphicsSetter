﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GraphicSetter
{
    public class SettingsGroup : IExposable
    {
        //
        public bool useMipMap = true;
        public bool useAntiA = true;
        public bool useCustomPawnAtlas = false;

        public float mipMapBias = -0.5f;
        public AntiAliasing antiALevel = AntiAliasing.FourX;

        public FilterMode filterMode = FilterMode.Trilinear;
        public int anisoLevel = 6;

        public int pawnTexResScale = 4;
        public int mainTexResScale = 1;

        //FIXED RANGE DATA
        public static readonly FloatRange PawnTexScaleRange = new FloatRange(1, 4);
        public static readonly IntRange MainTexScaleRange = new IntRange(1, 2);

        public static readonly FloatRange AnisoRange = new FloatRange(1, 9);
        public static readonly FloatRange MipMapBiasRange = new FloatRange(-1f, 0.75f);

        public void ExposeData()
        {
            Scribe_Values.Look(ref anisoLevel, "anisoLevel");
            Scribe_Values.Look(ref useMipMap, "useMipMap");
            Scribe_Values.Look(ref useAntiA, "useAntiA");
            Scribe_Values.Look(ref useCustomPawnAtlas, "useCustomPawnAtlas");
            Scribe_Values.Look(ref filterMode, "filterMode");
            Scribe_Values.Look(ref mipMapBias, "mipMapBias");
            Scribe_Values.Look(ref pawnTexResScale, "pawnTexResScale");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pawnTexResScale > 4)
                {
                    pawnTexResScale = 4;
                }
            }
        }

        public bool IsDefault()
        {
            if (anisoLevel != 6) return false;
            if (filterMode != FilterMode.Trilinear) return false;
            if (useMipMap != true) return false;
            if (mipMapBias != -0.5f) return false;
            if (pawnTexResScale != 4) return false;
            return true;
        }

        public void Reset()
        {
            anisoLevel = 6;
            filterMode = FilterMode.Trilinear;
            useMipMap = true;
            mipMapBias = -0.5f;
            pawnTexResScale = 4;
        }
    }

    public class GraphicsSettings : ModSettings
    {
        private SettingsGroup lastSettings = new SettingsGroup();
        public bool CausedMemOverflow = false;

        internal enum GraphicsTabOption
        {
            Advanced,
            Memory
        }

        //SETTINGS
        public static SettingsGroup mainSettings;

        private GraphicsTabOption SelTab { get; set; } = GraphicsTabOption.Advanced;

        public GraphicsSettings()
        {
            mainSettings = new SettingsGroup();
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            //
            GUI.BeginGroup(inRect);
            Rect tabRect = new Rect(0, TabDrawer.TabHeight, inRect.width, 0);
            Rect menuRect = new Rect(0, TabDrawer.TabHeight, inRect.width, inRect.height - TabDrawer.TabHeight);

            Widgets.DrawMenuSection(menuRect);
            //
            var tabs = new List<TabRecord>();
            tabs.Add(new TabRecord("Advanced", delegate { SelTab = GraphicsTabOption.Advanced; }, SelTab == GraphicsTabOption.Advanced));
            tabs.Add(new TabRecord("Memory", delegate { SelTab = GraphicsTabOption.Memory; }, SelTab == GraphicsTabOption.Memory));
            TabDrawer.DrawTabs(tabRect, tabs);

            switch (SelTab)
            {
                case GraphicsTabOption.Advanced:
                    DrawAdvanced(menuRect.ContractedBy(15));
                    break;
                case GraphicsTabOption.Memory:
                    DrawMemory(menuRect.ContractedBy(10));
                    break;
            }

            GUI.EndGroup();
        }

        public bool AnySettingsChanged()
        {
            if (mainSettings.anisoLevel != lastSettings.anisoLevel)
                return true;
            if (mainSettings.filterMode != lastSettings.filterMode)
                return true;
            if (mainSettings.mipMapBias != lastSettings.mipMapBias)
                return true;
            if (mainSettings.useMipMap != lastSettings.useMipMap)
                return true;
            return false;
        }

        private string mipMapBiasToolTip = "This value changes how blurry textures can get depending on zoom, it is recommended to be equal or below 0.";
        private string anisoLevelToolTip = "Set the level of anisotropic filtering, higher levels may reduce performance on older graphics cards.";
        private string pawnAtlasToolTip = "Sets a multipler for the cached pawn atlas size, the higher the value the more detail your pawns will have - uses more memory.";

        private void DrawAdvanced(Rect rect)
        {
            Listing_Standard listing = new Listing_Standard();
            var leftRect = rect.LeftHalf().ContractedBy(5).Rounded();
            listing.Begin(leftRect);
            {
                listing.Label("General Settings");
                listing.GapLine();

                listing.CheckboxLabeled("(Recommended) Activate Mip-Mapping", ref mainSettings.useMipMap);
                if (mainSettings.useMipMap)
                {
                    mainSettings.mipMapBias = listing.LabeledSlider("MipMap Bias", SettingsGroup.MipMapBiasRange,
                        mainSettings.mipMapBias, "Sharpest", "Blurriest", mipMapBiasToolTip, 0.05f);
                }

                mainSettings.anisoLevel = (int) listing.LabeledSlider("Anisotropic Filter Level", SettingsGroup.AnisoRange,
                    mainSettings.anisoLevel,
                    tooltip: anisoLevelToolTip,
                    roundTo: 1);

                SetFilter(listing);

                if (!mainSettings.IsDefault())
                {
                    if (listing.ButtonText("Reset"))
                    {
                        mainSettings.Reset();
                    }
                }
            }
            listing.End();


            var rightRect = rect.RightHalf().ContractedBy(5).Rounded();
            Listing_Standard listing2 = new Listing_Standard();
            listing2.Begin(rightRect);
            {
                listing2.Label("Pawn Render Settings");
                listing2.GapLine();

                listing2.CheckboxLabeled("Custom Pawn Atlas", ref mainSettings.useCustomPawnAtlas);

                if (mainSettings.useCustomPawnAtlas)
                {
                    mainSettings.pawnTexResScale = (int) listing2.LabeledSlider("Pawn Atlas Scale", SettingsGroup.PawnTexScaleRange, mainSettings.pawnTexResScale, roundTo: 1, tooltip: pawnAtlasToolTip);

                    listing2.Gap();
                    if (listing2.ButtonTextEnabled("Apply Atlas", Current.ProgramState == ProgramState.Playing, "Can only apply during running game."))
                    {
                        foreach (PawnTextureAtlas pawnTextureAtlas in GlobalTextureAtlasManager.pawnTextureAtlases)
                        {
                            pawnTextureAtlas.Destroy();
                        }
                    }

                    if (mainSettings.pawnTexResScale > 2)
                    {
                        GUI.color = Color.red;
                        listing2.Label("Warning: Higher texture scaling uses more VRAM!");
                        GUI.color = Color.white;
                    }
                }
                //listing2.RenderInListing(listing.curY, StaticContent.MemoryData.DrawPawnAtlasMemory);
            }
            listing2.End();

            /*
            if (Widgets.ButtonText(resetButton2, "Drop atlas"))
            {
                string path = Path.GetFullPath("C:\\Users\\maxim\\Desktop\\AtlasTest");
                GlobalTextureAtlasManager.DumpStaticAtlases(path);
                GlobalTextureAtlasManager.DumpPawnAtlases(path);
            }
            */

            if (AnySettingsChanged())
            {
                GUI.color = Color.red;
                Text.Font = GameFont.Medium;
                string text = "You will have to restart the game to apply changes!";
                Vector2 size = Text.CalcSize(text);
                float x2 = (rect.width - size.x) / 2f;
                float x3 = (rect.width - 150) / 2f;
                float y2 = rect.yMax - 150;
                Widgets.Label(new Rect(x2, y2, size.x, size.y), text);
                if (Widgets.ButtonText(new Rect(x3, y2 + size.y, 150, 45), "Restart Game", true, true))
                {
                    this.Write();
                    GenCommandLine.Restart();
                }
                Text.Font = GameFont.Small;
                GUI.color = Color.white;
            }
        }

        public void DrawMemory(Rect rect)
        {
            StaticContent.MemoryData.DrawMemoryData(rect);
        }

        public void SetFilter(Listing_Standard listing)
        {
            listing.Label("Texture Filtering: ");
            if(listing.RadioButton(FilterMode.Bilinear.ToString(), mainSettings.filterMode == FilterMode.Bilinear))
            {
                mainSettings.filterMode = FilterMode.Bilinear;
            }
            if (listing.RadioButton(FilterMode.Trilinear.ToString(), mainSettings.filterMode == FilterMode.Trilinear))
            {
                mainSettings.filterMode = FilterMode.Trilinear;
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref mainSettings, "settings");
            Scribe_Values.Look(ref CausedMemOverflow, "causedOverflow");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mainSettings ??= new SettingsGroup();
                lastSettings = new SettingsGroup();
                lastSettings.anisoLevel = mainSettings.anisoLevel;
                lastSettings.useMipMap = mainSettings.useMipMap;
                lastSettings.filterMode = mainSettings.filterMode;
                lastSettings.mipMapBias = mainSettings.mipMapBias;
            }
        }
    }
}
