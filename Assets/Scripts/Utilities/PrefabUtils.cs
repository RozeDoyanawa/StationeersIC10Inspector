using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using JetBrains.Annotations;
using ridorana.IC10Inspector.Objects.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Color = UnityEngine.Color;
using Image = UnityEngine.UI.Image;

namespace ridorana.IC10Inspector.Utilities {
    public class PrefabUtils {

        private static readonly Dictionary<Type, object> RozeColorizers = new();

        public static void AddColorizer<T>(ColorizeCallback<T> callback) {
            RozeColorizers.Add(typeof(T), callback);
        }

        public static ColorizeCallback<T> GetColorizer<T>() {
            if (!RozeColorizers.ContainsKey(typeof(T))) {
                throw new Exception("No colorizer for type " + typeof(T));
            }
            return (ColorizeCallback<T>)RozeColorizers[typeof(T)];
        }
        
        public static void CopyParams<T>(Thing target, Thing source, string path, CopyCallback<T> callback) {
            CopyParams(target, source, path, path, callback);
        }
        
        public static void RozeColorize<T>(Thing target, string path) {
            ColorizeCallback<T> colorizeCallback = GetColorizer<T>();
            RozeColorize<T>(target, path, colorizeCallback);
        }
        
        public static void RozeColorize<T>(Thing target, string path, ColorizeCallback<T> callback) {
            var l = target.transform.Find(path).GetComponent<T>();
            var t = typeof(T);
            callback(l);
        }
        
        public static void CopyParams<T>(Thing target, Thing source, string targetPath, string sourcePath, CopyCallback<T> callback) {
            var l = target.transform.Find(targetPath).GetComponent<T>();
            var e = source.transform.Find(sourcePath).GetComponent<T>();
            callback(l, e);
        }

        public delegate void CopyCallback<T>(T target, T source);
        public delegate void ColorizeCallback<T>(T target);

        public static void CopyTextFontData<T>(T target, T source) where T : Text {
            target.font = source.font;
            target.fontSize = source.fontSize;
            target.fontStyle = source.fontStyle;
            target.material = source.material;
            target.color = source.color;
            target.alignment = source.alignment;
            target.supportRichText = source.supportRichText;
            target.resizeTextForBestFit = source.resizeTextForBestFit;
            target.horizontalOverflow = source.horizontalOverflow;
            target.verticalOverflow = source.verticalOverflow;
            target.lineSpacing = source.lineSpacing;
        }
        
        public static void CopyMeshTextFontData<T>(T target, T source) where T : TextMeshProUGUI{
            target.font = source.font;
            target.fontSize = source.fontSize;
            target.fontStyle = source.fontStyle;
            target.material = source.material;
            target.fontMaterial = source.fontMaterial;
            target.color = source.color;
            target.fontWeight = source.fontWeight;
            target.fontSizeMin = source.fontSizeMin;
            target.fontSizeMax = source.fontSizeMax;
            target.enableAutoSizing = source.enableAutoSizing;
            target.richText = source.richText;
            target.alignment = source.alignment;
            target.horizontalAlignment = source.horizontalAlignment;
            target.horizontalMapping = source.horizontalMapping;
            target.verticalAlignment = source.verticalAlignment;
            target.verticalMapping = source.verticalMapping;
            target.overflowMode = source.overflowMode;
        }

        public static void CopyImageData<T>(T target, T source) where T : Image {
            target.material = source.material;
            target.color = source.color;
            target.maskable = source.maskable;
            target.sprite = source.sprite;
        }

        public static void CopyButtonStyle<T>(T target, T source) where T : Button {
            target.colors = source.colors;
            target.transition = source.transition;
            target.interactable = source.interactable;
        }

        public static Color ColorFromHex(string input) {
            Color newCol;
            if (ColorUtility.TryParseHtmlString(input, out newCol)) {
                return newCol;
            }

            throw new Exception("Color Conversion Failed");
        }

        private static readonly ColorBlock RozeColors = new ColorBlock();

        static PrefabUtils() {
            RozeColors.normalColor = ColorFromHex("#0d4857");
            RozeColors.disabledColor = ColorFromHex("#767676");
            RozeColors.pressedColor = ColorFromHex("#093c49");
            RozeColors.highlightedColor = ColorFromHex("#2a6c7d");
            RozeColors.selectedColor = ColorFromHex("#104755");
            RozeColors.colorMultiplier = 1;
            RozeColors.fadeDuration = 0.1f;
            
            AddColorizer<Button>(ColorizeUIButton);
            AddColorizer<Toggle>(ColorizeUIToggle);
            AddColorizer<Scrollbar>(ColorizeUIScrollbar);
            AddColorizer<Dropdown>(ColorizeUIDropdown);
        }

        public static void ColorizeUIToggle(Toggle target) {
            target.colors = RozeColors;
        }

        public static void ColorizeUIScrollbar(Scrollbar target) {
            target.colors = RozeColors;
        }

        public static void ColorizeUIButton<T>(T target) where T : Button{
            target.colors = RozeColors;
        }
        
        public static void ColorizeUIDropdown<T>(T target) where T : Dropdown{
            target.colors = RozeColors;
        }
        
        public static void CopyButtonStyleRoze<T>(T target, T source) where T : Button {
            //ColorBlock sourceColors = source.colors;
            target.colors = source.colors;
            target.transition = source.transition;
            target.interactable = source.interactable;
        }

        public static void ColorizeUIButtonText<T>(T target) where T : Text{
            target.color = Color.white;
        }





        public static ProgrammableChip GetChipFromHousing(ICircuitHolder housing) {
            if (housing is Thing thing) {
                foreach (Slot slot in thing.Slots) {
                    if (slot.Type == Slot.Class.ProgrammableChip) {
                        var chipFromHousing = slot.Get();
                        if (chipFromHousing is ProgrammableChip) {
                            return (ProgrammableChip)chipFromHousing;
                        }
                    }
                }
            }

            return null;
        }
    }
}