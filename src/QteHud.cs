using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace MimesisEscape;

internal sealed class QteHud
{
    private static readonly FieldInfo CrosshairField = AccessTools.Field(typeof(GameMainBase), "crosshairui");
    private GameMainBase? owner;
    private TextMeshProUGUI? label;

    internal void Show(GameMainBase? main, string text)
    {
        if (main == null) { Hide(); return; }
        var crosshair = CrosshairField.GetValue(main) as UIPrefab_crosshair;
        if (crosshair == null) { Hide(); return; }
        var source = crosshair.UE_UI_Text;
        var canvas = source.GetComponentInParent<Canvas>();
        if (source.font == null || canvas == null) { Hide(); return; }
        if (label == null || owner != main)
        {
            Dispose();
            owner = main;
            var go = new GameObject("MimesisEscapePrompt", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.layer = source.gameObject.layer;
            go.transform.SetParent(canvas.rootCanvas.transform, false);
            label = go.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.richText = false;
            label.alignment = TextAlignmentOptions.Center;
            var rect = label.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.28f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(900, 80);
        }
        // Inherit the active game's HUD style, including locale-dependent font/material changes.
        label.font = source.font;
        label.fontSharedMaterial = source.fontSharedMaterial;
        label.fontSize = source.fontSize;
        label.fontStyle = source.fontStyle;
        label.color = source.color;
        label.enableVertexGradient = source.enableVertexGradient;
        label.colorGradient = source.colorGradient;
        label.characterSpacing = source.characterSpacing;
        label.wordSpacing = source.wordSpacing;
        label.lineSpacing = source.lineSpacing;
        label.text = text;
        label.gameObject.SetActive(true);
    }

    internal void Hide()
    {
        if (label != null) label.gameObject.SetActive(false);
    }

    internal void Dispose()
    {
        if (label != null) Object.Destroy(label.gameObject);
        label = null;
        owner = null;
    }
}
