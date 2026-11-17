using System;
using MimesisEscape;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
    Console.WriteLine("PASS " + name);
}
string[] languages = { "en", "ko", "es", "ja", "fr", "de", "zh_cn", "zh_tw", "pt_br", "it", "ru", "uk", "vi", "th", "tr", "pl", "ar", "da" };
foreach (string language in languages)
{
    var text = EscapeTranslations.Get(language);
    Check(!string.IsNullOrWhiteSpace(text.Title) && !string.IsNullOrWhiteSpace(text.Prompt) &&
        !string.IsNullOrWhiteSpace(text.Error) && !string.IsNullOrWhiteSpace(text.Hud) &&
        !string.IsNullOrWhiteSpace(text.Seconds) && text.Conflict.Contains("{0}") &&
        string.Format(text.Conflict, "F6").Contains("F6"), "complete translation: " + language);
}
Check(EscapeTranslations.Get(null).Title == EscapeTranslations.Get("en").Title &&
    EscapeTranslations.Get("unknown").Title == EscapeTranslations.Get("en").Title, "unknown language falls back to English");
Check(EscapeTranslations.Get("KO").Title == EscapeTranslations.Get("ko").Title &&
    EscapeTranslations.Get("zh_cn").Title != EscapeTranslations.Get("zh_tw").Title, "language matching and Chinese variants");
QteSession Start(int presses = 5, bool held = false)
{
    var session = new QteSession();
    session.Begin(1, 0, presses, 3, held);
    return session;
}
void Press(QteSession session, double time)
{
    session.Tick(time, false, false, true);
    session.Tick(time + .01, true, true, true);
}

var success = Start();
for (int i = 0; i < 4; i++) Press(success, i * .2);
Check(success.Progress == 4 && success.State == QteState.Active, "four presses are insufficient");
success.Tick(1, false, false, true);
Check(success.Tick(1.1, true, true, true) && success.State == QteState.Succeeded, "five separate presses succeed");
Check(!success.Tick(1.2, true, true, true), "release trigger occurs exactly once");
Check(!success.Begin(1, 1.3, 5, 3, false), "same capture cannot restart after success");
Check(success.Begin(2, 4, 5, 3, false) && success.Progress == 0, "new capture resets progress");

var repeat = Start();
repeat.Tick(.1, true, true, true);
for (int i = 0; i < 25; i++) repeat.Tick(.2 + i * .02, true, true, true);
Check(repeat.Progress == 1, "holding and repeated down notifications count once");
var held = Start(1, true);
held.Tick(.1, true, true, true);
Check(held.Progress == 0, "key held before capture is ignored");
Press(held, .2);
Check(held.State == QteState.Succeeded, "release rearms held-at-start key");

var blocked = Start();
blocked.Tick(.1, true, true, false);
blocked.Tick(.2, true, true, true);
Check(blocked.Progress == 0, "chat/menu/focus block also disarms held input");
Press(blocked, .3);
Check(blocked.Progress == 1, "fresh press after closing input UI counts");
var blockRelease = Start();
blockRelease.Tick(.1, false, false, false);
blockRelease.Tick(.2, true, true, true);
Check(blockRelease.Progress == 0, "blocked release does not arm on return");
var synthetic = Start();
synthetic.Tick(.1, true, false, true);
Check(synthetic.Progress == 0, "pressed-state without a down transition is ignored");
synthetic.Tick(.2, true, true, true);
Check(synthetic.Progress == 0, "repeat after an unobserved initial down cannot become a press");

var timeout = Start(1);
Check(!timeout.Tick(3, true, true, true) && timeout.State == QteState.Failed, "deadline wins over simultaneous press");
Check(!timeout.Begin(1, 4, 1, 3, false), "timeout cannot restart same capture");
Check(!timeout.Tick(4, false, false, true), "timeout remains terminal");
var blockedTimeout = Start();
blockedTimeout.Tick(4, false, false, false);
Check(blockedTimeout.State == QteState.Failed, "menu does not pause or extend deadline");
var cancelled = Start();
Press(cancelled, .1);
cancelled.Cancel();
Check(cancelled.State == QteState.Cancelled && !cancelled.Tick(.5, true, true, true), "external release/death/scene cancellation is terminal");
Check(!cancelled.Begin(1, 1, 5, 3, false), "cancelled capture cannot restart");
Check(cancelled.Begin(2, 1, 5, 3, true) && cancelled.Progress == 0, "recapture has independent state");
Check(!cancelled.Begin(3, 1, 5, 3, false), "second QTE cannot replace active one");
var shortWindow = new QteSession();
shortWindow.Begin(1, 10, 5, .5, false);
Check(Math.Abs(shortWindow.Remaining(10.2) - .3) < 1e-8, "remaining time uses supplied native-limited window");
bool invalid = false;
try { new QteSession().Begin(1, 0, 5, double.NaN, false); }
catch (ArgumentOutOfRangeException) { invalid = true; }
Check(invalid, "invalid duration rejected");

ReleaseTicket Ticket() => new ReleaseTicket(10, 20, 1000, 4000);
var ticket = Ticket();
Check(!ticket.TryConsume(Guid.NewGuid(), 10, 20, 1000, 1500, true), "unissued release token rejected");
Check(!ticket.TryConsume(ticket.Token, 11, 20, 1000, 1500, true), "another authenticated player cannot use ticket");
Check(!ticket.TryConsume(ticket.Token, 10, 21, 1000, 1500, true), "another grabber cannot use ticket");
Check(!ticket.TryConsume(ticket.Token, 10, 20, 1001, 1500, true), "old ticket cannot release a later native capture");
Check(!ticket.TryConsume(ticket.Token, 10, 20, 1000, 1500, false), "external release/death invalidates capability");
Check(!ticket.TryConsume(ticket.Token, 10, 20, 1000, 999, true), "release before native start rejected");
Check(ticket.TryConsume(ticket.Token, 10, 20, 1000, 1500, true), "valid owner can consume ticket after rejected foreign attempts");
Check(!ticket.TryConsume(ticket.Token, 10, 20, 1000, 1600, true), "replayed release is rejected even if game call failed");
var expired = Ticket();
Check(!expired.TryConsume(expired.Token, 10, 20, 1000, 4000, true), "host deadline rejects simultaneous release");
var next = Ticket();
Check(next.Token != ticket.Token && !next.TryConsume(ticket.Token, 10, 20, 1000, 1500, true), "recapture uses new capability even with identical IDs and time");
Check(Math.Abs(ReleaseTicket.ClientWindow(2.5, 10, 10.2) - 2.3) < 1e-8, "client subtracts full measured round trip from host allowance");
Check(ReleaseTicket.ClientWindow(.1, 10, 10.2) == 0, "late grant cannot start or extend QTE");
Check(ReleaseTicket.ClientWindow(3, 10, 9) == 0 && ReleaseTicket.ClientWindow(double.NaN, 10, 11) == 0,
    "invalid client timing fails closed");
string id = Guid.NewGuid().ToString("N");
Check(QteWire.Valid("op=begin^request=" + id + "^caster=20", true), "canonical client begin accepted");
Check(QteWire.Valid("op=release^token=" + id, true), "canonical release accepted");
Check(!QteWire.Valid("op=grant^request=" + id + "^token=" + id + "^ms=3000", true), "client cannot submit host-only messages");
Check(!QteWire.Valid("op=release^token=" + id + "^token=" + id, true), "duplicate fields rejected before native argument parser");
Check(!QteWire.Valid("op=begin^request=" + id + "^caster=2147483648", true), "overflow actor ID rejected");
Check(!QteWire.Valid("op=begin^request=" + id + "^caster=-1", true), "negative actor ID rejected");
Check(!QteWire.Valid("op=release^token=" + new string('a', 20000), true), "oversized payload rejected before splitting");
Check(!QteWire.Valid("op=release^token=" + Guid.Empty.ToString("N"), true), "empty capability rejected");
Check(!QteWire.Valid(null, true) && !QteWire.Valid("op=release\ntoken=" + id, true), "null/malformed payload rejected");
Check(QteWire.Valid("op=grant^request=" + id + "^token=" + id + "^ms=3000", false), "canonical host grant accepted");
Check(QteWire.Valid("op=grant^request=" + id + "^token=" + id + "^ms=30000", false), "longer native grabs support configured thirty-second maximum");
Check(!QteWire.Valid("op=grant^request=" + id + "^token=" + id + "^ms=30001", false), "grant cannot exceed configured maximum");
Check(CaptureKinds.Matches(30001, false, 20000001, true), "Mimesis grab accepted");
Check(CaptureKinds.Matches(50001, false, 20000005, false), "Mama Monkey direct grab accepted");
Check(CaptureKinds.Matches(60001, true, 20000007, false), "Quokka reverse grab accepted");
Check(!CaptureKinds.Matches(50001, true, 20000005, false) && !CaptureKinds.Matches(60001, false, 20000007, false), "wrong grab direction rejected");
Check(!CaptureKinds.Matches(50001, false, 20000007, false) && !CaptureKinds.Matches(60001, true, 20000013, false), "other monsters rejected");
Check(!CaptureKinds.Matches(30001, false, 20000005, false) && !CaptureKinds.Supported(13001), "non-Mimesis and PowMoth grabs rejected");
Check(!QteWire.Valid("op=result^token=" + id + "^ok=yes", false), "ambiguous result rejected");
var budget = new RequestBudget();
bool firstEight = true;
for (int i = 0; i < 8; i++) firstEight &= budget.Take(0);
Check(firstEight && !budget.Take(0), "per-connection burst bounded at eight messages");
Check(!budget.Take(999) && budget.Take(1000), "flood budget renews only after the window expires");
Check(!budget.Take(999), "backward time cannot reset flood protection");
Check(new RequestBudget().Take(0), "another connection has an independent budget");
Console.WriteLine($"{checks} checks passed.");
