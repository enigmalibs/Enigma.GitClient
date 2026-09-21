# BUG-36A9 — A found row cannot be hovered

**Status:** DONE
**Type:** BUG
**Branch:** `bugfix/bug-36a9-match-hover-and-selection`
**Run:** feature/2026-09-21-refs-tags-and-dragging

## Objective

A history row the search found still shows that it is hovered and still shows that it is selected.

## Context & constraints

- The search wash is a background on the **row's own `Grid`**, not on its `ListBoxItem`:
  `Themes/Styles.axaml` has `Grid.commitrow { Background: Transparent }` and
  `Grid.commitrow.match { Background: SearchMatchBrush }`, and the row template sets
  `Classes.match="{Binding IsSearchMatch}"`.
- The hover and the selection are painted by the `ListBoxItem`'s own template, **behind** that Grid.
  `SearchMatchBrush` is opaque (`#3A3517` dark, `#FBF0C2` light), so a matched row covers its
  container's `:pointerover` and `:selected` plates completely: the row cannot be seen to be hovered
  and cannot be seen to be selected.
- The transparent background on a non-matched row is deliberate and must stay — it is what makes the
  whole line a hit target for its own context menu. A non-matched row therefore still shows the
  container's states, which is why only matched rows are affected.
- A local value in markup outranks every style setter in Avalonia, which is why the wash lives in
  styles rather than on the template's `Grid`. Further states must be styles too, declared **after**
  `Grid.commitrow.match` so they win at the same precedence.
- Selection-aware text already exists: `ListBoxItem:selected TextBlock.dim` / `.faint` restore the
  full foreground, so only the plate is missing.
- Colours live in `Themes/Graph.axaml`, which defines a `Color` per theme variant and one
  `SolidColorBrush` over it. `SearchMatchColor` is defined for Dark and Light.
- **Baseline:** clean build, 1775 tests green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the fix goes | Two more styles on `Grid.commitrow.match`, selected through `ListBoxItem:pointerover` and `ListBoxItem:selected` | The wash is the row's own background; the row is the only thing that can answer for all three states | Making the wash semi-transparent (the container's plate would tint the match colour differently in each state, and the match would stop being one recognisable colour) |
| What a hovered match looks like | The match colour, lifted | It has to read as "the same found row, under the pointer" rather than as a fourth colour | The plain hover plate (the row would stop looking found while the pointer is on it) |
| What a selected match looks like | The match colour pushed towards the selection: clearly the selected row, still visibly found | Both facts matter at once, and the selected row is the one the reader is acting on | Dropping the wash when selected (the count says "12 found" and one of them silently stops being marked) |
| Where the colours come from | Two new `Color` keys per theme variant in `Graph.axaml`, with brushes beside `SearchMatchBrush` | Same shape as every other colour in this app, and it keeps Dark and Light tuned separately | Computing a blend at runtime (a brush per row, and no way to tune Light and Dark apart) |

## Steps

1. `Themes/Graph.axaml`: `SearchMatchHoverColor` and `SearchMatchSelectedColor` for the Dark variant
   and for the Light variant, and `SearchMatchHoverBrush` / `SearchMatchSelectedBrush` beside
   `SearchMatchBrush`.
2. `Themes/Styles.axaml`: after `Grid.commitrow.match`, a style for
   `ListBoxItem:pointerover Grid.commitrow.match` and one for
   `ListBoxItem:selected Grid.commitrow.match`, with a comment saying why the row rather than the
   container answers for all three states.
3. Tests — `tests/.../HistoryPageTests.cs`: with a search running, a matched row's painted colour is
   the match colour; the same row under the pointer is the hover match colour; the same row selected
   is the selected match colour; and a row the search did not find is transparent in all three
   states, so the container keeps painting those.
4. Same file: the three match colours are distinct from one another, so none of the states is a
   change the reader cannot see.

## Acceptance criteria

- Hovering a highlighted search result changes its background, and it still reads as a found row.
- Selecting a highlighted search result shows it as the selected row, and it still reads as found.
- A row the search did not find keeps hovering and selecting exactly as it does today.
- Both theme variants define all three colours.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- What the search matches, how it counts them, or the clear button.
- The selection colours of any other list.
- Highlighting the matched substring inside the subject.
