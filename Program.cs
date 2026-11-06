using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using ResilientForm;

// ---- configuration: CLI args > environment variables > defaults ----
string Arg(string name, string env, string def)
{
    var i = Array.IndexOf(args, "--" + name);
    if (i >= 0 && i + 1 < args.Length) return args[i + 1];
    return Environment.GetEnvironmentVariable(env) ?? def;
}
var url       = Arg("url", "FORM_URL", "https://app.cloudqa.io/home/AutomationPracticeForm");
var firstName = Arg("first-name", "FORM_FIRST_NAME", "Alex");
var gender    = Arg("gender", "FORM_GENDER", "Male");
var state     = Arg("state", "FORM_STATE", "");               // empty = pick the first real option
var settle    = int.Parse(Arg("settle", "FORM_SETTLE_SECONDS", "20"));
var headless  = args.Contains("--headless");
var firstOnly = args.Contains("--first-only");                 // test only the first form instance found

var opts = new ChromeOptions();
if (headless) opts.AddArgument("--headless=new");
opts.AddArgument("--window-size=1366,900");
using IWebDriver driver = new ChromeDriver(opts);              // Selenium Manager downloads the driver automatically
var js = (IJavaScriptExecutor)driver;

// Force every shadow root created by the page to be "open", so closed shadow DOMs can be reached too (Chrome only).
try
{
    ((ChromeDriver)driver).ExecuteCdpCommand("Page.addScriptToEvaluateOnNewDocument", new Dictionary<string, object>
    {
        ["source"] = "(()=>{const o=Element.prototype.attachShadow;Element.prototype.attachShadow=function(i){return o.call(this,Object.assign({},i,{mode:'open'}));};})();"
    });
}
catch (Exception e) { Console.WriteLine($"(could not force open shadow roots: {e.Message})"); }

var cfg = JsonSerializer.Serialize(new
{
    text   = new { phrases = new[] { new[] { "first", "name" }, new[] { "fname" }, new[] { "given", "name" } } },
    radio  = new { phrases = new[] { new[] { "gender" }, new[] { "sex" } }, option = gender },
    select = new { phrases = new[] { new[] { "state" }, new[] { "province" }, new[] { "region" } }, option = state },
});

Console.WriteLine($"Target: {url}");
driver.Navigate().GoToUrl(url);
for (int i = 0; i < 40 && (string)js.ExecuteScript("return document.readyState") != "complete"; i++) Thread.Sleep(250);

var walker = new FormWalker(driver, cfg);

// ---- 1) discovery: wait until the number of form instances stops changing (iframes / widgets may load late) ----
Console.WriteLine("Discovering form instances (main page, iframes, shadow roots)...");
int last = -1, stable = 0;
var deadline = DateTime.UtcNow.AddSeconds(settle);
while (DateTime.UtcNow < deadline)
{
    int c = 0;
    try { walker.Walk(_ => c++); } catch (WebDriverException) { c = -1; }
    if (c > 0 && c == last) { if (++stable >= 2) break; } else stable = 0;
    last = c;
    Thread.Sleep(1000);
}
Console.WriteLine($"Found {Math.Max(last, 0)} form instance(s)\n");

// ---- 2) act + verify in every instance ----
var results = new List<(string where, string name, bool ok, string detail)>();
void Click(IWebElement el)
{
    try { el.Click(); }
    catch (WebDriverException) { js.ExecuteScript("arguments[0].click()", el); }     // custom-styled/hidden inputs
}
void Check(string where, string name, IWebElement? el, Func<IWebElement, string> body)
{
    try
    {
        if (el == null) throw new Exception("field not found in this form");
        var d = body(el);
        results.Add((where, name, true, d)); Console.WriteLine($"  [PASS] {name}: {d}");
    }
    catch (Exception e)
    {
        results.Add((where, name, false, e.Message)); Console.WriteLine($"  [FAIL] {name}: {e.Message.Split('\n')[0]}");
    }
}

int n = 0;
walker.Stop = false;
walker.Walk(hit =>
{
    if (firstOnly && n >= 1) { walker.Stop = true; return; }
    n++;
    Console.WriteLine($"== [{n}] {hit.Where}");
    Check(hit.Where, "First Name", hit.Text, el =>
    {
        el.Clear(); el.SendKeys(firstName);
        var actual = el.GetAttribute("value");
        return actual == firstName ? $"value is '{actual}'" : throw new Exception($"expected '{firstName}' but found '{actual}'");
    });
    Check(hit.Where, "Gender", hit.Radio, el =>
    {
        var note = "";
        Click(el);
        if (!el.Selected)                                   // normal click did not stick: retry via script
        {
            js.ExecuteScript("arguments[0].click()", el);
            note = " (needed script click)";
        }
        if (!el.Selected)
        {
            var diag = (string)js.ExecuteScript(
                "const e=arguments[0],r=e.getBoundingClientRect(),t=e.getRootNode().elementFromPoint(r.x+r.width/2,r.y+r.height/2);" +
                "return 'rect='+Math.round(r.x)+','+Math.round(r.y)+' '+Math.round(r.width)+'x'+Math.round(r.height)+" +
                "' topElement='+(t?t.tagName+(t.id?'#'+t.id:''):'none')+' checked='+e.checked+' disabled='+e.disabled;", el);
            throw new Exception($"'{gender}' not selected after click ({diag})");
        }
        return $"'{gender}' is selected{note}";
    });
    Check(hit.Where, "State", hit.Select, el =>
    {
        var options = ((System.Collections.IEnumerable)js.ExecuteScript("return [...arguments[0].options]", el)).Cast<IWebElement>().ToList();
        var placeholder = new[] { "select", "choose", "--", "please" };
        var target = string.IsNullOrEmpty(state)
            ? options.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Text) && !placeholder.Any(p => o.Text.ToLower().Contains(p)))
            : options.FirstOrDefault(o => o.Text.Trim().Equals(state, StringComparison.OrdinalIgnoreCase))
              ?? options.FirstOrDefault(o => o.Text.Contains(state, StringComparison.OrdinalIgnoreCase));
        if (target == null) throw new Exception($"no option matching '{state}'");
        var want = target.Text.Trim();
        Click(target);
        js.ExecuteScript("arguments[0].dispatchEvent(new Event('change',{bubbles:true}))", el);
        var got = (string)js.ExecuteScript("return arguments[0].options[arguments[0].selectedIndex].text", el);
        return got.Trim() == want ? $"selected '{got.Trim()}'" : throw new Exception($"expected '{want}' but selected '{got}'");
    });
});

// ---- summary ----
Console.WriteLine("\n==== SUMMARY ====");
var groups = results.GroupBy(r => r.where).ToList();
foreach (var g in groups)
    Console.WriteLine($"{(g.All(r => r.ok) ? "PASS" : "FAIL")}  {g.Key}  ({g.Count(r => r.ok)}/{g.Count()} checks)");
Console.WriteLine($"{groups.Count(g => g.All(r => r.ok))}/{groups.Count} form instances fully passed");
if (groups.Count == 0) Console.WriteLine("No form instance was found.");
return results.Count > 0 && results.All(r => r.ok) ? 0 : 1;