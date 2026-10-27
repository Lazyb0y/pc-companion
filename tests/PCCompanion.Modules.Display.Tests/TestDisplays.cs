using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using PCCompanion.Modules.Display.DisplayProfiles;
using PCCompanion.Platform.Windows;

namespace PCCompanion.Modules.Display.Tests;

// Displays and setups as Windows would report them, on one adapter with three sources.
internal static class TestDisplays
{
    public static CurrentSetup Current(IReadOnlyList<ConnectedDisplay> displays, IReadOnlyList<DisplayPath> paths, params (ConnectedDisplay Display, string Serial)[] serials) =>
        new(new DisplaySetup(displays, paths), serials.ToDictionary(pair => pair.Display.DevicePath, pair => pair.Serial));

    public static ConnectedDisplay Connected(uint target, string model = "DELA0A3", DisplayConnection connection = DisplayConnection.DisplayPort, string name = "DELL U2720Q") =>
        new(
            new DisplayTarget(1, target),
            $@"\\?\DISPLAY#{(model.Length > 0 ? model : "Default_Monitor")}#5&1f2e3d4c&0&UID{target}#{{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}}",
            model,
            name,
            connection,
            [new DisplaySource(1, 0), new DisplaySource(1, 1), new DisplaySource(1, 2)]);

    public static DisplayPath Path(ConnectedDisplay display, uint source, int x, int y, Size? area = null, DisplayOrientation orientation = DisplayOrientation.Landscape, DisplayHdr hdr = DisplayHdr.NotSupported) =>
        new(new DisplaySource(1, source), display.Target, new Rectangle(new Point(x, y), area ?? new Size(1920, 1080)), new DisplayRefreshRate(60000, 1000), orientation, hdr);
}
