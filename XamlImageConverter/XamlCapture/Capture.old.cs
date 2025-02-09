﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace CJC.XamlCapture {
  public static class Capture {
    internal static double defaultDpi = 96;

    /// <summary>
    /// Save snapshots of a UIElement with an optional clipping rectangle
    /// </summary>
    /// <param name="snapshot">Snapshot definition</param>
    /// <returns>Returns the captured <see cref="BitmapSource"/>s
    public static IEnumerable<BitmapSource> GetBitmaps(Snapshot snapshot) {
      var rootElement = (snapshot.Root ?? snapshot).Element;

      if (!snapshot.Frames.HasValue || snapshot.Frames.Value >= 1) {
        if (snapshot.Storyboard != null && rootElement != null) {
          var storyboard = snapshot.Storyboard;

          if (storyboard != null) {
            storyboard.Begin(rootElement, true);
            var clock = storyboard.CreateClock(false);
            var duration = clock.NaturalDuration.TimeSpan.TotalMilliseconds;

            var frames = Math.Max(snapshot.Frames ?? 1, 1);
            var step = (frames > 1) ? duration / (frames - 1) : 0;
            var time = (frames > 1) ? 0d : duration;

            for (var index = 0; index < frames; ++index, time += step) {
              storyboard.SeekNow(rootElement, time);
              yield return GetBitmap(snapshot);
            }

            storyboard.SeekNow(rootElement, 0);
            storyboard.Stop(rootElement);

            yield break;
          }
        }
      }

      yield return GetBitmap(snapshot);
    }

    /// <summary>
    /// Seek the Storyboard to the specified time and update the entire layout
    /// </summary>
    /// <param name="storyboard">Storyboard to seek</param>
    /// <param name="element">Root element for layout update</param>
    /// <param name="time">Time to seek (relative to beginning time of storyboard)</param>
    private static void SeekNow(this Storyboard storyboard, FrameworkElement element, double time) {
      storyboard.SeekAlignedToLastTick(element, TimeSpan.FromMilliseconds(time), TimeSeekOrigin.BeginTime);
      element.UpdateLayout();
    }

    /// <summary>
    /// Save a snapshot of a UIElement with an optional clipping rectangle
    /// </summary>
    /// <param name="element">Element to render</param>
    /// <param name="clipRect">Clipping rectangle (optional)</param>
    /// <returns>Returns a <see cref="BitmapSource"/> containing the snapshot</returns>
    public static BitmapSource GetBitmap(Snapshot snapshot) {
      var root = snapshot.Root ?? snapshot;
      var bounds = VisualTreeHelper.GetDescendantBounds(root.Element);
      var visual = new DrawingVisual();

      Group ancestor = snapshot;
      for (; ancestor.Parent != null && ancestor.Element == null; ancestor = (Group)ancestor.Parent) { }

      var origin = snapshot.TransformToAncestor(ancestor).Transform(new Point());
      var size = new Size(snapshot.ActualWidth, snapshot.ActualHeight);

      if (ancestor.Element != null) {
        origin = Point.Add(
          origin,
          (Vector)ancestor.Element.TransformToAncestor(root.Element).Transform(new Point()));

        size = new Size(
          size.Width > 0 ? size.Width : ancestor.Element.ActualWidth,
          size.Height > 0 ? size.Height : ancestor.Element.ActualHeight);
      }

      visual.Transform = new TranslateTransform(-origin.X, -origin.Y);

      using (DrawingContext context = visual.RenderOpen()) {
        context.DrawRectangle(new VisualBrush(root.Element), null, bounds);
      }

      var renderDpi = snapshot.RenderDpi ?? defaultDpi;

      var bitmap = new RenderTargetBitmap(
        (int)Math.Round(size.Width * renderDpi / defaultDpi),
        (int)Math.Round(size.Height * renderDpi / defaultDpi),
        renderDpi,
        renderDpi,
        PixelFormats.Pbgra32);

      bitmap.Render(visual);

      var dpi = snapshot.Dpi ?? defaultDpi;

      return (renderDpi != dpi)
        ? ScaleBitmap(bitmap, size, dpi)
        : bitmap;
    }

    /// <summary>
    /// Scale Bitmap to different DPI if required
    /// </summary>
    /// <param name="visual">Original Bitmap</param>
    /// <param name="size">Desired final size</param>
    /// <returns>Returns the scaled Bitmap</returns>
    private static BitmapSource ScaleBitmap(BitmapSource source, Size size, double dpi) {
      size = new Size(size.Width * dpi / defaultDpi, size.Height * dpi / defaultDpi);

      var visual = new DrawingVisual();

      using (var context = visual.RenderOpen()) {
        context.DrawRectangle(new ImageBrush(source), null, new Rect(size));
      }

      var bitmap = new RenderTargetBitmap(
        (int)Math.Round(size.Width),
        (int)Math.Round(size.Height),
        dpi,
        dpi,
        PixelFormats.Pbgra32);

      bitmap.Render(visual);

      return bitmap;
    }

    /// <summary>
    /// Encode bitmap(s) to a file
    /// </summary>
    /// <param name="encoder">Bitmap encoder</param>
    /// <param name="filename">Target filename</param>
    public static void Save(this BitmapEncoder encoder, string filename) {
      using (Stream stream = File.Create(filename)) {
        encoder.Save(stream);
      }
    }
  }
}