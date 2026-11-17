using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Mimic.Actors;
using Mimic.InputSystem;
using ReluNetwork.ConstEnum;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using GameInputManager = Mimic.InputSystem.InputManager;

namespace MimesisEscape;

// Build-specific bridge to the local host's authoritative grab. Client exchange is in EscapeNetwork.
internal sealed class CaptureAdapter
{
    private static readonly FieldInfo CreatureField = AccessTools.Field(typeof(AttachController), "_creature");
    private static readonly FieldInfo InfoField = AccessTools.Field(typeof(AttachController), "_attachInfo");
    private static readonly FieldInfo PdataField = AccessTools.Field(typeof(Hub), "pdata");
    private static readonly MethodInfo WorldGetter = AccessTools.PropertyGetter(typeof(Hub), "vworld");
    private static readonly FieldInfo StatesField = AccessTools.Field(typeof(GameInputManager), "states");
    private readonly Action<string> log;
    private GameMainBase? main;
    private VWorld? world;
    private VCreature? caster;
    private VActor? victim;
    private ProtoActor? avatar;
    private AttachController? controller;
    private AttachMasterInfo? info;
    private VCreature? owner;
    private VActor? attached;

    internal CaptureAdapter(Action<string> log) => this.log = log;
    internal bool HasCapture => controller != null;
    internal int CurrentGrabId => info?.MasterID ?? 0;

    internal static Hub.PersistentData? Pdata => Hub.s == null ? null : PdataField.GetValue(Hub.s) as Hub.PersistentData;
    internal static VWorld? World => Hub.s == null ? null : WorldGetter.Invoke(Hub.s, null) as VWorld;

    internal bool Begin(AttachController source, int targetId, Key key, out double duration)
    {
        duration = 0;
        if (HasCapture || Pdata?.ClientMode != NetworkClientMode.Host || !Pdata.SessionJoined || World == null)
            return false;
        var currentMain = Hub.Main;
        var local = currentMain?.GetMyAvatar();
        var actor = CreatureField.GetValue(source) as VCreature;
        var grabInfo = InfoField.GetValue(source) as AttachMasterInfo;
        if (local == null || !local.IsHost || !local.AmIAvatar() || local.dead || actor == null || grabInfo == null ||
            !CaptureKinds.Supported(grabInfo.MasterID))
            return false;
        var target = actor.VRoom.FindActorByObjectID(targetId);
        var player = (grabInfo.IsReverseGrab ? actor : target) as VPlayer;
        var grabber = (grabInfo.IsReverseGrab ? target : actor) as VCreature;
        var visualCaster = grabber == null ? null : currentMain!.GetActorByActorID(grabber.ObjectID);
        if (player == null || grabber == null || visualCaster == null || player.ObjectID != local.ActorID ||
            !CaptureKinds.Matches(grabInfo.MasterID, grabInfo.IsReverseGrab, grabber.MasterID, visualCaster.IsMimic()) ||
            !player.IsAliveStatus() || !grabber.IsAliveStatus() || target?.AttachControlUnit == null ||
            target.AttachControlUnit.AttachedActorID != actor.ObjectID || !target.AttachControlUnit.IsAttached() ||
            !source.IsAttaching() || grabInfo.Duration <= 0)
            return false;
        var input = Hub.s.GetComponentInChildren<GameInputManager>();
        string conflict = BindingConflict(input, key);
        if (conflict.Length > 0)
        {
            log($"QTE skipped: key {key} conflicts with {conflict}. Set an unused key in UserData/MimesisEscape.cfg.");
            return false;
        }
        main = currentMain;
        world = World;
        caster = grabber;
        victim = player;
        avatar = local;
        controller = source;
        info = grabInfo;
        owner = actor;
        attached = target;
        duration = grabInfo.Duration / 1000.0;
        return true;
    }

    internal string InvalidReason()
    {
        if (!HasCapture) return "no capture";
        if (Hub.Main != main || World != world || Pdata?.SessionJoined != true ||
            Pdata.ClientMode != NetworkClientMode.Host) return "scene/session/role changed";
        if (avatar == null || main!.GetMyAvatar() != avatar || !avatar.IsHost || !avatar.AmIAvatar())
            return "local player changed";
        if (avatar.dead || !victim!.IsAliveStatus()) return "player died";
        if (!caster!.IsAliveStatus()) return "grabber died";
        if (caster.VRoom.IsSessionEnding || !ReferenceEquals(caster.VRoom.FindActorByObjectID(victim.ObjectID), victim))
            return "room ended or player removed";
        if (!ReferenceEquals(InfoField.GetValue(controller), info) || !controller!.IsAttaching() ||
            attached?.AttachControlUnit?.IsAttached() != true || attached.AttachControlUnit.AttachedActorID != owner!.ObjectID ||
            !ReferenceEquals(owner.AttachControlUnit, controller))
            return "native or external release";
        return "";
    }

    internal static string CurrentBindingConflict(Key key) => BindingConflict(
        Hub.s == null ? null : Hub.s.GetComponentInChildren<GameInputManager>(), key);

    internal static bool AllowsPlayerInput(Key key)
    {
        var input = Hub.s == null ? null : Hub.s.GetComponentInChildren<GameInputManager>();
        var ui = Hub.s == null ? null : Hub.s.GetComponentInChildren<UIManager>();
        var console = Hub.s == null ? null : Hub.s.GetComponentInChildren<DebugConsole>();
        if (!Application.isFocused || Keyboard.current == null || input == null || ui == null || console == null ||
            !input.IsCapturing() || ui.isGameMenuOpen || ui.isChangingKeyBind || console.IsConsoleActive() ||
            BindingConflict(input, key).Length > 0) return false;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && (selected.GetComponentInParent<TMP_InputField>() != null ||
            selected.GetComponentInParent<UnityEngine.UI.InputField>() != null)) return false;
        // Cursor capture/menu state covers game dialogs; also reject focused fields outside the selected object.
        return !UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsSortMode.None).Any(x => x.isFocused) &&
            !UnityEngine.Object.FindObjectsByType<UnityEngine.UI.InputField>(FindObjectsSortMode.None).Any(x => x.isFocused);
    }

    internal bool Release()
    {
        if (InvalidReason().Length > 0) return false;
        // Matches MMBehaviorTree.DetachActor's existing victim-driven branch, including all native cleanup.
        bool accepted = controller!.DetachByRequest(victim!.ObjectID, attached!.ObjectID,
            info!.IsReverseGrab ? DetachReason.ActiveByCaster : DetachReason.ActiveByVictim);
        return accepted && !attached.AttachControlUnit!.IsAttached();
    }

    internal void Clear()
    {
        main = null; world = null; caster = null; victim = null; avatar = null;
        controller = null; info = null; owner = null; attached = null;
    }

    internal static string BindingConflict(GameInputManager? manager, Key key)
    {
        if (manager == null || Keyboard.current == null ||
            !(StatesField.GetValue(manager) is Dictionary<Mimic.InputSystem.InputAction, GameInputManager.ActionState> states) ||
            states.Count == 0) return "unavailable input bindings";
        var control = Keyboard.current[key];
        return string.Join(", ", states.Where(entry => entry.Value.assignedKeys.Contains(control))
            .Select(entry => entry.Key.ToString()).Distinct());
    }
}
