using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NightDisplay
{
    public sealed partial class PanelWindow
    {
        // A view-only window keeps screenshot documentation independent of live display control.
        public static void RenderPreview(string outputPath, bool dark)
        {
            var app = Application.Current ?? new Application();
            Theme.Install(app.Resources, dark);
            var window = new PanelWindow(true) { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false };
            window.stateText.Text = "当前：实体屏可见";
            window.detailText.Text = "主显示器 · 2560 × 1440\n桌面与后台任务保持运行。";
            window.phoneText.Text = "网页控制已关闭。需要时在“设置与保护”中开启。";
            window.phoneCopyButton.IsEnabled = window.phoneOpenButton.IsEnabled = false;
            window.hotkeyText.Text = "快捷键已启用；恢复键在实体屏熄灭时也有效。";
            window.Show(); window.UpdateLayout();
            int width = (int)window.root.ActualWidth, height = (int)window.root.ActualHeight;
            // Mica cannot be captured, so the preview uses the solid fallback background.
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen()) {
                var bounds = new Rect(0, 0, width, height);
                context.DrawRectangle((Brush)app.Resources["WindowBackground"], null, bounds);
                // Map the brush to the layout box; by default it stretches to the drawn content and crops the margins.
                context.DrawRectangle(new VisualBrush(window.root) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = bounds, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, bounds);
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(outputPath)) encoder.Save(stream);
            window.Close();
        }
    }
}
