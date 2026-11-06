using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Enigma.Avalonia.Desktop.Controls.ContentDialog;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.Views;

/// <summary>
/// Puts the keyboard focus inside a dialog as it opens, where the next keystroke belongs, and gives it
/// back to where it was when the dialog closes.
/// </summary>
/// <remarks>
/// <para>
/// The library's dialog moves the focus nowhere: it stayed on whatever opened it — a toolbar button, a
/// menu item — behind the dialog, so Enter and Escape never reached the dialog, and a name could not be
/// typed before clicking in its box. Once the dialog is laid out, the focus goes to:
/// </para>
/// <list type="number">
/// <item>nowhere new, when a view of the dialog has already taken it — the squash dialog puts the caret
/// on the message's title rather than in the branch name above it;</item>
/// <item>the dialog's first text field that can be typed in — not a read-only one, such as an error's
/// details;</item>
/// <item>else a button: Cancel on a question whose red button throws work away
/// (<see cref="ContentDialogServiceExtensions.DangerClass"/>), so an Enter pressed by mistake right away
/// cancels; otherwise the dialog's <see cref="ContentDialog.DefaultButton"/> — the library's "keyboard
/// focused action", which every question sets — or, when it names none or is not shown, the first shown
/// of the main, the secondary and the close button. Enter then answers with it, and Tab goes on to the
/// next buttons.</item>
/// </list>
/// <para>
/// The focus is given as Tab gives it: a filled single-line field has its text selected, so typing
/// replaces it, and a button shows its focus ring, so the reader sees which one Enter presses.
/// </para>
/// <para>
/// Every window attaches its dialog hosts once (<see cref="Attach"/>), as the views attach
/// <see cref="LineMenus"/>.
/// </para>
/// </remarks>
internal static class DialogFocus
{
    /// <summary>The template part of the dialog's main button.</summary>
    internal const string PrimaryButtonPart = "PART_PrimaryButton";

    /// <summary>The template part of the dialog's secondary button.</summary>
    internal const string SecondaryButtonPart = "PART_SecondaryButton";

    /// <summary>The template part of the dialog's close button — Cancel, Close, OK.</summary>
    internal const string CloseButtonPart = "PART_CloseButton";

    private static readonly ConditionalWeakTable<ContentDialog, Watch> Watched = new();

    /// <summary>
    /// Makes a dialog host move the focus in as each dialog opens, and back out as it closes. Attaching
    /// a host twice changes nothing.
    /// </summary>
    /// <param name="host">The host.</param>
    public static void Attach(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Watched.TryGetValue(host, out _))
        {
            return;
        }

        Watch watch = new(host);
        Watched.Add(host, watch);
        host.PropertyChanged += watch.OnHostPropertyChanged;
    }

    /// <summary>
    /// Gets a value indicating whether a host has been attached.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns><see langword="true"/> once <see cref="Attach"/> has been called for it.</returns>
    internal static bool IsAttached(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return Watched.TryGetValue(host, out _);
    }

    /// <summary>
    /// Puts the focus where the open dialog wants it, unless a view of the dialog has already taken it.
    /// </summary>
    /// <param name="host">The host, open.</param>
    /// <returns><see langword="true"/> when the focus is inside the dialog afterwards.</returns>
    internal static bool MoveFocusIn(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (FocusedElement(host) is Visual focused && TakenByTheDialogsOwnView(host, focused))
        {
            return true;
        }

        InputElement? target = (InputElement?)FirstTextField(host) ?? ButtonToFocus(host);

        return target?.Focus(NavigationMethod.Tab) ?? false;
    }

    /// <summary>
    /// Gets the dialog's first text field that can be typed in, in the order it is laid out.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The field, or <see langword="null"/> when the dialog asks for no text.</returns>
    internal static TextBox? FirstTextField(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return host.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(box => !box.IsReadOnly && CanTakeFocus(box));
    }

    /// <summary>
    /// Gets the button an open dialog with no text field to type in gives the focus to.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <returns>The button, or <see langword="null"/> when none is shown.</returns>
    internal static Button? ButtonToFocus(ContentDialog host)
    {
        ArgumentNullException.ThrowIfNull(host);

        IEnumerable<string> parts = host.Classes.Contains(ContentDialogServiceExtensions.DangerClass)
            ? [CloseButtonPart]
            : new[] { PartOf(host.DefaultButton), PrimaryButtonPart, SecondaryButtonPart, CloseButtonPart }
                .OfType<string>()
                .Distinct(StringComparer.Ordinal);

        foreach (string part in parts)
        {
            if (Part(host, part) is { } button && CanTakeFocus(button))
            {
                return button;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets one of the dialog's own buttons.
    /// </summary>
    private static Button? Part(ContentDialog host, string name)
        => host.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => ReferenceEquals(button.TemplatedParent, host) && button.Name == name);

    /// <summary>
    /// Gets the button part a <see cref="DefaultButton"/> names.
    /// </summary>
    private static string? PartOf(DefaultButton button)
        => button switch
        {
            DefaultButton.Primary => PrimaryButtonPart,
            DefaultButton.Secondary => SecondaryButtonPart,
            DefaultButton.Close => CloseButtonPart,
            _ => null,
        };

    /// <summary>
    /// Gets a value indicating whether the focus is inside the dialog, somewhere a view put it — not on
    /// one of the dialog's own buttons, where the previous question may have left it.
    /// </summary>
    private static bool TakenByTheDialogsOwnView(ContentDialog host, Visual focused)
        => host.IsVisualAncestorOf(focused)
            && !(focused is Button { Name: PrimaryButtonPart or SecondaryButtonPart or CloseButtonPart } button
                && ReferenceEquals(button.TemplatedParent, host));

    /// <summary>
    /// Gets a value indicating whether an element can take the focus now.
    /// </summary>
    private static bool CanTakeFocus(InputElement element)
        => element.Focusable && element.IsEffectivelyEnabled && element.IsEffectivelyVisible;

    /// <summary>
    /// Gets the element that has the focus in the host's window.
    /// </summary>
    private static IInputElement? FocusedElement(Visual host)
        => TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();

    /// <summary>
    /// One host's watch: the element to give the focus back to, and the pass waiting for the layout.
    /// </summary>
    private sealed class Watch
    {
        private readonly ContentDialog _host;
        private InputElement? _returnTo;
        private bool _waiting;

        /// <summary>
        /// Initialises a new instance.
        /// </summary>
        /// <param name="host">The host watched.</param>
        public Watch(ContentDialog host) => _host = host;

        /// <summary>
        /// Moves the focus in as the dialog opens, and out as it closes.
        /// </summary>
        public void OnHostPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property != ContentDialog.IsOpenProperty)
            {
                return;
            }

            if (_host.IsOpen)
            {
                Opened();
            }
            else
            {
                Closed();
            }
        }

        /// <summary>
        /// Remembers where the focus is, and waits for the dialog to be laid out: until then its view is
        /// not in the tree, and its buttons may not exist yet.
        /// </summary>
        private void Opened()
        {
            Remember();

            if (!_waiting)
            {
                _waiting = true;
                _host.LayoutUpdated += OnLayoutUpdated;
            }
        }

        /// <summary>
        /// Once the dialog is laid out, moves the focus in — after the views' own
        /// <see cref="Control.Loaded"/> work, which runs first at a higher priority, so a view that
        /// puts the focus somewhere of its own is seen to have done it.
        /// </summary>
        private void OnLayoutUpdated(object? sender, EventArgs e)
        {
            StopWaiting();

            Dispatcher.UIThread.Post(
                () =>
                {
                    if (_host.IsOpen)
                    {
                        // A menu item that opened the dialog has gone with its menu by now, and the
                        // menu has given the focus back to what had it before: that is where to return.
                        Remember();
                        MoveFocusIn(_host);
                    }
                },
                DispatcherPriority.Background);
        }

        /// <summary>
        /// Gives the focus back to where it was before the dialog opened, when it is still there and can
        /// take it, rather than leaving it on a button the closed dialog no longer shows.
        /// </summary>
        private void Closed()
        {
            StopWaiting();

            InputElement? returnTo = _returnTo;
            _returnTo = null;

            // Taken by something else since — another dialog, a window: not ours to move.
            if (FocusedElement(_host) is Visual focused && !_host.IsVisualAncestorOf(focused))
            {
                return;
            }

            if (returnTo is not null && ReferenceEquals(TopLevel.GetTopLevel(returnTo), TopLevel.GetTopLevel(_host))
                && CanTakeFocus(returnTo))
            {
                returnTo.Focus();
            }
        }

        /// <summary>
        /// Notes the element that has the focus outside the dialog, in the dialog's own window — not one
        /// in a menu's popup, which closes.
        /// </summary>
        private void Remember()
        {
            if (FocusedElement(_host) is InputElement focused
                && !_host.IsVisualAncestorOf(focused)
                && ReferenceEquals(TopLevel.GetTopLevel(focused), TopLevel.GetTopLevel(_host)))
            {
                _returnTo = focused;
            }
        }

        private void StopWaiting()
        {
            if (_waiting)
            {
                _waiting = false;
                _host.LayoutUpdated -= OnLayoutUpdated;
            }
        }
    }
}
