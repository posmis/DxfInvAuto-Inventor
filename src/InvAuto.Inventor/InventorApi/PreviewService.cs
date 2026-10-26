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

        private sealed class PictureConverter : System.Windows.Forms.AxHost
        {
            private PictureConverter() : base(string.Empty) { }

            public static System.Drawing.Image ToImage(object picture) => GetPictureFromIPicture(picture);
        }
    }
}
