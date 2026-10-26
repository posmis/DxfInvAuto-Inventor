using System.Globalization;
using System.Windows.Media;
using InvAuto.Core.Export;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.SheetMetal
{
    public enum RowStatus
    {
        None,
        Ok,
        Warning,
        Error
    }

    /// <summary>Строка списка листовых деталей.</summary>
    public sealed class PartRow : ObservableObject
    {
        private bool _isChecked = true;
        private int _total;
        private string _status = string.Empty;
        private RowStatus _statusKind;
        private ImageSource _thumbnail;
        private FlatDrawing _flat;

        public PartRow(SheetMetalPart part, int productQuantity)
        {
            Part = part;
            _total = part.Quantity * productQuantity;
        }

        public SheetMetalPart Part { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set => Set(ref _isChecked, value);
        }

        public string FileName => Part.Name;
        public string FilePath => Part.FullFileName;
        public string PartNumber => Part.PartNumber;
        public string Description => Part.Description;
        public string Material => Part.Material;
        public double Thickness => Part.ThicknessMm;
        public int Quantity => Part.Quantity;
        public bool HasFlatPattern => Part.HasFlatPattern;
        public string FlatState => Part.HasFlatPattern ? "Есть" : "Нет";

        public string FlatSize => Part.FlatLengthMm > 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} × {1:0.#}", Part.FlatLengthMm, Part.FlatWidthMm)
            : string.Empty;

        public string FlatSizeText => FlatSize.Length > 0 ? FlatSize + " мм" : string.Empty;

        /// <summary>Количество с учётом числа изделий.</summary>
        public int Total
        {
            get => _total;
            set => Set(ref _total, value);
        }

        public string Status
        {
            get => _status;
            set => Set(ref _status, value);
        }

        public RowStatus StatusKind
        {
            get => _statusKind;
            set => Set(ref _statusKind, value);
        }

        public ImageSource Thumbnail
        {
            get => _thumbnail;
            set => Set(ref _thumbnail, value);
        }

        public FlatDrawing Flat
        {
            get => _flat;
            set => Set(ref _flat, value);
        }

        public bool PreviewLoaded { get; set; }

        public string Property(string name) =>
            Part.CustomProperties.TryGetValue(name, out var value) ? value : string.Empty;

        public void ApplyResult(ExportResult result)
        {
            switch (result.Status)
            {
                case ExportStatus.Exported:
                    StatusKind = result.Message.Length > 0 ? RowStatus.Warning : RowStatus.Ok;
                    Status = result.Message.Length > 0 ? "Выгружено: " + result.Message : "Выгружено";
                    break;
                case ExportStatus.Skipped:
                    StatusKind = RowStatus.Warning;
                    Status = "Пропущено: " + result.Message;
                    break;
                default:
                    StatusKind = RowStatus.Error;
                    Status = "Ошибка: " + result.Message;
                    break;
            }
        }
    }
}
