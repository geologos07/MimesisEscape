using System;
using System.Collections.Generic;

namespace MimesisEscape;

// The language codes are the public values of the game's L10NManager.Language enum.
internal sealed class EscapeText
{
    internal readonly string Title, Prompt, Conflict, Error, Hud, Seconds;
    internal EscapeText(string title, string prompt, string conflict, string error, string hud, string seconds)
    { Title = title; Prompt = prompt; Conflict = conflict; Error = error; Hud = hud; Seconds = seconds; }
}

internal static class EscapeTranslations
{
    private static readonly Dictionary<string, EscapeText> Texts = new Dictionary<string, EscapeText>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new EscapeText("Escape (QTE)", "Press a key (Esc to cancel)", "{0} is in use — choose another or Esc", "Could not save — try again", "Escape", "s"),
        ["ko"] = new EscapeText("탈출 (QTE)", "키를 누르세요 (Esc: 취소)", "{0} 사용 중 — 다른 키 또는 Esc", "저장 실패 — 다시 선택하세요", "탈출", "초"),
        ["es"] = new EscapeText("Liberarse (QTE)", "Pulsa una tecla (Esc: cancelar)", "{0} en uso — otra tecla o Esc", "Error al guardar — inténtalo de nuevo", "Liberarse", "s"),
        ["ja"] = new EscapeText("脱出 (QTE)", "キーを押す（Esc：キャンセル）", "{0} は使用中 — 別のキーか Esc", "保存失敗 — もう一度選択", "脱出", "秒"),
        ["fr"] = new EscapeText("Libération (QTE)", "Appuyez sur une touche (Échap : annuler)", "{0} utilisée — autre touche ou Échap", "Échec de l’enregistrement — réessayez", "Libération", "s"),
        ["de"] = new EscapeText("Befreiung (QTE)", "Taste drücken (Esc: Abbrechen)", "{0} belegt — andere Taste oder Esc", "Speichern fehlgeschlagen — erneut wählen", "Befreiung", "s"),
        ["zh_cn"] = new EscapeText("挣脱（QTE）", "按下按键（Esc 取消）", "{0} 已占用 — 换一个键或按 Esc", "保存失败 — 请重新选择", "挣脱", "秒"),
        ["zh_tw"] = new EscapeText("掙脫（QTE）", "按下按鍵（Esc 取消）", "{0} 已被使用 — 換一個鍵或按 Esc", "儲存失敗 — 請重新選擇", "掙脫", "秒"),
        ["pt_br"] = new EscapeText("Libertar-se (QTE)", "Pressione uma tecla (Esc: cancelar)", "{0} em uso — outra tecla ou Esc", "Falha ao salvar — tente novamente", "Libertar-se", "s"),
        ["it"] = new EscapeText("Liberazione (QTE)", "Premi un tasto (Esc: annulla)", "{0} occupato — altro tasto o Esc", "Salvataggio fallito — riprova", "Liberazione", "s"),
        ["ru"] = new EscapeText("Освобождение (QTE)", "Нажмите клавишу (Esc — отмена)", "{0} занята — другая клавиша или Esc", "Ошибка сохранения — повторите выбор", "Освобождение", "с"),
        ["uk"] = new EscapeText("Звільнення (QTE)", "Натисніть клавішу (Esc — скасувати)", "{0} зайнята — інша клавіша або Esc", "Помилка збереження — оберіть знову", "Звільнення", "с"),
        ["vi"] = new EscapeText("Thoát (QTE)", "Nhấn phím (Esc để hủy)", "{0} đã dùng — chọn phím khác hoặc Esc", "Không thể lưu — hãy chọn lại", "Thoát", "giây"),
        ["th"] = new EscapeText("หลบหนี (QTE)", "กดปุ่ม (Esc เพื่อยกเลิก)", "{0} ถูกใช้แล้ว — เลือกปุ่มอื่นหรือ Esc", "บันทึกไม่สำเร็จ — เลือกอีกครั้ง", "หลบหนี", "วิ"),
        ["tr"] = new EscapeText("Kurtulma (QTE)", "Bir tuşa basın (Esc: iptal)", "{0} kullanımda — başka tuş veya Esc", "Kaydedilemedi — yeniden seçin", "Kurtulma", "sn"),
        ["pl"] = new EscapeText("Uwolnienie (QTE)", "Naciśnij klawisz (Esc: anuluj)", "{0} zajęty — inny klawisz lub Esc", "Nie udało się zapisać — wybierz ponownie", "Uwolnienie", "s"),
        ["ar"] = new EscapeText("التحرر (QTE)", "اضغط مفتاحًا (Esc للإلغاء)", "{0} مستخدم — اختر مفتاحًا آخر أو Esc", "تعذر الحفظ — حاول مجددًا", "التحرر", "ث"),
        ["da"] = new EscapeText("Slip fri (QTE)", "Tryk på en tast (Esc: annuller)", "{0} bruges — vælg en anden tast eller Esc", "Kunne ikke gemme — prøv igen", "Slip fri", "s")
    };

    internal static EscapeText Get(string? language)
    {
        if (language != null && Texts.TryGetValue(language, out var value) && value != null) return value;
        return Texts["en"];
    }
}