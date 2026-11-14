using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using GameInputManager = Mimic.InputSystem.InputManager;

namespace MimesisEscape;

// One row in the current Settings > Controls list, using its native prefab and input blocker.
internal sealed class KeybindSettings
{
    private readonly Func<Key> currentKey;
    private readonly Action<Key> saveKey;
    private UIPrefab_ControlSetting? screen;
    private UIPrefab_KeybindListItem? row;
    private UIManager? ui;
    private bool waiting;
    private bool released;
    private double deadline;
    private string? displayedLanguage;

    internal KeybindSettings(Func<Key> currentKey, Action<Key> saveKey)
    {
        this.currentKey = currentKey;
        this.saveKey = saveKey;
    }

    internal void Attach(UIPrefab_ControlSetting target)
    {
        if (screen != target || row == null)
        {
            Dispose();
            screen = target;
            row = UnityEngine.Object.Instantiate(target.UE_UIPrefab_KeybindListItem.gameObject, target.UE_Contents)
                .GetComponent<UIPrefab_KeybindListItem>();
            row.name = "MimesisEscapeKeybind";
            var toggle = row.UE_UI_Toggle_IconBox_KeyboardMouse;
            toggle.group = null;
            toggle.onValueChanged = new Toggle.ToggleEvent();
            toggle.onValueChanged.AddListener(on => { if (on) Listen(); });
            row.SetGamepadKey("", false);
            row.Show(_skipAnimation: true);
            // UIPrefabScript.Awake deactivates a newly cloned inactive prefab on its first activation.
            // Match the native CreateKeybindItem's second SetActive call.
            if (!row.gameObject.activeSelf) row.gameObject.SetActive(true);
        }
        var interaction = target.UE_Contents.GetComponentsInChildren<UIPrefab_KeybindListItem>()
            .FirstOrDefault(item => item != row && item.mapping?.action == Mimic.InputSystem.InputAction.Interact);
        // Keep the native sensitivity/inversion controls and keyboard/gamepad header above the bindings.
        row.transform.SetAsLastSibling();
        if (interaction != null) row.transform.SetSiblingIndex(interaction.transform.GetSiblingIndex() + 1);
        if (!waiting) Refresh();
    }

    private void Refresh()
    {
        if (row == null) return;
        row.ResetToggleState();
        SetInfoText(GameLocalization.Text.Title);
        string name = Keyboard.current == null ? currentKey().ToString() : Keyboard.current[currentKey()].name;
        row.SetKeyboardMouseKey("k_" + name, true);
    }

    private void Listen()
    {
        if (screen == null || row == null || Hub.s == null || Keyboard.current == null) return;
        ui = Hub.s.GetComponentInChildren<UIManager>();
        if (ui == null || ui.isChangingKeyBind) { row.ResetToggleState(); return; }
        ui.isChangingKeyBind = true;
        waiting = true;
        released = false;
        deadline = Time.realtimeSinceStartupAsDouble + 15;
        row.SetWaitingForInput(false, true);
        SetInfoText(GameLocalization.Text.Prompt);
    }

    internal void Tick()
    {
        if (screen != null && row != null && screen.gameObject.activeInHierarchy && displayedLanguage != GameLocalization.Language)
        {
            if (waiting) SetInfoText(GameLocalization.Text.Prompt);
            else Refresh();
        }
        if (!waiting) return;
        if (screen == null || !screen.gameObject.activeInHierarchy || !Application.isFocused ||
            Keyboard.current == null || Time.realtimeSinceStartupAsDouble >= deadline)
        {
            Cancel();
            return;
        }
        var keyboard = Keyboard.current;
        // Ignore Enter/Space activating the row and any key held before listening began.
        if (!released)
        {
            released = !keyboard.allKeys.Any(button => button.isPressed);
            return;
        }
        if (keyboard.escapeKey.wasPressedThisFrame) { Cancel(); return; }
        // A short tap may already be released in this frame; binding only needs its down transition.
        var pressed = keyboard.allKeys.FirstOrDefault(button => button.wasPressedThisFrame);
        if (pressed == null) return;
        var manager = Hub.s == null ? null : Hub.s.GetComponentInChildren<GameInputManager>();
        string conflict = CaptureAdapter.BindingConflict(manager, pressed.keyCode);
        if (conflict.Length > 0)
        {
            SetInfoText(string.Format(GameLocalization.Text.Conflict, pressed.keyCode));
            released = false;
            return;
        }
        try
        {
            saveKey(pressed.keyCode);
            Cancel();
        }
        catch (Exception)
        {
            SetInfoText(GameLocalization.Text.Error);
            released = false;
            throw;
        }
    }

    private void SetInfoText(string text)
    {
        if (row == null) return;
        row.SetInfoText(text);
        GameLocalization.ApplyDirection(row.UE_InfoText);
        displayedLanguage = GameLocalization.Language;
    }

    internal void Cancel()
    {
        if (!waiting) return;
        waiting = false;
        if (ui != null) ui.isChangingKeyBind = false;
        Refresh();
        EventSystem.current?.SetSelectedGameObject(null);
    }

    internal void Dispose()
    {
        Cancel();
        if (row != null) UnityEngine.Object.Destroy(row.gameObject);
        row = null;
        screen = null;
    }
}
