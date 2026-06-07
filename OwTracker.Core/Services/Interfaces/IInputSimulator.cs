using System.Drawing;

namespace OwTracker.Core.Services.Interfaces;

public interface IInputSimulator
{
    /// <summary>Brings the OW window to the foreground. Must be called before any input.</summary>
    bool BringOwToForeground();

    /// <summary>Sends a left mouse click at the given screen coordinate.</summary>
    Task ClickAsync(Point screenPoint, CancellationToken ct = default);

    /// <summary>Moves the cursor to a screen coordinate without clicking (to trigger hover).</summary>
    Task MoveMouseAsync(Point screenPoint, CancellationToken ct = default);

    /// <summary>Presses and releases a virtual key.</summary>
    Task SendKeyAsync(ushort vk, CancellationToken ct = default);

    /// <summary>Presses ESC (navigates back / opens escape menu).</summary>
    Task PressEscapeAsync(CancellationToken ct = default);

    /// <summary>
    /// Scrolls the mouse wheel at <paramref name="screenPoint"/>. Positive notches scroll up,
    /// negative scroll down. The cursor is moved over the point first so the scroll lands on
    /// the intended scrollable control.
    /// </summary>
    Task ScrollAsync(Point screenPoint, int notches, CancellationToken ct = default);
}
