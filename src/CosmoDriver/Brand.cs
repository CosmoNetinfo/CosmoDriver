using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CosmoDriver;

internal static class Brand
{
    private const string Arc = "M 93 38 A 40 40 0 1 0 93 90";
    private const string Arrow = "M 80 30 L 104 30 L 104 54 Z";
    private const string Chip = "M 54 50 L 74 50 Q 78 50 78 54 L 78 74 Q 78 78 74 78 L 54 78 Q 50 78 50 74 L 50 54 Q 50 50 54 50 Z";
    private const string Pins = "M 57 44 L 57 50 M 71 44 L 71 50 M 57 78 L 57 84 M 71 78 L 71 84 M 44 57 L 50 57 M 44 71 L 50 71 M 78 57 L 84 57 M 78 71 L 84 71";
    private const string Orbit = "M 20 88 C -6 116 64 108 105 64 C 126 41 127 23 108 28";
    private const string Gradient = "<defs><linearGradient id=\"brand\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop stop-color=\"#38D9F6\"/><stop offset=\"1\" stop-color=\"#2876F9\"/></linearGradient></defs>";

    public static DrawingImage Symbol(bool dark = true)
    {
        var group = new DrawingGroup();
        var blue = new LinearGradientBrush(Color.FromRgb(56,217,246), Color.FromRgb(40,118,249), 45);
        var orbit = new SolidColorBrush(Color.FromRgb(82,167,245));
        group.Children.Add(new GeometryDrawing(null, new Pen(blue, 12) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse(Arc)));
        group.Children.Add(new GeometryDrawing(blue, null, Geometry.Parse(Arrow)));
        group.Children.Add(new GeometryDrawing(dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(17,43,61)), null, Geometry.Parse(Chip)));
        group.Children.Add(new GeometryDrawing(null, new Pen(dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(17,43,61)), 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse(Pins)));
        group.Children.Add(new GeometryDrawing(null, new Pen(orbit, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse(Orbit)));
        group.Children.Add(new GeometryDrawing(blue, null, new EllipseGeometry(new Point(110,27), 5,5)));
        group.Freeze(); return new DrawingImage(group);
    }

    public static void Export(string directory, string iconPath)
    {
        Directory.CreateDirectory(directory);
        string SvgSymbol(bool dark) => Gradient + $"<path d=\"{Arc}\" fill=\"none\" stroke=\"url(#brand)\" stroke-width=\"12\" stroke-linecap=\"round\"/><path d=\"{Arrow}\" fill=\"url(#brand)\"/><path d=\"{Chip}\" fill=\"{(dark ? "#FFFFFF" : "#112B3D")}\"/><path d=\"{Pins}\" stroke=\"{(dark ? "#FFFFFF" : "#112B3D")}\" stroke-width=\"3\" stroke-linecap=\"round\"/><path d=\"{Orbit}\" fill=\"none\" stroke=\"#52A7F5\" stroke-width=\"3\" stroke-linecap=\"round\"/><circle cx=\"110\" cy=\"27\" r=\"5\" fill=\"url(#brand)\"/>";
        foreach (bool dark in new[] { true, false })
        {
            string suffix = dark ? "dark" : "light";
            File.WriteAllText(Path.Combine(directory, $"cosmodriver-symbol-{suffix}.svg"), $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 128 128\" role=\"img\" aria-label=\"CosmoDriver\">{SvgSymbol(dark)}</svg>", new UTF8Encoding(false));
            string ink = dark ? "#FFFFFF" : "#112B3D";
            File.WriteAllText(Path.Combine(directory, $"cosmodriver-logo-{suffix}.svg"), $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 580 140\" role=\"img\" aria-label=\"CosmoDriver by CosmoNet\"><g transform=\"translate(0 6)\">{SvgSymbol(dark)}</g><text x=\"150\" y=\"77\" fill=\"{ink}\" font-family=\"Segoe UI, sans-serif\" font-size=\"52\" font-weight=\"600\">Cosmo<tspan fill=\"#289CF3\">Driver</tspan></text><text x=\"153\" y=\"107\" fill=\"{ink}\" font-family=\"Segoe UI, sans-serif\" font-size=\"18\" letter-spacing=\"2\">BY COSMONET</text></svg>", new UTF8Encoding(false));
            SaveBitmap(Symbol(dark), 512, Path.Combine(directory, $"cosmodriver-symbol-{suffix}.png"));
        }
        var frames = new List<(int Size, byte[] Data)>();
        foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(16,35,53)), null, new Rect(0,0,size,size), size * .2,size * .2);
                dc.DrawImage(Symbol(), new Rect(size*.08,size*.08,size*.84,size*.84));
            }
            var bitmap = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream(); encoder.Save(stream); frames.Add((size,stream.ToArray()));
        }
        using (var writer = new BinaryWriter(File.Create(iconPath)))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Count);
            uint offset = (uint)(6 + frames.Count * 16);
            foreach (var frame in frames)
            {
                writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); writer.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)frame.Data.Length); writer.Write(offset); offset += (uint)frame.Data.Length;
            }
            foreach (var frame in frames) writer.Write(frame.Data);
        }
        var board = new DrawingVisual();
        using (var dc = board.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10,24,39)),null,new Rect(0,0,1600,960));
            DrawText(dc,"COSMODRIVER / IDENTITÀ 01",42,44,16,Color.FromRgb(151,181,208));
            dc.DrawImage(Symbol(),new Rect(96,150,230,230));
            DrawText(dc,"Cosmo",386,202,74,Colors.White, true); DrawText(dc,"Driver",620,202,74,Color.FromRgb(51,178,250),true);
            DrawText(dc,"BY COSMONET",392,306,21,Color.FromRgb(151,181,208));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(244,248,252)),null,new Rect(40,466,1000,360));
            dc.DrawImage(Symbol(false),new Rect(82,516,178,178));
            DrawText(dc,"Cosmo",290,548,54,Color.FromRgb(17,43,61),true); DrawText(dc,"Driver",463,548,54,Color.FromRgb(34,143,231),true);
            DrawText(dc,"BY COSMONET",294,626,16,Color.FromRgb(78,104,124));
            DrawText(dc,"ICONA APP",1120,490,14,Color.FromRgb(151,181,208));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(16,35,53)),new Pen(new SolidColorBrush(Color.FromRgb(44,71,95)),1),new Rect(1110,542,150,150),30,30);
            dc.DrawImage(Symbol(),new Rect(1125,557,120,120));
            dc.DrawImage(Symbol(),new Rect(1300,600,48,48)); dc.DrawImage(Symbol(),new Rect(1380,610,32,32));
            DrawText(dc,"Orbite, circuiti, aggiornamenti.",44,882,22,Color.FromRgb(151,181,208));
        }
        var preview = new RenderTargetBitmap(1600,960,96,96,PixelFormats.Pbgra32); preview.Render(board);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(preview)); using var file = File.Create(Path.Combine(directory,"cosmodriver-brand-preview.png")); png.Save(file);
    }

    private static void SaveBitmap(DrawingImage image,int size,string path)
    {
        var visual = new DrawingVisual(); using(var dc = visual.RenderOpen()) dc.DrawImage(image,new Rect(0,0,size,size));
        var bitmap = new RenderTargetBitmap(size,size,96,96,PixelFormats.Pbgra32); bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
    }
    private static void DrawText(DrawingContext dc,string text,double x,double y,double size,Color color,bool bold=false) =>
        dc.DrawText(new FormattedText(text,CultureInfo.GetCultureInfo("it-IT"),FlowDirection.LeftToRight,new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,bold?FontWeights.SemiBold:FontWeights.Normal,FontStretches.Normal),size,new SolidColorBrush(color),1),new Point(x,y));
}
