using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Zenith.App.Extensions;
using Zenith.App.Navigation;
using Zenith.Core.Permissions;

namespace Zenith.App.Tests.Navigation;

internal static class FileChooserScenario
{
    internal static async Task RunAsync()
    {
        await RunAsync(allowed: false);
        await RunAsync(allowed: true);
        foreach (var failure in new[] { "unavailable", "null", "invalid" })
            await FailureAsync(failure);
    }

    private static async Task FailureAsync(string failure)
    {
        await using var root = new LoopbackSite("127.0.0.1");
        await using var child = new LoopbackSite("127.0.0.2");
        child.Response = _ => (200, "", "<script>fetch('/executed')</script>");
        var folder = Directory.CreateTempSubdirectory("Zenith-ChooserFailure-");
        var browser = new WebView2 { CreationProperties = new() { UserDataFolder = folder.FullName, AdditionalBrowserArguments = "--no-proxy-server" } };
        var host = new Window { Content = browser, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FileChooserGuard? guard = null;
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var evaluations = 0;
            guard = new FileChooserGuard(core, () => ++evaluations == 1
                ? new BrowserCapabilityPolicy().Evaluate(BrowserCapability.FileSelection)
                : failure switch
                {
                    "null" => null!,
                    "invalid" => new CapabilityDecision(true, ""),
                    _ => throw new IOException("Synthetic policy unavailable")
                }, _ => { }, () => { browser.Dispose(); failed.TrySetResult(); });
            await guard.InitializeAsync();
            core.Navigate(root.Origin + "/root");
            await Until(async () => await core.ExecuteScriptAsync("document.readyState==='complete'") == "true" && core.Source == root.Origin + "/root");
            await core.ExecuteScriptAsync(Frame("child", child.Origin + "/child"));
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(evaluations >= 2);
            Assert.DoesNotContain("/executed", child.Requests);
            Console.WriteLine($"F06 policy {failure}: failed closed; controller disposed before child script resumed");
        }
        finally
        {
            guard?.Dispose();
            browser.Dispose();
            host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task RunAsync(bool allowed)
    {
        await using var root = new LoopbackSite("127.0.0.1");
        await using var cross = new LoopbackSite("127.0.0.2");
        await using var deep = new LoopbackSite("127.0.0.3");
        foreach (var site in new[] { root, cross, deep })
            site.Response = _ => (200, "", """
                <html><body><input id="file" type="file"><script>
                window.ready=true;window.cancelled=false;window.selected=null;
                file.oncancel=()=>cancelled=true;
                file.onchange=async()=>{const f=file.files[0];if(f){selected={name:f.name,value:file.value,text:await f.text()};await fetch('/upload',{method:'POST',body:selected.text});}};
                </script></body></html>
                """);
        var folder = Directory.CreateTempSubdirectory("Zenith-FileChooser-");
        var selectedPath = Path.Combine(folder.FullName, "selected-fixture.txt");
        const string contents = "Zenith synthetic selected file only";
        File.WriteAllText(selectedPath, contents);
        BundledExtensions.VerifyPackage();
        var browser = new WebView2 { CreationProperties = new() { UserDataFolder = Path.Combine(folder.FullName, "Profile"), AdditionalBrowserArguments = "--no-proxy-server", AreBrowserExtensionsEnabled = true } };
        var host = new Window { Content = browser, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FileChooserGuard? guard = null;
        var failed = false;
        try
        {
            host.Show();
            await browser.EnsureCoreWebView2Async();
            var core = browser.CoreWebView2;
            core.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            var sessions = new Dictionary<string, string>();
            core.GetDevToolsProtocolEventReceiver("Target.attachedToTarget").DevToolsProtocolEventReceived += (_, e) =>
            {
                using var data = JsonDocument.Parse(e.ParameterObjectAsJson);
                sessions[data.RootElement.GetProperty("targetInfo").GetProperty("targetId").GetString()!] = data.RootElement.GetProperty("sessionId").GetString()!;
            };
            // Production uses the unchanged deny-all Core policy. Allowed is an
            // adapter contract test, not a new user-facing grant mechanism.
            guard = new FileChooserGuard(core,
                () => allowed ? new CapabilityDecision(true, "Synthetic allowed decision") : new BrowserCapabilityPolicy().Evaluate(BrowserCapability.FileSelection),
                _ => { }, () => { failed = true; browser.Dispose(); });
            await guard.InitializeAsync();
            await BundledExtensions.InstallAsync(core.Profile);
            core.Navigate(root.Origin + "/root");
            await Until(async () => await core.ExecuteScriptAsync("window.ready===true") == "true");
            await core.ExecuteScriptAsync(Frame("same", root.Origin + "/same") + Frame("cross", cross.Origin + "/cross"));
            await Until(async () => await core.ExecuteScriptAsync("frames.same?.ready===true") == "true");
            await core.ExecuteScriptAsync("frames.same.eval(" + J(Frame("nested", root.Origin + "/nested")) + ")");
            await Until(async () => await core.ExecuteScriptAsync("frames.same.frames.nested?.ready===true") == "true");

            async Task<string> Session(string url)
            {
                string? session = null;
                await Until(async () =>
                {
                    using var targets = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Target.getTargets", "{}"));
                    var target = targets.RootElement.GetProperty("targetInfos").EnumerateArray().FirstOrDefault(t => t.GetProperty("url").GetString() == url);
                    return target.ValueKind != JsonValueKind.Undefined && sessions.TryGetValue(target.GetProperty("targetId").GetString()!, out session);
                });
                await Until(async () => (await Eval(core, session, "window.ready===true")).GetBoolean());
                return session!;
            }
            var crossSession = await Session(cross.Origin + "/cross");
            await Eval(core, crossSession, Frame("deep", deep.Origin + "/deep"));
            var deepSession = await Session(deep.Origin + "/deep");
            var processFrames = (await core.Environment.GetProcessExtendedInfosAsync()).SelectMany(p => p.AssociatedFrameInfos.Select(f => new { p.ProcessInfo.ProcessId, f.Source })).ToArray();
            var rootPid = processFrames.Single(f => f.Source == root.Origin + "/root").ProcessId;
            var crossPid = processFrames.Single(f => f.Source == cross.Origin + "/cross").ProcessId;
            var deepPid = processFrames.Single(f => f.Source == deep.Origin + "/deep").ProcessId;
            Assert.NotEqual(rootPid, crossPid);
            Assert.NotEqual(crossPid, deepPid);
            Console.WriteLine($"F06 allowed={allowed}, runtime={core.Environment.BrowserVersionString}, renderer PIDs root={rootPid}, oopif={crossPid}, nested-oopif={deepPid}");

            foreach (var (name, session, prefix, site) in new[]
            {
                ("root", (string?)null, "", root), ("same", (string?)null, "frames.same.", root),
                ("nested", (string?)null, "frames.same.frames.nested.", root),
                ("oopif", (string?)crossSession, "", cross), ("nested-oopif", (string?)deepSession, "", deep)
            })
            {
                string InFrame(string expression) => prefix.Length == 0 ? expression : prefix + "eval(" + J(expression) + ")";
                var before = site.Received.Count(r => r.Path == "/upload");
                await Eval(core, session, InFrame("file.click()"), gesture: true);
                if (allowed)
                {
                    IntPtr dialog = IntPtr.Zero;
                    await Until(() => Task.FromResult((dialog = Dialogs(core).FirstOrDefault()) != IntPtr.Zero));
                    // Drive the real native chooser with only our synthetic file.
                    // No CDP file injection or native web filesystem handle is used.
                    SelectFixture(dialog, selectedPath);
                    await Until(async () => (await Eval(core, session, InFrame("selected!==null"))).GetBoolean());
                    var selection = await Eval(core, session, InFrame("selected"));
                    Assert.Equal("selected-fixture.txt", selection.GetProperty("name").GetString());
                    Assert.Equal(contents, selection.GetProperty("text").GetString());
                    Assert.DoesNotContain(folder.FullName, selection.GetProperty("value").GetString()!);
                    await Until(() => Task.FromResult(site.Received.Count(r => r.Path == "/upload" && r.Body == contents) == before + 1));
                }
                else
                {
                    for (var i = 0; i < 25; i++)
                    {
                        Assert.Empty(Dialogs(core));
                        await Task.Delay(20);
                    }
                    var result = await Eval(core, session, InFrame("({count:file.files.length,value:file.value,selected,cancelled})"));
                    Assert.Equal(0, result.GetProperty("count").GetInt32());
                    Assert.Equal("", result.GetProperty("value").GetString());
                    Assert.Equal(JsonValueKind.Null, result.GetProperty("selected").ValueKind);
                    Assert.True(result.GetProperty("cancelled").GetBoolean());
                    Assert.Equal(before, site.Received.Count(r => r.Path == "/upload"));
                }
                Assert.False(failed);
                Console.WriteLine($"F06 {name}: {(allowed ? "native chooser selected fixture; upload observed; real path hidden" : "no native chooser; cancelled; no selection/path/upload")}");
            }
            // Remove and recreate a cross-process subtree; protection must attach
            // again rather than depending on the first renderer's lifetime.
            await core.ExecuteScriptAsync("document.querySelector('iframe[name=cross]').remove();" + Frame("replacement", cross.Origin + "/replacement"));
            var replacement = await Session(cross.Origin + "/replacement");
            if (!allowed)
            {
                await Eval(core, replacement, "file.click()", gesture: true);
                await Until(async () => (await Eval(core, replacement, "cancelled")).GetBoolean());
                Assert.Empty(Dialogs(core));
            }
            await core.ExecuteScriptAsync("document.querySelector('iframe[name=replacement]').src=" + J(root.Origin + "/merged"));
            await Until(async () => await core.ExecuteScriptAsync("(()=>{try{return frames.replacement.ready===true}catch{return false}})()") == "true");
            await core.ExecuteScriptAsync("document.querySelector('iframe[name=replacement]').src=" + J(cross.Origin + "/swapped-back"));
            var swapped = await Session(cross.Origin + "/swapped-back");
            if (!allowed)
            {
                await Eval(core, swapped, "file.click()", gesture: true);
                await Until(async () => (await Eval(core, swapped, "cancelled")).GetBoolean());
                Assert.Empty(Dialogs(core));
            }
            Console.WriteLine($"F06 allowed={allowed}: frame recreation and renderer process swaps passed");
            Assert.False(failed);
        }
        finally
        {
            guard?.Dispose();
            if (browser.CoreWebView2 is { } core)
                foreach (var dialog in Dialogs(core)) PostMessage(dialog, 0x0010, IntPtr.Zero, IntPtr.Zero);
            browser.Dispose();
            host.Close();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
            try { folder.Delete(true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Frame(string name, string url) => "(()=>{const f=document.createElement('iframe');f.name=" + J(name) + ";f.src=" + J(url) + ";document.body.append(f)})();";
    private static string J(object value) => JsonSerializer.Serialize(value);
    private static async Task<JsonElement> Eval(CoreWebView2 core, string? session, string expression, bool gesture = false)
    {
        var parameters = J(new { expression, userGesture = gesture, returnByValue = true });
        using var result = JsonDocument.Parse(await core.CallDevToolsProtocolMethodForSessionAsync(session!, "Runtime.evaluate", parameters).WaitAsync(TimeSpan.FromSeconds(10)));
        if (result.RootElement.TryGetProperty("exceptionDetails", out var error)) throw new InvalidOperationException(error.ToString());
        return result.RootElement.GetProperty("result").TryGetProperty("value", out var value) ? value.Clone() : JsonSerializer.SerializeToElement("undefined");
    }
    private static async Task Until(Func<Task<bool>> predicate)
    {
        for (var i = 0; i < 500; i++) { if (await predicate()) return; await Task.Delay(20); }
        throw new TimeoutException("File chooser fixture state did not arrive.");
    }
    private static List<IntPtr> Dialogs(CoreWebView2 core)
    {
        var pids = core.Environment.GetProcessInfos().Select(p => p.ProcessId).Append(Environment.ProcessId).ToHashSet();
        var found = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            var name = new StringBuilder(256);
            if (pids.Contains((int)pid) && IsWindowVisible(window) && GetClassName(window, name, 256) != 0 && name.ToString() == "#32770") found.Add(window);
            return true;
        }, IntPtr.Zero);
        return found;
    }
    private static void SelectFixture(IntPtr dialog, string path)
    {
        IntPtr edit = IntPtr.Zero;
        EnumChildWindows(dialog, (window, _) =>
        {
            var name = new StringBuilder(256); GetClassName(window, name, 256);
            if (name.ToString() == "Edit" && GetDlgCtrlID(window) == 1148) edit = window;
            return true;
        }, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, edit);
        SendMessage(edit, 0x000C, IntPtr.Zero, path);
        PostMessage(dialog, 0x0111, new IntPtr(1), IntPtr.Zero);
    }
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindow callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern int GetDlgCtrlID(IntPtr window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, string text);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
