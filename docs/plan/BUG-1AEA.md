# BUG-1AEA — The diff dialog scrolls everything

**Status:** DONE — see `docs/done/BUG-1AEA.md`
**Type:** BUG
**Branch:** `bugfix/bug-1aea-dialog-pane-scrolling`
**Run:** feature/2026-09-18-history-and-diffs

## Objective

Give the changed-files list and the diff their own scrollbars inside the commit dialog, so the
dialog's single scrollbar stops moving both of them at once.

## Context & constraints

- Reported as "the vertical scrollbar of the content dialog scrolls for everything: the files on the
  left and the diffs comparison. For me it makes no sense".
- Measured, not assumed. A headless capture of the opened dialog over a 4 000-line diff and a
  41-file commit reports:

  ```
  ScrollViewer            bounds=0,44,1078,695   extent=1078,84069  viewport=1078,695
    DockPanel#DiffDialogBody                     bounds=0,0,1078,84069
      ChangedFilesPanelView                      bounds=0,0,320,84028
        ListBox                                  bounds=0,0,320,83951
          ScrollViewer#PART_ScrollViewer         extent=320,83951   viewport=320,83951
      DiffViewerView                             bounds=326,0,752,84028
        ListBox                                  bounds=0,0,752,83991
          ScrollViewer#PART_ScrollViewer         extent=752,83991   viewport=752,83991
  ```

  The card wraps its content in a `ScrollViewer` — which is what the control library's
  `DialogMaxHeight` documentation means by "set the max to make tall content scroll inside the
  card". A scrolling `ScrollViewer` measures its child with **infinite** height, so the body is laid
  out 84 069 px tall, each inner list is given exactly the height it asked for, and every inner
  viewport equals its own extent. Hence one bar for everything, and no bar on either list.
- The same measurement shows a second cost: `ScrollViewer.HorizontalScrollBarVisibility` /
  `VerticalScrollBarVisibility` `Disabled` is the one state in which a `ScrollContentPresenter`
  measures its child with the space it actually has, and the diff list is a
  `VirtualizingStackPanel`. Measured at infinite height it realises **every** row of the patch, so a
  4 000-line file builds 4 000 rows the moment it is shown. Bounding the body restores the
  virtualisation the list was built for.
- The dialog's own size is already bound: `DialogHeight`/`DialogMaxHeight` follow the page through
  `DialogSizing.Fill`, so the card's height does not depend on its content and the `ScrollViewer`
  inside it is laid out to a height the card decided. There is nothing to scroll to at that level
  once the body fits — the two lists are the things with more content than room.
- The panel and the viewer both set `ScrollViewer.HorizontalScrollBarVisibility="Disabled"` today.
  For the diff that is deliberate and stays: `BUG-1D34` and `BUG-0DC2` exist because a horizontally
  scrolling diff list drags its gutter along, so the viewer scrolls sideways through its own
  character-counted bars instead. For the file list it is just a leftover.

## Steps

1. `Views/Pages/HistoryPageView.axaml.cs`: when the body is attached, find the `ScrollViewer` the
   card puts around it and turn both of its scroll directions off, so the body is measured against
   the room the card gives it. Guarded: a dialog template without one leaves the behaviour exactly
   as it is today, so a future version of the control library cannot break the page.
2. Same file: a comment saying why a page reaches into another library's template here — the card
   scrolls its content by design, and this body is two panes that scroll themselves.
3. `Views/Panels/ChangedFilesPanelView.axaml`: the list and the tree take
   `ScrollViewer.HorizontalScrollBarVisibility="Auto"`, so a long name or a deep tree is reachable
   rather than merely trimmed. The vertical bar is the `ListBox`/`TreeView`'s own and needs nothing.
4. Tests — `tests/.../HistoryPageTests.cs`: over a commit big enough to overflow in both panes, the
   card's `ScrollViewer` has extent equal to viewport (nothing left for it to scroll), while the
   files list and the diff each report an extent taller than their own viewport; and the body is no
   taller than the card's content area.

## Acceptance criteria

- With a 40-file commit and a 4 000-line patch shown side by side, the card's `ScrollViewer` reports
  `Extent.Height == Viewport.Height`.
- The changed-files list and the diff list each report `Extent.Height > Viewport.Height` and scroll
  independently of one another.
- `DiffDialogBody`'s height is bounded by the card, not by its content.
- The dialog still fills the page and still follows a resize (the `FEATURE-2288` sizing tests stay
  green).
- The diff list still refuses to scroll horizontally itself — the viewer's own bars are what move
  the text (the `BUG-1D34` / `BUG-0DC2` tests stay green).
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The two panes' *horizontal* synchronisation, which is `FEATURE-295F-PHASE01`.
- Remembering a scroll position between commits, or restoring one when the dialog reopens.
- The conflict-resolution page's three-way view, which is not in a dialog.
- Any change to the control library itself.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the body is bounded | Turn the card's own `ScrollViewer` off for this body | Exact at every window size and self-correcting, because the card decides that viewer's height | A `MaxHeight` on the body computed from the page size (needs a chrome constant — title, buttons, padding — that drifts with the package version) |
| What happens on a template without that viewer | Nothing; today's behaviour | A layout fix must not become a crash when a dependency's template changes | Throwing, or asserting the template's shape |
| Where it is done | The page's code-behind, at attach time | It is a fact about this dialog's body, not about the panels, which are also used outside a dialog | Doing it inside `ChangedFilesPanelView` / `DiffViewerView` (would reach up out of a reusable panel) |
| The file list's horizontal bar | `Auto` on both shapes | The request asks for the list to have its own scrollbars, and a deep tree is otherwise unreachable | Leaving it `Disabled` (trimming only); `Visible` (a bar that is always there for names that usually fit) |
| The diff list's horizontal bar | Still `Disabled` | A scrolling diff list takes its gutter with it, which is the bug `BUG-1D34` and `BUG-0DC2` were about; the viewer scrolls sideways in characters instead | Enabling it and dropping the character-counted bars |
| How it is proved | Extent against viewport on the real controls | It is the exact measurement that showed the defect, so the test fails on the current code | Asserting a pixel height, or a snapshot |
