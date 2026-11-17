using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Mimic.Actors;
using ReluNetwork.ConstEnum;
using ReluProtocol;
using ReluProtocol.Enum;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MimesisEscape;

// A small opt-in exchange in the game's existing reliable AdminCommandReq envelope.
// No new serializer, socket, message type, dispatcher replacement or broadcast to unmodded players.
internal sealed class EscapeNetwork
{
    internal const string Command = "mimesisescape_v2";
    private static readonly FieldInfo Commands = AccessTools.Field(typeof(VWorld), "AdminCommandFuncs");
    private static readonly FieldInfo Slots = AccessTools.Field(typeof(AttachController), "_attachingActorIDs");
    private static readonly FieldInfo Creature = AccessTools.Field(typeof(AttachController), "_creature");
    private static readonly FieldInfo Info = AccessTools.Field(typeof(AttachController), "_attachInfo");
    private static readonly FieldInfo GrabHelper = AccessTools.Field(typeof(ProtoActor), "grabHelper");
    private static readonly FieldInfo GrabTarget = AccessTools.Field(GrabHelper.FieldType, "target");
    private static readonly FieldInfo GrabId = AccessTools.Field(GrabHelper.FieldType, "grabMasterID");
    private static readonly MethodInfo TimeGetter = AccessTools.PropertyGetter(typeof(Hub), "timeutil");
    private readonly Dictionary<int, HostCapture> tickets = new Dictionary<int, HostCapture>();
    private readonly Action<string> log;
    private readonly Action<double> startQte;
    private readonly Action<string> endQte;
    private readonly Func<double> hostSeconds;
    private VWorld? ticketWorld;
    private ClientCapture? client;
    private VWorld? registeredWorld;
    private VWorld.AdminCommandFunc? registeredHandler;
    private object? protectedDispatcher;
    private FieldInfo? preDispatchField;
    private OnPreDispatchEventHandler? originalPreDispatch, guardedPreDispatch;
    private readonly ConditionalWeakTable<object, RequestBudget> budgets = new ConditionalWeakTable<object, RequestBudget>();
    private readonly Stopwatch ingressClock = Stopwatch.StartNew();

    private sealed class HostCapture
    {
        internal VPlayer Player = null!;
        internal VCreature Caster = null!;
        internal AttachController Controller = null!;
        internal AttachMasterInfo Info = null!;
        internal VCreature Owner = null!;
        internal VActor Attached = null!;
        internal ReleaseTicket Ticket = null!;
    }

    private sealed class ClientCapture
    {
        internal GameMainBase Main = null!;
        internal ProtoActor Avatar = null!, Caster = null!;
        internal ProtoActor Owner = null!, Attached = null!;
        internal int GrabId;
        internal Guid Request = Guid.NewGuid(), Ticket;
        internal double ObservedAt, RequestedAt;
        internal bool Requested, Started, Finished, ReleaseSent, Detached;
    }

    internal EscapeNetwork(Action<string> log, Action<double> startQte, Action<string> endQte, Func<double> hostSeconds)
    { this.log = log; this.startQte = startQte; this.endQte = endQte; this.hostSeconds = hostSeconds; }

    internal bool HasClientCapture => client?.Started == true && !client.Finished;

    internal void ProtectDispatcher(VWorld world)
    {
        var manager = AccessTools.Field(typeof(VWorld), "_serverDispatchers").GetValue(world);
        var map = (Dictionary<MsgType, IDispatcher>)AccessTools.Field(typeof(ServerDispatchManager), "_messageDispatcherMap").GetValue(manager);
        protectedDispatcher = map[MsgType.C2S_AdminCommandReq];
        preDispatchField = AccessTools.Field(protectedDispatcher.GetType(), "PreDispatchEventHandler");
        originalPreDispatch = (OnPreDispatchEventHandler?)preDispatchField.GetValue(protectedDispatcher);
        var previous = originalPreDispatch;
        guardedPreDispatch = (msg, context) =>
        {
            if (msg is AdminCommandReq request && request.command == Command &&
                (context == null || !budgets.GetValue(context, _ => new RequestBudget()).Take(ingressClock.ElapsedMilliseconds) ||
                 !QteWire.Valid(request.args, true))) return false;
            return previous?.Invoke(msg, context) ?? true;
        };
        preDispatchField.SetValue(protectedDispatcher, guardedPreDispatch);
    }

    internal void Register(VWorld world)
    {
        var commands = (Dictionary<string, VWorld.AdminCommandFunc>)Commands.GetValue(world);
        if (commands.ContainsKey(Command)) throw new InvalidOperationException("QTE network command already registered.");
        Unregister();
        registeredWorld = world;
        registeredHandler = (player, args) => HandleHost(world, player, args);
        commands.Add(Command, registeredHandler);
        log("Host QTE network handler registered (protocol 2).");
    }

    internal void ObserveHostAttach(AttachController controller, int victimId)
    {
        var world = CaptureAdapter.World;
        if (world == null || CaptureAdapter.Pdata?.ClientMode != NetworkClientMode.Host) return;
        if (ticketWorld != world) { tickets.Clear(); ticketWorld = world; }
        var owner = Creature.GetValue(controller) as VCreature;
        var info = Info.GetValue(controller) as AttachMasterInfo;
        var attached = owner?.VRoom.FindActorByObjectID(victimId);
        var player = (info?.IsReverseGrab == true ? owner : attached) as VPlayer;
        var caster = (info?.IsReverseGrab == true ? attached : owner) as VCreature;
        if (player != null) tickets.Remove(player.ObjectID);
        if (owner == null || attached == null || caster == null || player == null || player.IsHost || info == null ||
            !CaptureKinds.Matches(info.MasterID, info.IsReverseGrab, caster.MasterID, caster.GetCreatureAIData()?.IsMimic() == true) ||
            info.Duration <= 0 || !TryStart(controller, attached.ObjectID, out long start)) return;
        long window = (long)Math.Min(info.Duration, hostSeconds() * 1000);
        if (window <= 0) return;
        tickets[player.ObjectID] = new HostCapture
        {
            Player = player, Caster = caster, Owner = owner, Attached = attached, Controller = controller, Info = info,
            Ticket = new ReleaseTicket(player.ObjectID, caster.ObjectID, start,
                start + window)
        };
    }

    private static bool TryStart(AttachController controller, int victim, out long start)
    {
        start = 0;
        var slots = Slots.GetValue(controller) as Dictionary<int, (int targetActorID, long attachTime)?>;
        if (slots == null) return false;
        foreach (var slot in slots.Values)
            if (slot.HasValue && slot.Value.targetActorID == victim) { start = slot.Value.attachTime; return true; }
        return false;
    }

    private bool Valid(HostCapture state, out long nativeStart)
    {
        nativeStart = 0;
        return CaptureAdapter.World == ticketWorld && CaptureAdapter.Pdata?.SessionJoined == true &&
            CaptureAdapter.Pdata.ClientMode == NetworkClientMode.Host && !state.Caster.VRoom.IsSessionEnding &&
            ReferenceEquals(state.Caster.VRoom.FindActorByObjectID(state.Player.ObjectID), state.Player) &&
            state.Player.IsAliveStatus() && state.Caster.IsAliveStatus() &&
            ReferenceEquals(Info.GetValue(state.Controller), state.Info) && state.Controller.IsAttaching() &&
            ReferenceEquals(state.Owner.AttachControlUnit, state.Controller) &&
            state.Attached.AttachControlUnit?.IsAttached() == true &&
            state.Attached.AttachControlUnit.AttachedActorID == state.Owner.ObjectID &&
            TryStart(state.Controller, state.Attached.ObjectID, out nativeStart) && nativeStart == state.Ticket.NativeStart;
    }

    private MsgErrorCode HandleHost(VWorld world, VPlayer sender, AdminCommandArgs args)
    {
        try
        {
            // sender comes from SessionContext._vPlayer, never from a client-supplied player ID.
            if (world != CaptureAdapter.World || !tickets.TryGetValue(sender.ObjectID, out var state) ||
                !ReferenceEquals(sender, state.Player) || !Valid(state, out long nativeStart))
                return Reject(sender, args);
            long now = Now();
            string? operation = args["op"];
            if (operation == "begin" && Guid.TryParseExact(args["request"], "N", out var request) &&
                int.TryParse(args["caster"], out int caster) && caster == state.Caster.ObjectID && state.Ticket.Remaining(now) > 0)
            {
                sender.SendToMe(Message("op=grant^request=" + request.ToString("N") + "^token=" + state.Ticket.Token.ToString("N") +
                    "^ms=" + state.Ticket.Remaining(now).ToString(CultureInfo.InvariantCulture)));
                return MsgErrorCode.Success;
            }
            if (operation != "release" || !Guid.TryParseExact(args["token"], "N", out var token) ||
                !state.Ticket.TryConsume(token, sender.ObjectID, state.Caster.ObjectID, nativeStart, now, true))
                return Reject(sender, args);
            bool released = state.Controller.DetachByRequest(sender.ObjectID, state.Attached.ObjectID,
                state.Info.IsReverseGrab ? DetachReason.ActiveByCaster : DetachReason.ActiveByVictim) &&
                !state.Attached.AttachControlUnit!.IsAttached();
            tickets.Remove(sender.ObjectID);
            sender.SendToMe(Message("op=result^token=" + token.ToString("N") + "^ok=" + (released ? "1" : "0")));
            log(released ? "Client QTE: native ActiveByVictim release completed on host." : "Client QTE: native release rejected; no retry.");
            return released ? MsgErrorCode.Success : MsgErrorCode.InvalidAdminCommandArgument;
        }
        catch (Exception ex) { log("Client QTE request rejected: " + ex.GetType().Name + "."); return MsgErrorCode.InvalidAdminCommandArgument; }
    }

    private static AdminCommandReq Message(string args) => new AdminCommandReq { command = Command, args = args };
    private static long Now() => ((TimeUtil)TimeGetter.Invoke(Hub.s, null)).GetCurrentTickMilliSec();

    private static MsgErrorCode Reject(VPlayer sender, AdminCommandArgs args)
    {
        if (args["op"] == "release" && Guid.TryParseExact(args["token"], "N", out var token))
            sender.SendToMe(Message("op=result^token=" + token.ToString("N") + "^ok=0"));
        return MsgErrorCode.InvalidAdminCommandArgument;
    }

    internal void ObserveClientAttach(ProtoActor caster, AttachActorSig sig)
    {
        var main = Hub.Main;
        var local = main?.GetMyAvatar();
        if (local == null || local.IsHost || !local.AmIAvatar() || sig.actorID != caster.ActorID)
            return;
        if (sig.state == AttachState.Detached)
        {
            if (client?.Owner == caster && client.Attached.ActorID == sig.targetID)
            {
                client.Detached = true;
                if (client.ReleaseSent) log("Client QTE: native detach observed after release request.");
            }
            return;
        }
        if (sig.state != AttachState.Attached || !CaptureKinds.Supported(sig.grabMasterID)) return;
        bool reverse = CaptureKinds.Reverse(sig.grabMasterID);
        if ((reverse ? sig.actorID : sig.targetID) != local.ActorID) return;
        var attached = main!.GetActorByActorID(sig.targetID);
        var monster = reverse ? attached : caster;
        if (attached == null || monster == null ||
            !CaptureKinds.Matches(sig.grabMasterID, reverse, monster.monsterMasterID, monster.IsMimic())) return;
        if (client != null && client.Main == main && client.Owner == caster && client.Attached == attached && !client.Detached) return;
        endQte("new client capture");
        client = new ClientCapture { Main = main, Avatar = local, Caster = monster, Owner = caster,
            Attached = attached, GrabId = sig.grabMasterID, ObservedAt = Time.realtimeSinceStartupAsDouble };
    }

    private static bool VisualMatches(ClientCapture state)
    {
        if (state.Avatar == null || state.Caster == null || state.Owner == null || state.Attached == null ||
            state.Avatar.dead || state.Caster.dead || !state.Attached.IsGrabbedByOther) return false;
        object helper = GrabHelper.GetValue(state.Owner);
        return helper != null && ReferenceEquals(GrabTarget.GetValue(helper), state.Attached) &&
            (int)GrabId.GetValue(helper) == state.GrabId;
    }

    internal string InvalidClientReason()
    {
        if (client == null || client.Detached) return "native or external release";
        if (Hub.Main != client.Main || CaptureAdapter.Pdata?.SessionJoined != true ||
            CaptureAdapter.Pdata.ClientMode == NetworkClientMode.Host || client.Main.GetMyAvatar() != client.Avatar ||
            !client.Avatar.AmIAvatar()) return "scene/session/role changed";
        return VisualMatches(client) ? "" : "native release or death";
    }

    internal void Tick(Key key)
    {
        if (ticketWorld != CaptureAdapter.World) { tickets.Clear(); ticketWorld = CaptureAdapter.World; }
        foreach (int id in tickets.Where(x => !Valid(x.Value, out _) ||
            x.Value.Ticket.Remaining(Now()) == 0)
            .Select(x => x.Key).ToArray()) tickets.Remove(id);
        var state = client;
        if (state == null || state.Finished) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (Hub.Main != state.Main || CaptureAdapter.Pdata?.SessionJoined != true || state.Avatar == null || state.Avatar.dead)
        { state.Finished = true; return; }
        if (now - state.ObservedAt >= 30 && !state.Started)
        {
            state.Finished = true;
            log("Client QTE unavailable: no valid grant from host before native capture expired. Host needs the same mod version.");
            return;
        }
        if (state.Requested || !VisualMatches(state)) return;
        string conflict = CaptureAdapter.CurrentBindingConflict(key);
        if (conflict.Length > 0 || !Application.isFocused)
        { state.Finished = true; log("Client QTE skipped: input unavailable or key conflict: " + conflict + "."); return; }
        state.Requested = true;
        state.RequestedAt = now;
        state.Main.SendPacket(Message("op=begin^request=" + state.Request.ToString("N") + "^caster=" + state.Caster.ActorID));
    }

    // Called only from NetworkManagerV2.TryDequeueMsg on the native main-thread packet pump.
    internal void Receive(IMsg packet)
    {
        if (!(packet is AdminCommandReq msg) || msg.command != Command || !QteWire.Valid(msg.args, false) ||
            client == null || Hub.Main != client.Main || client.Avatar == null || client.Avatar.IsHost) return;
        var args = new AdminCommandArgs(msg.args);
        var state = client;
        if (args["op"] == "grant" && !state.Finished && state.Requested && !state.Started &&
            Guid.TryParseExact(args["request"], "N", out var request) && request == state.Request &&
            Guid.TryParseExact(args["token"], "N", out var token) && token != Guid.Empty &&
            long.TryParse(args["ms"], out long ms) && ms > 0 && ms <= 30000 && InvalidClientReason().Length == 0)
        {
            double window = ReleaseTicket.ClientWindow(ms / 1000.0, state.RequestedAt, Time.realtimeSinceStartupAsDouble);
            if (window <= 0) { state.Finished = true; return; }
            state.Ticket = token; state.Started = true;
            startQte(window);
            log("Capture started: grab " + state.GrabId + "; role=client; host granted a one-use release ticket.");
        }
        else if (args["op"] == "result" && state.ReleaseSent &&
            Guid.TryParseExact(args["token"], "N", out var responseToken) && responseToken == state.Ticket)
            log(args["ok"] == "1" ? "Client QTE: host confirmed native release." : "Client QTE: host rejected release; no retry.");
    }

    internal bool RequestRelease()
    {
        var state = client;
        if (state == null || !HasClientCapture || state.ReleaseSent || InvalidClientReason().Length > 0) return false;
        state.ReleaseSent = true; state.Finished = true;
        state.Main.SendPacket(Message("op=release^token=" + state.Ticket.ToString("N")));
        return true;
    }

    internal void CancelLocal() { if (client != null) client.Finished = true; }
    internal void Reset() { client = null; tickets.Clear(); }
    internal void Dispose() { Reset(); Unregister(); }

    private void Unregister()
    {
        if (protectedDispatcher != null && Equals(preDispatchField!.GetValue(protectedDispatcher), guardedPreDispatch))
            preDispatchField.SetValue(protectedDispatcher, originalPreDispatch);
        protectedDispatcher = null; originalPreDispatch = null; guardedPreDispatch = null; preDispatchField = null;
        if (registeredWorld != null)
        {
            var commands = (Dictionary<string, VWorld.AdminCommandFunc>)Commands.GetValue(registeredWorld);
            if (commands.TryGetValue(Command, out var handler) && handler == registeredHandler) commands.Remove(Command);
        }
        registeredWorld = null; registeredHandler = null;
    }
}
