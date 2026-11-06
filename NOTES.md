# Approach & limitations (Task 1)

**Approach**

- Walk the whole page (document, nested shadow roots, iframes, iframes inside shadow roots) and test every form instance found.
- Locate fields by meaning: score candidates on label / aria-label / placeholder / nearby text / group heading / option contents,
  with id and name only as weak hints. No fixed XPath or CSS selectors.
- Verify after acting (read back the value) and report PASS/FAIL per field and per form instance.

**Limitations**

- Only native `<select>` dropdowns; custom div-based dropdowns (React-Select etc.) are not handled.
- Closed shadow roots are only reachable because a script forces them open at page load (Chrome/CDP only). Roots created
  before that script runs, or in other browsers, may be missed.
- Matching uses English keywords (first/name, gender/sex, state/province/region); other languages need more phrases in `Program.cs`.
- A scope counts as "a form" if at least 2 of the 3 fields are found; a form missing all or all-but-one fields is skipped silently.
- If the label is an image or there is no text near a field, it fails rather than guessing.
- Fields hidden at the moment of testing (collapsed accordions, display:none) are skipped.
- Iframes that are cross-origin are entered via WebDriver, but pages that block automation inside them may still fail.
- Verified by running against the live CloudQA page (20/20 form instances, 60 checks) and the included
  original/mutated/nested test pages. Only tested with Chrome on Linux.
