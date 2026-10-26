using System;
using System.Collections.Generic;
using InvAuto.Core.Bends;
using InvAuto.Core.Preview;
using IO = System.IO;

namespace InvAuto.InventorApi
{
    public enum FlatPreviewState
    {
        /// <summary>Развертка ещё не сформирована (или устарела).</summary>
        NotGenerated,
        Ready,
        /// <summary>В детали нет развертки.</summary>
        NoFlatPattern,
        Failed
    }

    /// <summary>Сформированная развертка: «сырой» DXF транслятора, до обработки линий гиба.</summary>
    public sealed class FlatPreview
    {
        private FlatPreview(FlatPreviewState state, string rawText, string dxfPath, string error)
        {
            State = state;
            RawText = rawText;
            DxfPath = dxfPath;
            Error = error;
        }

        public FlatPreviewState State { get; }

        /// <summary>Текст DXF до обработки линий гиба.</summary>
        public string RawText { get; }

        /// <summary>DXF в кэше — его же сможет открыть окно просмотра развертки.</summary>
        public string DxfPath { get; }

        public string Error { get; }

        public static FlatPreview NotGenerated { get; } = new FlatPreview(FlatPreviewState.NotGenerated, null, null, null);
        public static FlatPreview NoFlatPattern { get; } = new FlatPreview(FlatPreviewState.NoFlatPattern, null, null, null);
        public static FlatPreview Failed(string error) => new FlatPreview(FlatPreviewState.Failed, null, null, error);

        public static FlatPreview Load(string path) =>
            new FlatPreview(FlatPreviewState.Ready, IO.File.ReadAllText(path, DxfBendProcessor.FileEncoding), path, null);

        /// <summary>Развертка для показа: линии гиба обработаны так же, как будут при выгрузке.</summary>
        public (FlatDrawing Drawing, IReadOnlyList<BendLineLayout> BendLines) Render(BendLineOptions bends, IEnumerable<string> bendLayers)
        {
            if (State != FlatPreviewState.Ready)
                return (null, Array.Empty<BendLineLayout>());

            var processed = DxfBendProcessor.Process(RawText, bendLayers, bends);
            return (DxfReader.Read(new IO.StringReader(processed.Text)), processed.Lines);
        }
    }

    /// <summary>
    /// Развертки для просмотра перед выгрузкой. DXF пишется тем же способом, что и при выгрузке
    /// (тот же ini), во временную папку-кэш. Пока деталь не изменилась, развертка берётся из кэша
    /// без обращения к транслятору. В кэше лежит DXF до обработки линий гиба — их параметры
    /// применяются при показе и при выгрузке.
    /// </summary>
    public sealed class FlatPreviewService
    {
        private readonly IFlatPatternWriter _writer;
        private readonly FlatPreviewCache _cache;

        public FlatPreviewService(IFlatPatternWriter writer, FlatPreviewCache cache)
        {
            _writer = writer;
            _cache = cache;
        }

        public static string DefaultCacheFolder => IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InvAuto", "FlatCache");

        /// <summary>Только кэш: быстро, без транслятора. NotGenerated — если в кэше нет актуальной развертки.</summary>
        public FlatPreview FromCache(Inventor.PartDocument part)
        {
            string path = CachePath(part);
            if (path == null || !IO.File.Exists(path))
                return FlatPreview.NotGenerated;

            try
            {
                var preview = FlatPreview.Load(path);
                TouchQuietly(path); // используемые развертки не попадают под очистку старых
                return preview;
            }
            catch (Exception ex)
            {
                return FlatPreview.Failed("не удалось прочитать DXF из кэша: " + ex.Message);
            }
        }

        /// <summary>Формирует развертку (DXF в кэш) и загружает её для показа.</summary>
        public FlatPreview Generate(Inventor.PartDocument part)
        {
            if (!(part.ComponentDefinition is Inventor.SheetMetalComponentDefinition definition))
                return FlatPreview.Failed("деталь не листовая");
            if (!definition.HasFlatPattern)
                return FlatPreview.NoFlatPattern;

            // Несохранённую деталь пишем во временный файл вне кэша: дата файла ещё не отражает изменений.
            string path = CachePath(part) ?? IO.Path.Combine(_cache.Folder, "unsaved", Guid.NewGuid().ToString("N") + ".dxf");
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
            string temp = path + ".tmp.dxf";

            _writer.Write(definition, temp);
            if (IO.File.Exists(path))
                IO.File.Delete(path);
            IO.File.Move(temp, path);

            return FlatPreview.Load(path);
        }

        /// <summary>
        /// Кладёт в кэш DXF, только что записанный при выгрузке тем же способом (до обработки линий гиба).
        /// null — если кэш для детали неприменим.
        /// </summary>
        public FlatPreview StoreRaw(Inventor.PartDocument part, string rawDxfPath)
        {
            string path = CachePath(part);
            if (path == null || !IO.File.Exists(rawDxfPath))
                return null;

            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
            IO.File.Copy(rawDxfPath, path, true);
            return FlatPreview.Load(path);
        }

        private static void TouchQuietly(string path)
        {
            try { IO.File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch { /* не критично */ }
        }

        /// <summary>null — кэш для детали неприменим (не сохранена или изменена в Inventor).</summary>
        private string CachePath(Inventor.PartDocument part)
        {
            var document = (Inventor.Document)part;
            string file = part.FullFileName;
            if (string.IsNullOrEmpty(file) || !IO.File.Exists(file) || document.Dirty)
                return null;
            return _cache.PathFor(file, IO.File.GetLastWriteTimeUtc(file), _writer.Signature);
        }
    }
}
