using System.IO;
using System.Threading.Tasks;
using Godot;

namespace MpFoundation.Dev;

/// <summary>
/// The one way this codebase turns the live viewport into a .png on disk.
///
/// <para>Every capture in the repo — the bot harness's <c>--capture-at</c> marks, the dev
/// labs' shot lists, and the human screenshot key — does the same four things in the same
/// order, and each of them has exactly one way to go quietly wrong: read the texture before
/// the frame has finished drawing and the file is a frame late; run headless and
/// <c>GetImage()</c> hands back nothing while <c>SavePng</c> still reports success, so the
/// caller gets a green log and no picture. Both traps are handled once, here.</para>
///
/// <para>Deliberately NOT a Node: a capture belongs to whatever is already in the tree, and
/// the caller passes itself in as the context whose viewport is read.</para>
/// </summary>
public static class ViewportCapture
{
    /// <summary>Saves <paramref name="context"/>'s viewport to an ABSOLUTE path, creating the
    /// directory if needed. Returns false — loudly, never silently — if there was nothing to
    /// photograph or the write failed. <paramref name="tag"/> prefixes the log line so a shot
    /// can be traced back to whoever asked for it.</summary>
    public static async Task<bool> SaveAsync(Node context, string absolutePath, string tag)
    {
        // A headless process has no rendered viewport. Saving one produces a plausible-looking
        // file and no image, which is worse than failing — the caller believes it has evidence.
        if (DisplayServer.GetName() == "headless")
        {
            GD.PushWarning($"[{tag}] capture skipped: running headless, there is no viewport to " +
                           "read. Launch windowed (no --headless) for captures.");
            return false;
        }
        try
        {
            // The frame the caller wants is the one currently being drawn, so wait for the draw
            // to finish rather than for the next process tick — a ProcessFrame await can land
            // between the request and the render and photograph the previous frame.
            await context.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string? dir = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            Image img = context.GetViewport().GetTexture().GetImage();
            Error err = img.SavePng(absolutePath);
            if (err != Error.Ok)
            {
                GD.PushError($"[{tag}] capture FAILED {err} -> {absolutePath}");
                return false;
            }
            GD.Print($"[{tag}] captured {absolutePath}");
            return true;
        }
        catch (System.Exception e)
        {
            GD.PushError($"[{tag}] capture failed: {e}");
            return false;
        }
    }
}
