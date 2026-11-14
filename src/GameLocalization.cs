using System.Reflection;
using HarmonyLib;
using TMPro;

namespace MimesisEscape;

internal static class GameLocalization
{
    private static readonly FieldInfo LanguageField = AccessTools.Field(typeof(Hub), "lcman");
    private static readonly FieldInfo RtlField = AccessTools.Field(typeof(Hub), "rtlEnforcer");

    internal static string Language => Hub.s == null ? "en" :
        (LanguageField.GetValue(Hub.s) as L10NManager)?.language ?? "en";
    internal static EscapeText Text => EscapeTranslations.Get(Language);

    // Match the game's UIApplyL10N, including its Arabic shaping and direction.
    internal static void ApplyDirection(TMP_Text text)
    {
        var enforcer = Hub.s == null ? null : RtlField.GetValue(Hub.s) as RTLEnforcer;
        if (enforcer != null)
            enforcer.ApplyText(text);
        else if (Language == "ar")
        {
            text.isRightToLeftText = true;
            text.text = ArabicSupport.ArabicFixer.FixForRTL(text.text, true, false);
        }
        else text.isRightToLeftText = false;
    }
}