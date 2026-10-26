using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace InvAuto.Framework
{
    /// <summary>
    /// Загружает иконку кнопки из встроенных ресурсов: Resources\Icons\{FeatureId}.{size}.png.
    /// Возвращает IPictureDisp (то, что принимает AddButtonDefinition) или null, если иконки нет.
    /// </summary>
    internal static class IconLoader
    {
        public static object Load(string featureId, int size)
        {
            var assembly = typeof(IconLoader).Assembly;
            string suffix = $".Resources.Icons.{featureId}.{size}.png";
            string resource = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase));
            if (resource == null)
                return null;

            using (var stream = assembly.GetManifestResourceStream(resource))
            using (var image = Image.FromStream(stream))
                return PictureConverter.ToPictureDisp(new Bitmap(image));
        }

        private sealed class PictureConverter : AxHost
        {
            private PictureConverter() : base(string.Empty) { }

            public static object ToPictureDisp(Image image) => GetIPictureDispFromPicture(image);
        }
    }
}
