using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>
    /// Реалистичный DXF 2004 в том виде, как его пишет транслятор Inventor для развертки:
    /// заголовок с $HANDSEED и кодовой страницей 1251, таблица слоёв, блоки пространств, контур со
    /// скруглениями (LWPOLYLINE с bulge), отверстие, линии гиба вверх и вниз, текст на кириллице, OBJECTS.
    /// Развертка 200 × 100 мм: линия гиба вверх на y = 40 (200 мм), вниз на x = 150 (100 мм).
    /// </summary>
    internal static class TestDxf
    {
        public const string CyrillicText = "Деталь Опора-01";

        public static string Inventor2004(string newline = "\r\n", string handSeed = "60")
        {
            var b = new Builder(newline);
            b.Pair(0, "SECTION").Pair(2, "HEADER")
             .Pair(9, "$ACADVER").Pair(1, "AC1018")
             .Pair(9, "$DWGCODEPAGE").Pair(3, "ANSI_1251")
             .Pair(9, "$INSUNITS").Pair(70, "4")
             .Pair(9, "$HANDSEED").Pair(5, handSeed)
             .Pair(0, "ENDSEC");

            b.Pair(0, "SECTION").Pair(2, "TABLES")
             .Pair(0, "TABLE").Pair(2, "LAYER").Pair(5, "2").Pair(100, "AcDbSymbolTable").Pair(70, "4");
            int handle = 0x10;
            foreach (var layer in new[] { "0", "IV_OUTER_PROFILE", "IV_INTERIOR_PROFILES", "IV_BEND", "IV_BEND_DOWN" })
            {
                b.Pair(0, "LAYER").Pair(5, Hex(handle++)).Pair(330, "2").Pair(100, "AcDbSymbolTableRecord")
                 .Pair(100, "AcDbLayerTableRecord").Pair(2, layer).Pair(70, "0").Pair(62, layer.StartsWith("IV_BEND") ? "2" : "7")
                 .Pair(6, "CONTINUOUS");
            }
            b.Pair(0, "ENDTAB").Pair(0, "ENDSEC");

            b.Pair(0, "SECTION").Pair(2, "BLOCKS")
             .Pair(0, "BLOCK").Pair(5, "20").Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, "0")
             .Pair(100, "AcDbBlockBegin").Pair(2, "*Model_Space").Pair(70, "0")
             .Pair(10, "0.0").Pair(20, "0.0").Pair(30, "0.0").Pair(3, "*Model_Space").Pair(1, "")
             .Pair(0, "ENDBLK").Pair(5, "21").Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, "0").Pair(100, "AcDbBlockEnd")
             .Pair(0, "ENDSEC");

            b.Pair(0, "SECTION").Pair(2, "ENTITIES");
            // Наружный контур 200 × 100 со скруглёнными углами R5 (bulge = tan(90°/4)).
            const string bulge = "0.4142135624";
            b.Pair(0, "LWPOLYLINE").Pair(5, "30").Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, "IV_OUTER_PROFILE")
             .Pair(100, "AcDbPolyline").Pair(90, "8").Pair(70, "1").Pair(43, "0.0")
             .Point(5, 0).Point(195, 0).Pair(42, bulge).Point(200, 5).Point(200, 95).Pair(42, bulge)
             .Point(195, 100).Point(5, 100).Pair(42, bulge).Point(0, 95).Point(0, 5).Pair(42, bulge);
            b.Pair(0, "CIRCLE").Pair(5, "31").Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, "IV_INTERIOR_PROFILES")
             .Pair(100, "AcDbCircle").Pair(10, "100.0").Pair(20, "70.0").Pair(30, "0.0").Pair(40, "6.0");
            b.Line("32", "IV_BEND", 0, 40, 200, 40);
            b.Line("33", "IV_BEND_DOWN", 150, 0, 150, 100);
            b.Pair(0, "TEXT").Pair(5, "34").Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, "0")
             .Pair(100, "AcDbText").Pair(10, "10.0").Pair(20, "10.0").Pair(30, "0.0").Pair(40, "3.5")
             .Pair(1, CyrillicText).Pair(100, "AcDbText");
            b.Pair(0, "ENDSEC");

            b.Pair(0, "SECTION").Pair(2, "OBJECTS")
             .Pair(0, "DICTIONARY").Pair(5, "C").Pair(330, "0").Pair(100, "AcDbDictionary").Pair(281, "1")
             .Pair(0, "ENDSEC")
             .Pair(0, "EOF");
            return b.ToString();
        }

        /// <summary>Байты файла в кодовой странице 1251, как их пишет Inventor (кириллица — один байт на символ).</summary>
        public static byte[] Cp1251(string text)
        {
            // В тестовой среде может не быть провайдера кодовых страниц — кодируем кириллицу вручную.
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c < 0x80) bytes[i] = (byte)c;
                else if (c >= 'А' && c <= 'я') bytes[i] = (byte)(0xC0 + (c - 'А'));
                else if (c == 'Ё') bytes[i] = 0xA8;
                else if (c == 'ё') bytes[i] = 0xB8;
                else throw new ArgumentException("символ вне теста: " + c);
            }
            return bytes;
        }

        private static string Hex(int value) => value.ToString("X", CultureInfo.InvariantCulture);

        private sealed class Builder
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private readonly string _nl;

            public Builder(string newline) => _nl = newline;

            public Builder Pair(int code, string value)
            {
                _sb.Append(code.ToString(CultureInfo.InvariantCulture).PadLeft(3)).Append(_nl).Append(value).Append(_nl);
                return this;
            }

            public Builder Point(double x, double y) =>
                Pair(10, x.ToString("0.0", CultureInfo.InvariantCulture)).Pair(20, y.ToString("0.0", CultureInfo.InvariantCulture));

            public Builder Line(string handle, string layer, double x1, double y1, double x2, double y2) =>
                Pair(0, "LINE").Pair(5, handle).Pair(330, "1F").Pair(100, "AcDbEntity").Pair(8, layer).Pair(62, "2")
                    .Pair(100, "AcDbLine")
                    .Pair(10, F(x1)).Pair(20, F(y1)).Pair(30, "0.0").Pair(11, F(x2)).Pair(21, F(y2)).Pair(31, "0.0");

            private static string F(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);

            public override string ToString() => _sb.ToString();
        }
    }

    /// <summary>Проверка структуры DXF: то, на что ругаются AutoCAD и программы раскроя.</summary>
    internal static class DxfValidator
    {
        public sealed class Entity
        {
            public string Type;
            public string Layer;
            public string Handle;
        }

        public static List<Entity> AssertValid(string dxf)
        {
            string nl = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var lines = dxf.Split(new[] { nl }, StringSplitOptions.None).ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            Assert.True(lines.Count % 2 == 0, "нечётное число строк: пары код/значение нарушены");
            Assert.DoesNotContain(lines, l => l.Contains("\r") || l.Contains("\n"));

            var pairs = new List<(int Code, string Value)>();
            for (int i = 0; i < lines.Count; i += 2)
            {
                Assert.True(int.TryParse(lines[i].Trim(), out int code), $"строка {i + 1}: не код группы «{lines[i]}»");
                pairs.Add((code, lines[i + 1]));
            }

            Assert.Equal((0, "EOF"), pairs.Last());
            int depth = 0;
            var handles = new List<string>();
            var entities = new List<Entity>();
            string section = null;
            Entity current = null;
            ulong seed = 0;

            for (int i = 0; i < pairs.Count; i++)
            {
                var (code, value) = pairs[i];
                if (code == 0)
                {
                    current = null;
                    if (value == "SECTION")
                    {
                        Assert.Equal(0, depth);
                        depth++;
                        section = pairs[i + 1].Value;
                    }
                    else if (value == "ENDSEC")
                    {
                        Assert.Equal(1, depth);
                        depth--;
                        section = null;
                    }
                    else if (section == "ENTITIES")
                    {
                        current = new Entity { Type = value };
                        entities.Add(current);
                    }
                }
                else if (code == 5 || code == 105)
                {
                    if (i > 0 && pairs[i - 1].Code == 9 && pairs[i - 1].Value == "$HANDSEED")
                        seed = Convert.ToUInt64(value, 16);
                    else
                    {
                        handles.Add(value);
                        if (current != null)
                            current.Handle = value;
                    }
                }
                else if (code == 8 && current != null)
                {
                    current.Layer = value;
                }
            }

            Assert.Equal(0, depth);
            var duplicates = handles.GroupBy(h => h, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, "повторяющиеся handle: " + string.Join(", ", duplicates));
            ulong max = handles.Select(h => Convert.ToUInt64(h, 16)).DefaultIfEmpty(0UL).Max();
            Assert.True(seed > max, $"$HANDSEED {seed:X} должен быть больше всех handle (максимальный {max:X})");
            Assert.All(entities, e => Assert.False(string.IsNullOrEmpty(e.Layer), $"у объекта {e.Type} нет слоя"));
            return entities;
        }
    }
}
