using System.Collections;
using System.Text.Json;
using OpenQA.Selenium;

namespace ResilientForm;

/// <summary>One form instance: the fields found inside one scope (document or shadow root) of one browsing context.</summary>
public record ScopeHit(string Where, IWebElement? Text, IWebElement? Radio, IWebElement? Select, string Debug);

/// <summary>
/// Walks the whole page: the main document, every (nested) open shadow root, and every iframe (including iframes
/// inside shadow roots, and iframes in iframes). In each scope it asks Scripts/locate.js to find the three fields
/// by meaning rather than by selector. The visitor runs while the driver is switched into the right frame, because
/// element references are only valid inside the frame they came from.
/// </summary>
public class FormWalker
{
    private const int MaxFrameDepth = 8;
    private readonly IWebDriver _driver;
    private readonly IJavaScriptExecutor _js;
    private readonly string _common, _locate, _frames, _scopes, _cfg;
    public bool Stop;

    public FormWalker(IWebDriver driver, string cfgJson)
    {
        _driver = driver; _js = (IJavaScriptExecutor)driver; _cfg = cfgJson;
        _common = Load("common.js"); _locate = Load("locate.js"); _frames = Load("frames.js"); _scopes = Load("scopes.js");
    }

    private static string Load(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", name));

    public void Walk(Action<ScopeHit> visit)
    {
        _driver.SwitchTo().DefaultContent();
        WalkContext("", visit, 0);
        _driver.SwitchTo().DefaultContent();
    }

    private void WalkContext(string path, Action<ScopeHit> visit, int depth)
    {
        string[] labels;
        try { labels = JsonSerializer.Deserialize<string[]>((string)_js.ExecuteScript(_common + _scopes))!; }
        catch (WebDriverException) { return; }

        for (int i = 0; i < labels.Length && !Stop; i++)
        {
            object? res;
            try { res = _js.ExecuteScript(_common + _locate, i, _cfg); }
            catch (WebDriverException) { continue; }          // DOM changed under us; next pass will retry
            if (res is not IEnumerable list) continue;
            var items = list.Cast<object?>().ToArray();
            var hit = new ScopeHit(path + labels[i], items[0] as IWebElement, items[1] as IWebElement,
                                   items[2] as IWebElement, items[3] as string ?? "");
            if (new[] { hit.Text, hit.Radio, hit.Select }.Count(e => e != null) >= 2) visit(hit);   // looks like our form
        }

        if (depth >= MaxFrameDepth) return;
        List<IWebElement> frames;
        try { frames = ((IEnumerable)_js.ExecuteScript(_common + _frames)).Cast<IWebElement>().ToList(); }
        catch (WebDriverException) { return; }

        for (int f = 0; f < frames.Count && !Stop; f++)
        {
            try { _driver.SwitchTo().Frame(frames[f]); WalkContext($"{path}iframe[{f}] > ", visit, depth + 1); }
            catch (WebDriverException e) { Console.WriteLine($"    (could not enter {path}iframe[{f}]: {e.GetType().Name})"); }
            finally { try { _driver.SwitchTo().ParentFrame(); } catch (WebDriverException) { } }
        }
    }
}
