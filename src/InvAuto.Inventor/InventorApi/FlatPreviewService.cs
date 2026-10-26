using System;
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

    public sealed class FlatPreview
    {
        private FlatPreview(FlatPreviewState state, FlatDrawing drawing, string dxfPath, string error)
        {
            State = state;
            Drawing = drawing;
            DxfPath = dxfPath;
            Error = error;
        }

        public FlatPreviewState State { get; }
        public FlatDrawing Drawing { get; }

        /// <summary>DXF в кэше — его же сможет открыть окно просмотра развертки.</summary>
        public string DxfPath { get; }

        public string Error { get; }

        public static FlatPreview NotGenerated { get; } = new FlatPreview(FlatPreviewState.NotGenerated, null, null, null);
        public static FlatPreview NoFlatPattern { get; } = new FlatPreview(FlatPreviewState.NoFlatPattern, null, null, null);
        public static FlatPreview Ready(FlatDrawing drawing, string path) => new FlatPreview(FlatPreviewState.Ready, drawing, path, null);
        public static FlatPreview Failed(string error) => new FlatPreview(FlatPreviewState.Failed, null, null, error);
    }

    /// <summary>
    /// Развертки для просмотра перед выгрузкой. DXF пишется тем же способом, что и при выгрузке
    /// (тот же ini), во временную папку-кэш, и показывается в окне. Пока деталь не изменилась,
    /// развертка берётся из кэша без обращения к транслятору.
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
                var drawing = DxfReader.ReadFile(path);
                TouchQuietly(path); // используемые развертки не попадают под очистку старых
                return FlatPreview.Ready(drawing, path);
            }
            catch (Exception ex)
            {
                return FlatPreview.Failed("не удалось прочитать DXF из кэша: " + ex.Message);
            }
        }

        /// <summary>Формирует развертку (DXF в кэш) и читает её для показа.</summary>
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

            return FlatPreview.Ready(DxfReader.ReadFile(path), path);
        }

        /// <summary>
        /// Кладёт в кэш DXF, выгруженный тем же способом (те же настройки). null — если кэш для детали неприменим.
        /// </summary>
        public FlatPreview StoreExported(Inventor.PartDocument part, string dxfPath)
        {
            string path = CachePath(part);
            if (path == null || !IO.File.Exists(dxfPath))
                return null;

            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
            IO.File.Copy(dxfPath, path, true);
            return FlatPreview.Ready(DxfReader.ReadFile(path), path);
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
