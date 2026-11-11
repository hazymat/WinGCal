using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
namespace WinGCal.Shell;

// Clear glass frost drawn by the page (SPEC.md › Window › Clear glass). Windows' own frost has one fixed strength and ignores window
// transparency, so for an adjustable frost the page blurs a copy of the desktop wallpaper, lined up with where the window sits on its monitor.
// This renders that monitor's wallpaper as Windows draws it (fill, fit, stretch, tile, centre, span, background colour) into a small image
public static class WallpaperFrost
{
    public record Picture(string DataUrl, Rectangle Monitor);
    static string lastKey = "";
    static Picture? lastPicture;

    // The wallpaper of the monitor holding this screen point, or null when Windows won't say (the caller falls back to Windows' frost)
    public static Picture? For(System.Drawing.Point screenPoint)
    {
        try
        {
            var wallpaper = (IDesktopWallpaper)new DesktopWallpaperClass();
            string monitorId = ""; Rectangle monitor = Rectangle.Empty;
            for (uint index = 0, count = wallpaper.GetMonitorDevicePathCount(); index < count; index++)
            {
                var id = wallpaper.GetMonitorDevicePathAt(index); var rect = wallpaper.GetMonitorRECT(id); var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (monitorId == "" || bounds.Contains(screenPoint)) { monitorId = id; monitor = bounds; if (bounds.Contains(screenPoint)) break; }
            }
            if (monitorId == "") return null;
            var position = wallpaper.GetPosition();
            var path = wallpaper.GetWallpaper(monitorId);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            var colourValue = wallpaper.GetBackgroundColor(); var background = Color.FromArgb((int)(colourValue & 0xFF), (int)(colourValue >> 8 & 0xFF), (int)(colourValue >> 16 & 0xFF));
            var key = $"{monitor}|{position}|{path}|{(File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0)}|{colourValue}";
            if (key == lastKey && lastPicture != null) return lastPicture;
            // Half size is plenty: it is always blurred, and keeps the message to the page small
            const double shrink = 0.5;
            using var canvas = new Bitmap(Math.Max(1, (int)(monitor.Width * shrink)), Math.Max(1, (int)(monitor.Height * shrink)));
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.Clear(background); graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (File.Exists(path))
                {
                    using var image = Image.FromFile(path);
                    // Span stretches one picture over all monitors: this monitor shows its slice of it
                    var area = position == WallpaperPosition.Span ? System.Windows.Forms.SystemInformation.VirtualScreen : monitor;
                    var drawn = Placement(position, image.Size, area.Size);
                    drawn.Offset(area.X - monitor.X, area.Y - monitor.Y);
                    graphics.ScaleTransform((float)shrink, (float)shrink);
                    if (position == WallpaperPosition.Tile) { for (var y = 0; y < monitor.Height; y += image.Height) for (var x = 0; x < monitor.Width; x += image.Width) graphics.DrawImage(image, x, y, image.Width, image.Height); }
                    else graphics.DrawImage(image, drawn);
                }
            }
            using var stream = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
            using var quality = new EncoderParameters(1); quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
            canvas.Save(stream, jpeg, quality);
            lastKey = key; lastPicture = new Picture("data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray()), monitor);
            return lastPicture;
        }
        catch (Exception wallpaperError) { CrashReporter.Write("Wallpaper for frost", wallpaperError, false); return null; }
    }

    // Where Windows draws the picture inside the area, for each Personalise › Background "fit" choice
    static Rectangle Placement(WallpaperPosition position, Size image, Size area)
    {
        double widthRatio = (double)area.Width / image.Width, heightRatio = (double)area.Height / image.Height;
        var scale = position switch { WallpaperPosition.Fill or WallpaperPosition.Span => Math.Max(widthRatio, heightRatio), WallpaperPosition.Fit => Math.Min(widthRatio, heightRatio), _ => 1.0 };
        if (position == WallpaperPosition.Stretch) return new Rectangle(0, 0, area.Width, area.Height);
        int width = (int)Math.Round(image.Width * scale), height = (int)Math.Round(image.Height * scale);
        return new Rectangle((area.Width - width) / 2, (area.Height - height) / 2, width, height);
    }

    enum WallpaperPosition { Center = 0, Tile = 1, Stretch = 2, Fit = 3, Fill = 4, Span = 5 }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")] class DesktopWallpaperClass { }
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
        uint GetMonitorDevicePathCount();
        NativeRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(WallpaperPosition position);
        WallpaperPosition GetPosition();
    }
}

// Keeps one window's page supplied with the wallpaper behind it: the picture once (again only when it changes), then where the window sits
public sealed class WallpaperFeed
{
    WallpaperFrost.Picture? picture;
    string sentImage = "";
    public void Forget() => sentImage = ""; // the page reloaded
    public void Off() { picture = null; sentImage = ""; }
    // The "wallpaper" message for this window, or null when its wallpaper can't be read
    public string? Message(System.Windows.FrameworkElement content, double width, double height, bool canSendImage, bool recheck = false)
    {
        if (System.Windows.PresentationSource.FromVisual(content) == null) return null;
        var origin = content.PointToScreen(new System.Windows.Point(0, 0));
        var centre = new System.Drawing.Point((int)origin.X + (int)(width / 2), (int)origin.Y + (int)(height / 2));
        // Moving within the same monitor only shifts the picture; another monitor, or a settings change (new wallpaper), reads it again
        if (recheck || picture == null || !picture.Monitor.Contains(centre)) picture = WallpaperFrost.For(centre);
        if (picture == null) return null;
        var sendImage = canSendImage && sentImage != picture.DataUrl;
        if (sendImage) sentImage = picture.DataUrl;
        var monitor = picture.Monitor;
        return System.Text.Json.JsonSerializer.Serialize(new { type = "wallpaper", image = sendImage ? picture.DataUrl : null, monitor = new { x = monitor.X, y = monitor.Y, width = monitor.Width, height = monitor.Height }, origin = new { x = (int)origin.X, y = (int)origin.Y }, frost = WinGCal.App.Instance.Settings.Look.PanelFrost });
    }
}
