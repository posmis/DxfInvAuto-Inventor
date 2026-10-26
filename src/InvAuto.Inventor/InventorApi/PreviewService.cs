using System;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InvAuto.Core.Preview;

namespace InvAuto.InventorApi
{
    /// <summary>Миниатюры деталей и векторные превью разверток.</summary>
    public static class PreviewService
    {
        /// <summary>
        /// Миниатюра, которую Inventor сохраняет в файле (та же, что в Проводнике).
        /// null — если миниатюры нет (деталь ни разу не сохранялась).
        /// </summary>
        public static ImageSource Thumbnail(Inventor.Document document)
        {
            try
            {
                // Thumbnail имеет тип stdole.IPictureDisp. Через dynamic не нужна ссылка на сборку stdole.
                object picture = ((dynamic)document).Thumbnail;
                if (picture == null)
                    return null;

                using (var image = PictureConverter.ToImage(picture))
                {
                    if (image == null)
                        return null;

                    using (var stream = new MemoryStream())
                    {
                        image.Save(stream, ImageFormat.Png);
                        stream.Position = 0;

                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Превью развертки: DXF записывается тем же способом, что и при выгрузке (writer),
        /// во временный файл и читается обратно. Так превью совпадает с файлом, который уйдёт в резку.
        /// </summary>
        public static FlatDrawing FlatPattern(Inventor.PartDocument part, IFlatPatternWriter writer)
        {
            var definition = part.ComponentDefinition as Inventor.SheetMetalComponentDefinition;
            if (definition == null || !definition.HasFlatPattern)
                return null;

            string folder = Path.Combine(Path.GetTempPath(), "InvAuto");
            string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".dxf");
            try
            {
                Directory.CreateDirectory(folder);
                writer.Write(definition, path);
                return DxfReader.ReadFile(path);
            }
            catch
            {
                return null;
            }
            finally
            {
                try { File.Delete(path); } catch { /* временный файл */ }
            }
        }

        private sealed class PictureConverter : System.Windows.Forms.AxHost
        {
            private PictureConverter() : base(string.Empty) { }

            public static System.Drawing.Image ToImage(object picture) => GetPictureFromIPicture(picture);
        }
    }
}
