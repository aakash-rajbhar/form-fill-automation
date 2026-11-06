# Task 1 – Resilient Selenium automation (C#)

Automates **First Name** (text box), **Gender** (radio) and **State** (dropdown) on
`https://app.cloudqa.io/home/AutomationPracticeForm`, then verifies each value was really set.

The page contains the same form many times: the main form, iframes, shadow DOMs, shadow DOMs nested up to
6 levels, and an iframe inside a shadow DOM. The program **discovers every form instance on the page and tests each one**,
then prints a per-instance summary.

It finds fields the way a person does (visible label, aria-label, placeholder, nearby text, group heading, option contents)
instead of fixed XPath/CSS, so it survives reordering, changed id/name/class, light HTML restructuring and look-alike fields.

## Setup

Requirements: .NET 8 SDK or newer, Google Chrome, internet on first run (Selenium Manager downloads chromedriver).

```bash
dotnet run -- --url https://app.cloudqa.io/home/AutomationPracticeForm
```

## Options (CLI arg > environment variable > default)

| Arg            | Env                   | Default                                         |
| -------------- | --------------------- | ----------------------------------------------- |
| `--url`        | `FORM_URL`            | the CloudQA practice form                       |
| `--first-name` | `FORM_FIRST_NAME`     | Alex                                            |
| `--gender`     | `FORM_GENDER`         | Male                                            |
| `--state`      | `FORM_STATE`          | first real option                               |
| `--settle`     | `FORM_SETTLE_SECONDS` | 20 (max seconds to wait for late-loading forms) |
| `--first-only` | –                     | off (test only the first form instance)         |
| `--headless`   | –                     | off                                             |

Exit code: 0 = every form instance passed, 1 = something failed or no form was found.

## Offline demos

```bash
dotnet run -- --url file://$PWD/testpages/original.html
dotnet run -- --url file://$PWD/testpages/mutated.html   # reordered, renamed, decoy fields
dotnet run -- --url file://$PWD/testpages/nested.html    # main + iframe + shadow DOM (2/3/6 levels, some closed) + iframe in shadow
```

`nested.html` should report 7 form instances.

## How it works

1. **Discovery** (`FormWalker.cs`, `Scripts/common.js`): lists the document and every nested shadow root, and every
   iframe (also inside shadow roots and inside other iframes). Each iframe is entered with `SwitchTo().Frame`, because
   element references only work inside their own frame. Closed shadow roots are opened by injecting a tiny script
   (`Page.addScriptToEvaluateOnNewDocument`) that makes `attachShadow` always create open roots (Chrome only).
   Discovery repeats until the instance count is stable, so late-loading iframes are included.
2. **Locating** (`Scripts/locate.js`): in each scope, every candidate is scored on label text (weight 10) >
   aria-label/placeholder (8) > nearby text (6) > id/name (3, weak hint). Exact matches beat longer ones
   ("First Name" beats "Parent's first name").
   - Radio: radios are grouped; the group is scored on its legend/heading/container text and name; the option is
     chosen by its own label (exact word, so "Male" never matches "Female").
   - Dropdown: scored by label, plus a bonus if it contains the requested value (so "Country" is not mistaken for "State").
3. **Acting + verifying**: fill / click / select, then re-read the value from the DOM to decide PASS or FAIL.
   Hidden or custom-styled radios fall back to a JS click.

## Example output (shape)

```
== [1] document
  [PASS] First Name: value is 'Alex'
  [PASS] Gender: 'Male' is selected
  [PASS] State: selected 'Afganistan'
== [2] iframe[0] > document
...
==== SUMMARY ====
PASS  document  (3/3 checks)
PASS  shadow(div#...) > shadow(div) (3/3 checks)
7/7 form instances fully passed
```

(Paste your real run output here.)

See `NOTES.md` for approach and limitations.
