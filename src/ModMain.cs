using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HarmonyLib;
using MelonLoader;
using Mimic.Actors;
using ReluProtocol;
using Steamworks;
using UnityEngine;
using UnityEngine.InputSystem;
using GameInputManager = Mimic.InputSystem.InputManager;

[assembly: MelonInfo(typeof(MimesisEscape.EscapeMod), "Mimesis Escape QTE", "0.2.1", "Local mod")]
[assembly: MelonGame("ReLUGames", "MIMESIS")]

namespace MimesisEscape;

public sealed class EscapeMod : MelonMod
{
    private static EscapeMod? instance;
    private readonly QteSession qte = new QteSession();
    private readonly QteHud hud = new QteHud();
    private CaptureAdapter? capture;
    private EscapeNetwork? network;
    private bool clientAttempt;
    private KeybindSettings? keybindSettings;
    private Key key = Key.F6;
    private int required = 5;
    private double seconds = 3;
    private long captureNumber;
    private bool ready;
    private bool reportedRole;
    private bool reportedSteam;
    private bool reportedBindings;
    private bool show;
    private void Log(string message) => LoggerInstance.Msg(message);

    public override void OnInitializeMelon()
    {
        try
        {
            VerifyFile("Managed/Assembly-CSharp.dll", "4a4d9e97a2061611989309e89227857cdae0b540940489d9ed2dee017e3f47ae");
            VerifyFile("StreamingAssets/masterdata/GrabData.json", "f4ec14e623f6ee6ade39a36430049b6e9ad898ec836c0fbfd78887c1955b29cf");
            VerifyFile("StreamingAssets/masterdata/AbnormalData.json", "9f301233cfc69b119e20bdb072747e64065ff1cb3be24a8fe83c1f0f72f51d75");
            VerifyFile("StreamingAssets/masterdata/MonsterSkillSequenceData.json", "477d83cc9336dad97deacdc29c8b3bb4c657f3a2fe1e7762dcf3ce44533ae206");
            ReadConfig();
            capture = new CaptureAdapter(Log);
            network = new EscapeNetwork(Log, BeginClientCapture, EndCapture, () => seconds);
            keybindSettings = new KeybindSettings(() => key, SaveKey);
            instance = this;
            HarmonyInstance.Patch(AccessTools.Method(typeof(AttachController), nameof(AttachController.AttachBySkill),
                new[] { typeof(int), typeof(int) }), postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterAttach)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(UIPrefab_ControlSetting), "RefreshKeybindItems"),
                postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterKeybindRefresh)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(VWorld), nameof(VWorld.RegisterCommand)),
                postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterRegisterCommands)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(VWorld), nameof(VWorld.RegisterAdminProtocol)),
                postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterRegisterAdminProtocol)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(ProtoActor), "OnPacket", new[] { typeof(AttachActorSig) }),
                postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterAttachPacket)));
            HarmonyInstance.Patch(AccessTools.Method(typeof(NetworkManagerV2), nameof(NetworkManagerV2.TryDequeueMsg)),
                postfix: new HarmonyMethod(typeof(EscapeMod), nameof(AfterDequeue)));
            ready = true;
            Application.focusChanged += OnFocusChanged;
            Log($"Loaded for Steam build 26447295 / game {Application.version} / Unity {Application.unityVersion}; " +
                $"Mimesis grab 30001 only; host + opt-in clients (same mod required on host); {required} separate {key} presses / {seconds.ToString(CultureInfo.InvariantCulture)} s. Hashes verified.");
        }
        catch (Exception ex) { Disable(ex); }
    }

    private static void AfterKeybindRefresh(UIPrefab_ControlSetting __instance)
    {
        if (instance?.ready != true) return;
        try { instance.keybindSettings!.Attach(__instance); }
        catch (Exception ex) { instance.LoggerInstance.Error("Key settings UI: " + ex.Message); }
    }

    private static void AfterAttach(AttachController __instance, int grabMasterID, int attachedActorID, bool __result)
    {
        if (!__result || grabMasterID != CaptureAdapter.GrabId || instance?.ready != true) return;
        try
        {
            instance.network!.ObserveHostAttach(__instance, attachedActorID);
            instance.BeginCapture(__instance, attachedActorID);
        }
        catch (Exception ex) { instance.Disable(ex); }
    }

    private static void AfterRegisterCommands(VWorld __instance)
    {
        if (instance?.ready != true) return;
        try { instance.network!.Register(__instance); }
        catch (Exception ex) { instance.Disable(ex); }
    }

    private static void AfterRegisterAdminProtocol(VWorld __instance)
    {
        if (instance?.ready != true) return;
        try { instance.network!.ProtectDispatcher(__instance); }
        catch (Exception ex) { instance.Disable(ex); }
    }

    private static void AfterAttachPacket(ProtoActor __instance, AttachActorSig sig)
    {
        if (instance?.ready != true) return;
        try { instance.network!.ObserveClientAttach(__instance, sig); }
        catch (Exception ex) { instance.Disable(ex); }
    }

    private static void AfterDequeue(bool __result, (bool, IMsg) msgPair)
    {
        if (!__result || instance?.ready != true) return;
        try { instance.network!.Receive(msgPair.Item2); }
        catch (Exception ex) { instance.Disable(ex); }
    }

    private void BeginClientCapture(double grantedSeconds)
    {
        qte.Cancel();
        clientAttempt = true;
        qte.Begin(++captureNumber, Time.realtimeSinceStartupAsDouble, required, Math.Min(seconds, grantedSeconds),
            Keyboard.current == null || Keyboard.current[key].isPressed);
    }

    private void BeginCapture(AttachController controller, int victim)
    {
        if (!capture!.Begin(controller, victim, key, out double nativeDuration)) return;
        // Start from the native attach boundary, never increase the native duration or pause its clock.
        double window = Math.Min(seconds, nativeDuration);
        qte.Begin(++captureNumber, Time.realtimeSinceStartupAsDouble, required, window,
            Keyboard.current == null || Keyboard.current[key].isPressed);
        Log($"Capture started: Mimesis/30001; QTE window={window.ToString("0.###", CultureInfo.InvariantCulture)} s; native={nativeDuration:0.###} s; role=host.");
    }

    public override void OnLateUpdate()
    {
        if (!ready) return;
        try
        {
            try { keybindSettings!.Tick(); }
            catch (Exception ex) { LoggerInstance.Error("Key binding was not saved: " + ex.Message); }
            if (!reportedSteam)
            {
                try
                {
                    int build = SteamApps.GetAppBuildId();
                    bool beta = SteamApps.GetCurrentBetaName(out string branch, 128);
                    Log($"Steam identity: build={build}; branch={(beta ? branch : "public (no beta)")}.");
                    reportedSteam = true;
                    if (build != 26447295) throw new NotSupportedException("Steam build changed; mod disabled pending audit.");
                }
                catch (InvalidOperationException) { /* Steam may not have initialized yet. */ }
            }
            var local = Hub.Main?.GetMyAvatar();
            if (!reportedBindings && Hub.s != null)
            {
                var input = Hub.s.GetComponentInChildren<GameInputManager>();
                if (input?.data?.data?.Count > 0 && Keyboard.current != null)
                {
                    Log($"Input audit: E={CaptureAdapter.BindingConflict(input, Key.E)}; F6={CaptureAdapter.BindingConflict(input, Key.F6)}; configured={key}.");
                    reportedBindings = true;
                }
            }
            network!.Tick(key);
            if (local == null) reportedRole = false;
            else if (!reportedRole && CaptureAdapter.Pdata?.SessionJoined == true)
            {
                reportedRole = true;
                bool host = local.IsHost;
                Log(host ? "Role=host. QTE enabled for local Mimesis grabs." :
                    "Role=client. QTE requires a per-capture grant from a host running the same mod; unmodded players are unaffected.");
            }
            if (!clientAttempt && !capture!.HasCapture) { show = false; hud.Hide(); return; }
            string reason = clientAttempt ? network.InvalidClientReason() : capture!.InvalidReason();
            if (reason.Length > 0) { EndCapture(reason); return; }
            if (!Application.isFocused) { EndCapture("focus lost"); return; }
            bool allowed = CaptureAdapter.AllowsPlayerInput(key);
            var button = Keyboard.current?[key];
            var previous = qte.State;
            bool success = qte.Tick(Time.realtimeSinceStartupAsDouble, button?.isPressed == true,
                button?.wasPressedThisFrame == true, allowed);
            show = qte.State == QteState.Active && allowed;
            if (success)
            {
                // State already latched Succeeded before entering the game method; no retry on rejection/error.
                if (clientAttempt)
                {
                    bool sent = network.RequestRelease();
                    Log(sent ? "QTE succeeded: release requested from host; awaiting native confirmation." :
                        "QTE succeeded but capture is no longer valid; no release request or retry.");
                    clientAttempt = false;
                }
                else
                {
                    bool released = capture!.Release();
                    Log(released ? "QTE succeeded: native ActiveByVictim release completed on host." :
                        "QTE succeeded but native release was rejected; no retry or fallback mutations.");
                    capture.Clear();
                }
                show = false;
            }
            else if (previous == QteState.Active && qte.State == QteState.Failed)
                Log("QTE timed out; native grab continues unchanged.");
            if (show)
                hud.Show(Hub.Main, $"Освобождение  [{key}]   {qte.Progress}/{qte.Required}   {qte.Remaining(Time.realtimeSinceStartupAsDouble):0.0} с");
            else hud.Hide();
        }
        catch (Exception ex) { Disable(ex); }
    }

    private void EndCapture(string reason)
    {
        if (capture?.HasCapture == true || clientAttempt)
        {
            if (qte.State == QteState.Active) Log("QTE cancelled: " + reason + ".");
            qte.Cancel(); capture?.Clear();
        }
        network?.CancelLocal();
        clientAttempt = false;
        show = false;
        hud.Hide();
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        keybindSettings?.Cancel();
        EndCapture("scene loaded");
        network?.Reset();
        hud.Dispose();
    }
    public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
    {
        keybindSettings?.Cancel();
        EndCapture("scene unloaded");
        network?.Reset();
        hud.Dispose();
    }
    public override void OnApplicationQuit() => EndCapture("application quit");
    private void OnFocusChanged(bool focused)
    {
        if (focused) return;
        keybindSettings?.Cancel();
        EndCapture("focus lost");
    }
    public override void OnDeinitializeMelon()
    {
        ready = false;
        Application.focusChanged -= OnFocusChanged;
        keybindSettings?.Dispose();
        EndCapture("mod unloaded");
        network?.Dispose();
        hud.Dispose();
        HarmonyInstance.UnpatchSelf();
        instance = null;
    }

    private void Disable(Exception ex)
    {
        ready = false;
        keybindSettings?.Cancel();
        EndCapture("error");
        network?.Dispose();
        LoggerInstance.Error("QTE disabled: " + ex.GetType().Name + ": " + ex.Message);
    }

    private static void VerifyFile(string relative, string expected)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(Path.Combine(Application.dataPath, relative));
        string actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        if (actual != expected) throw new NotSupportedException("Unaudited game file: " + relative);
    }

    private static string ConfigPath => Path.Combine(Path.GetDirectoryName(Application.dataPath)!, "UserData", "MimesisEscape.cfg");

    private void SaveKey(Key selected)
    {
        string path = ConfigPath;
        var lines = File.ReadAllLines(path).ToList();
        bool replaced = false;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].TrimStart().StartsWith("#") || lines[i].Split('=')[0].Trim() != "Key") continue;
            lines[i] = "Key=" + selected;
            replaced = true;
        }
        if (!replaced) lines.Add("Key=" + selected);
        File.WriteAllLines(path + ".tmp", lines);
        File.Replace(path + ".tmp", path, null);
        EndCapture("key binding changed");
        key = selected;
        Log("QTE key saved from settings: " + selected + ". Applies immediately.");
    }

    private void ReadConfig()
    {
        string path = ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) File.WriteAllText(path, "# Restart game after editing. Unity Input System Key enum.\nKey=F6\nPresses=5\nSeconds=3\n");
        foreach (string line in File.ReadAllLines(path))
        {
            string text = line.Trim();
            if (text.Length == 0 || text.StartsWith("#")) continue;
            string[] entry = text.Split(new[] { '=' }, 2);
            if (entry.Length != 2) throw new FormatException("Expected Name=Value in MimesisEscape.cfg.");
            string value = entry[1].Trim();
            switch (entry[0].Trim())
            {
                case "Key":
                    if (!Enum.TryParse(value, true, out key) || !Enum.IsDefined(typeof(Key), key) || key == Key.None)
                        throw new FormatException("Unknown Unity Input System key in config; value omitted.");
                    break;
                case "Presses":
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out required))
                        throw new FormatException("Invalid Presses in config; value omitted.");
                    break;
                case "Seconds":
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds))
                        throw new FormatException("Invalid Seconds in config; value omitted.");
                    break;
                default: throw new FormatException("Unknown config entry; name and value omitted.");
            }
        }
        if (required < 1 || required > 100 || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0 || seconds > 30)
            throw new FormatException("Presses must be 1..100 and Seconds must be >0..30.");
    }
}
