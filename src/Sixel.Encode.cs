using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace SixPix;

public partial class Sixel
{
    const string SixelStart = "\x1bP7;1;q\"1;1";
    const string SixelEnd = "\x1b\\";

    const byte specialChNr = (byte)0x6d;
    const byte specialChCr = (byte)0x64;

    /// <summary>
    /// Encode Image stream to Sixel string
    /// </summary>
    /// <param name="stream">Image Stream</param>
    /// <returns>Sxiel string</returns>
    public static ReadOnlySpan<char> Encode(Stream stream)
    {
        using var img = Image.Load<Rgb24>(stream);
        // 減色処理
        img.Mutate(x => {
            x.Quantize(KnownQuantizers.Octree);
        });
        var width = img.Width;
        var height = img.Height;

        DebugPrint($"Width: {width}, Height: {height}, (bpp={img.PixelType.BitsPerPixel})", lf: true);

        // カラーパレットの構築
        // XXX: もっと良い方法がありそう
        ReadOnlySpan<Rgb24> colorPalette;
        using (var quantizer = KnownQuantizers.Octree.CreatePixelSpecificQuantizer<Rgb24>(img.Configuration))
        {
            quantizer.BuildPalette(new DefaultPixelSamplingStrategy(), img);
            colorPalette = new HashSet<Rgb24>(quantizer.Palette.ToArray()).ToArray();
        }

        //
        // https://github.com/mattn/go-sixel/blob/master/sixel.go の丸パクリです！！
        //
        var sb = new StringBuilder();
        // DECSIXEL Introducer(\033P0;0;8q) + DECGRA ("1;1): Set Raster Attributes
        sb.Append(SixelStart)
          .Append($";{width};{height}");

        DebugPrint($"Pallete Start Length={colorPalette.Length}", lf: true);

        int colorPaletteLength = colorPalette.Length;
        for (var i = 0; i < colorPaletteLength; i++)
        {
            var rgb = colorPalette[i];
            var (r, g, b) = (rgb.R * 100 / 0xFF, rgb.G * 100 / 0xFF, rgb.B * 100 / 0xFF);
            // DECGCI (#): Graphics Color Introducer
            sb.Append($"#{i};2;{r:d};{g:d};{b:d}");
            DebugPrint($"#{i};2;", ConsoleColor.Red);
            DebugPrint($"{r:d};{g:d};{b:d}", ConsoleColor.Green, true);
        }
        DebugPrint("End Palette", ConsoleColor.DarkGray, true);

        var buffer = new byte[width * colorPaletteLength];
        var cset = new bool[colorPaletteLength]; // 表示すべきカラーパレットがあるかのフラグ
        var ch0 = specialChNr;
        for (var (z, y) = (0, 0); z < (height + 5) / 6; z++, y = z * 6)
        {
            if (z > 0) {
                // DECGNL (-): Graphics Next Line
                sb.Append('-');
                DebugPrint("-", lf: true);
            }
            DebugPrint($"[{z}]", ConsoleColor.DarkGray);
            for (var p = 0; p < 6 && y < height; p++, y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var idx = colorPalette.IndexOf(img[x, y]);
                    cset[idx] = true;
                    buffer[width * idx + x] |= (byte)(1 << p);
                }
            }
            bool first = true;
            for (var n = 0; n < colorPaletteLength; n++)
            {
                if (!cset[n]) continue;

                cset[n] = false;
                if (ch0 == specialChCr && !first)
                {
                    // DECGCR ($): Graphics Carriage Return
                    sb.Append('$');
                    DebugPrint("$");
                }
                first = false;

                sb.Append($"#{n}");
                DebugPrint($"#{n}", ConsoleColor.Red, false);
                var cnt = 0;
                for (var x = 0; x < width; x++)
                {
                    // make sixel character from 6 pixels
                    var bufIndex = width * n + x;
                    var ch = buffer[bufIndex];
                    buffer[bufIndex] = 0;
                    if (ch0 < 0x40 && ch != ch0)
                    {
                        var sixelChar = (char)(63 + ch0);
                        for (; cnt > 255; cnt -= 255)
                        {
                            sb.Append("!255").Append(sixelChar);
                            DebugPrint($"!255{sixelChar}", ConsoleColor.Yellow);
                        }
                        if (cnt == 1)
                        {
                            sb.Append(sixelChar);
                            DebugPrint($"{sixelChar}", ConsoleColor.Yellow);
                        }
                        else if (cnt == 2)
                        {
                            sb.Append([sixelChar, sixelChar]);
                            DebugPrint($"{sixelChar}{sixelChar}", ConsoleColor.Yellow);
                        }
                        else if (cnt == 3)
                        {
                            sb.Append([sixelChar, sixelChar, sixelChar]);
                            DebugPrint($"{sixelChar}{sixelChar}{sixelChar}", ConsoleColor.Yellow);
                        }
                        else
                        {
                            sb.Append($"!{cnt}").Append(sixelChar);
                            DebugPrint($"!{cnt}{sixelChar}", ConsoleColor.Yellow);
                        }
                        cnt = 0;
                    }
                    ch0 = ch;
                    cnt++;
                }
                if (ch0 != 0)
                {
                    var sixelChar = (char)(63 + ch0);
                    for (; cnt > 255; cnt -= 255)
                    {
                        sb.Append("!255").Append(sixelChar);
                        DebugPrint($"!255{sixelChar}", ConsoleColor.Cyan);
                    }
                    if (cnt == 1)
                    {
                        sb.Append(sixelChar);
                        DebugPrint($"{sixelChar}", ConsoleColor.Cyan);
                    }
                    else if (cnt == 2)
                    {
                        sb.Append([sixelChar, sixelChar]);
                        DebugPrint($"{sixelChar}{sixelChar}", ConsoleColor.Cyan);
                    }
                    else if (cnt == 3)
                    {
                        sb.Append([sixelChar, sixelChar, sixelChar]);
                        DebugPrint($"{sixelChar}{sixelChar}{sixelChar}", ConsoleColor.Cyan);
                    }
                    else
                    {
                        sb.Append($"!{cnt}").Append(sixelChar);
                        DebugPrint($"!{cnt}{sixelChar}", ConsoleColor.Cyan);
                    }
                    cnt = 0;
                }
                ch0 = specialChCr;
            }
        }
        sb.Append(SixelEnd);
        DebugPrint("End", ConsoleColor.DarkGray, true);
        return sb.ToString();
    }
}
