using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Marking;
using InvAuto.Core.Materials;
using InvAuto.Core.Preflight;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;
using InvAuto.InventorApi;

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
        private bool _isChecked;
        private int _total;
        private string _status = string.Empty;
        private RowStatus _statusKind;
        private ImageSource _thumbnail;
        private FlatDrawing _flat;
        private FlatPreviewState _flatPreviewState;
        private string _flatPreviewError = string.Empty;

        public PartRow(SheetMetalPart part, int productQuantity)
        {
            Part = part;
            _total = part.Quantity * productQuantity;
            _flatPreviewState = part.HasFlatPattern ? FlatPreviewState.NotGenerated : FlatPreviewState.NoFlatPattern;
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
            set
            {
                if (Set(ref _total, value))
                    OnPropertyChanged(nameof(TotalMass));
            }
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

        public FlatPreviewState FlatPreviewState
        {
            get => _flatPreviewState;
            private set
            {
                if (Set(ref _flatPreviewState, value))
                    OnPropertyChanged(nameof(FlatPreviewText));
            }
        }

        /// <summary>Подпись в ячейке развертки, когда картинки нет.</summary>
        public string FlatPreviewText
        {
            get
            {
                switch (FlatPreviewState)
                {
                    case FlatPreviewState.NotGenerated: return "не сформирована";
                    case FlatPreviewState.NoFlatPattern: return "нет развертки";
                    case FlatPreviewState.Failed: return "ошибка";
                    default: return string.Empty;
                }
            }
        }

        public string FlatPreviewError
        {
            get => _flatPreviewError;
            private set => Set(ref _flatPreviewError, value ?? string.Empty);
        }

        /// <summary>DXF развертки в кэше — для будущего окна просмотра.</summary>
        public string FlatDxfPath { get; private set; }

        public bool ThumbnailLoaded { get; set; }

        /// <summary>Кэш разверток для строки уже проверен.</summary>
        public bool FlatCacheChecked { get; set; }

        /// <summary>Исходная (до обработки линий гиба) развертка.</summary>
        public FlatPreview Preview { get; private set; } = FlatPreview.NotGenerated;

        /// <summary>Линии гиба развертки и что с ними будет сделано при выгрузке.</summary>
        public IReadOnlyList<ProcessedBendLine> BendLines { get; private set; } = Array.Empty<ProcessedBendLine>();

        private BendLineOptions _bends = new BendLineOptions();
        private Func<BendLineInfo, BendLineOptions> _resolve = _ => null;
        private bool _bendsPersonal;
        private int _personalLines;
        private IReadOnlyCollection<string> _bendLayers = DxfBendProcessor.DefaultLayers;

        /// <summary>Параметры линий гиба на уровне детали (без своих настроек отдельных линий).</summary>
        public BendLineOptions Bends => _bends;

        /// <summary>Для колонки «Линии гиба»: режим, пометка о персональной настройке и число линий со своими.</summary>
        public string BendSummary =>
            (_bendsPersonal ? "своя: " : string.Empty) + _bends.FullSummary +
            (_personalLines > 0 ? $" · своих линий: {_personalLines}" : string.Empty);

        public bool BendsPersonal => _bendsPersonal;

        private MarkingOptions _markingOptions = new MarkingOptions();
        private bool _markingPersonal;

        /// <summary>Параметры маркировки детали (свои или общие).</summary>
        public MarkingOptions MarkingOptions => _markingOptions;

        public bool MarkingPersonal => _markingPersonal;

        /// <summary>Итог маркировки на развертке; null — развертка не сформирована.</summary>
        public MarkingResult Marking { get; private set; }

        /// <summary>Для панели свойств: параметры и где получилось.</summary>
        public string MarkingSummary =>
            (_markingPersonal ? "своя: " : string.Empty) + _markingOptions.Summary;

        /// <summary>Развертка с другими параметрами маркировки — для примера в окне настроек.</summary>
        public (FlatDrawing Drawing, MarkingResult Marking) RenderWithMarking(MarkingOptions options)
        {
            var (drawing, _, marking) = Preview.Render(_resolve, _bendLayers, options, PartNumber);
            return (drawing, marking);
        }

        /// <summary>Маркировка изменилась — перерисовываем развертку (без обращения к Inventor).</summary>
        public void ApplyMarking(MarkingOptions options, bool personal)
        {
            _markingOptions = options ?? new MarkingOptions();
            _markingPersonal = personal;
            OnPropertyChanged(nameof(MarkingSummary));
            Render();
        }

        public int PersonalLineCount => _personalLines;

        public void ApplyPreview(FlatPreview preview)
        {
            Preview = preview;
            FlatDxfPath = preview.DxfPath;
            FlatPreviewError = preview.Error;
            FlatPreviewState = preview.State;
            FlatCacheChecked = true;
            Render();
        }

        /// <summary>Параметры линий гиба изменились — перерисовываем развертку (без обращения к Inventor).</summary>
        public void ApplyBends(Func<BendLineInfo, BendLineOptions> resolve, BendLineOptions partOptions, bool personal,
            int personalLines, IReadOnlyCollection<string> bendLayers)
        {
            _resolve = resolve;
            _bends = partOptions;
            _bendsPersonal = personal;
            _personalLines = personalLines;
            _bendLayers = bendLayers;
            OnPropertyChanged(nameof(BendSummary));
            OnPropertyChanged(nameof(BendsPersonal));
            Render();
        }

        private void Render()
        {
            try
            {
                var (drawing, lines, marking) = Preview.Render(_resolve, _bendLayers, _markingOptions, PartNumber);
                Flat = drawing;
                BendLines = lines;
                Marking = marking;
            }
            catch (Exception ex)
            {
                Flat = null;
                BendLines = Array.Empty<ProcessedBendLine>();
                Marking = null;
                FlatPreviewError = "не удалось показать развертку: " + ex.Message;
                FlatPreviewState = FlatPreviewState.Failed;
            }
            UpdateMetrics();
        }

        #region Проверка перед выгрузкой

        private IReadOnlyList<PreflightIssue> _issues;

        /// <summary>Замечания проверки; null — ещё не проверялась.</summary>
        public IReadOnlyList<PreflightIssue> Issues => _issues;

        /// <summary>Худшее замечание (без «сведений»); null — замечаний нет или не проверялась.</summary>
        public IssueSeverity? WorstIssue =>
            _issues == null ? null : _issues.Where(i => i.Severity != IssueSeverity.Info).Select(i => (IssueSeverity?)i.Severity).Max();

        public bool CheckOk => _issues != null && WorstIssue == null;
        public bool CheckWarning => WorstIssue == IssueSeverity.Warning;
        public bool CheckError => WorstIssue == IssueSeverity.Error;

        /// <summary>Текст ячейки «Проверка».</summary>
        public string CheckText
        {
            get
            {
                if (_issues == null)
                    return string.Empty;
                int count = _issues.Count(i => i.Severity != IssueSeverity.Info);
                if (count == 0)
                    return "Нет замечаний";
                string first = _issues.Where(i => i.Severity != IssueSeverity.Info).OrderByDescending(i => i.Severity).First().Message;
                int cut = first.IndexOf(" — ", StringComparison.Ordinal);
                return (cut > 0 ? first.Substring(0, cut) : first) + (count > 1 ? $" (+{count - 1})" : string.Empty);
            }
        }

        public string CheckToolTip => _issues == null
            ? null
            : _issues.Count == 0 ? "Замечаний нет" : string.Join("\n", _issues.Select(i => "• " + i));

        /// <summary>Для сортировки: ошибки, затем предупреждения, затем без замечаний.</summary>
        public int CheckRank => _issues == null ? 0 : WorstIssue == IssueSeverity.Error ? 3 : WorstIssue == IssueSeverity.Warning ? 2 : 1;

        public void ApplyIssues(IReadOnlyList<PreflightIssue> issues)
        {
            _issues = issues;
            foreach (string name in new[] { nameof(Issues), nameof(WorstIssue), nameof(CheckOk), nameof(CheckWarning), nameof(CheckError),
                         nameof(CheckText), nameof(CheckToolTip), nameof(CheckRank) })
                OnPropertyChanged(name);
        }

        #endregion

        #region Материал из библиотеки и показатели развертки

        private ResolvedMaterial _libraryMaterial;
        private FlatMetrics _metrics;

        public ResolvedMaterial LibraryMaterial => _libraryMaterial;

        /// <summary>Площадь, длина реза, врезки — по сформированной развертке (null — развертки нет).</summary>
        public FlatMetrics Metrics => _metrics;

        public string LibraryMaterialText => _libraryMaterial.Material?.Name ?? "не задан";

        public bool MaterialManual => _libraryMaterial.Source == MaterialSource.Manual;
        public bool MaterialMissing => _libraryMaterial.Source == MaterialSource.None;
        public bool MaterialMismatch => _libraryMaterial.ThicknessMismatch;

        /// <summary>Подсказка к материалу: откуда он и что не так.</summary>
        public string LibraryMaterialHint
        {
            get
            {
                var m = _libraryMaterial.Material;
                switch (_libraryMaterial.Source)
                {
                    case MaterialSource.None:
                        return string.Format(CultureInfo.CurrentCulture,
                            "В библиотеке нет материала толщиной {0:0.##} мм — добавьте его или назначьте вручную", Thickness);
                    case MaterialSource.Manual:
                        return string.Format(CultureInfo.CurrentCulture, "Назначен вручную: {0}, {1:0.##} мм, {2:0.#} кг/м³", m.Name, m.Thickness, m.Density) +
                               (MaterialMismatch
                                   ? string.Format(CultureInfo.CurrentCulture, "\nТолщина материала не совпадает с толщиной детали ({0:0.##} мм); масса считается по толщине детали", Thickness)
                                   : string.Empty);
                    default:
                        return string.Format(CultureInfo.CurrentCulture, "По толщине детали: {0}, {1:0.#} кг/м³", m.Name, m.Density);
                }
            }
        }

        private bool HasMetrics => _metrics != null && _metrics.Pierces > 0;

        /// <summary>Площадь детали без отверстий, м².</summary>
        public double? AreaM2 => HasMetrics ? _metrics.Area * 1e-6 : (double?)null;

        /// <summary>Масса одной детали, кг: площадь × толщина детали × плотность материала.</summary>
        public double? Mass => HasMetrics && _libraryMaterial.Material != null
            ? _metrics.Mass(Thickness, _libraryMaterial.Material.Density)
            : (double?)null;

        /// <summary>Масса с учётом количества на изделия, кг.</summary>
        public double? TotalMass => Mass * Total;

        /// <summary>Длина реза одной детали, м.</summary>
        public double? CutLengthM => HasMetrics ? _metrics.CutLength / 1000 : (double?)null;

        /// <summary>Число врезок на одну деталь.</summary>
        public int? Pierces => HasMetrics ? _metrics.Pierces : (int?)null;

        public void ApplyMaterial(ResolvedMaterial material)
        {
            _libraryMaterial = material;
            OnPropertyChanged(nameof(LibraryMaterial));
            OnPropertyChanged(nameof(LibraryMaterialText));
            OnPropertyChanged(nameof(LibraryMaterialHint));
            OnPropertyChanged(nameof(MaterialManual));
            OnPropertyChanged(nameof(MaterialMissing));
            OnPropertyChanged(nameof(MaterialMismatch));
            OnPropertyChanged(nameof(Mass));
            OnPropertyChanged(nameof(TotalMass));
        }

        private void UpdateMetrics()
        {
            _metrics = Flat == null ? null : FlatMetrics.Compute(Flat);
            OnPropertyChanged(nameof(Metrics));
            OnPropertyChanged(nameof(AreaM2));
            OnPropertyChanged(nameof(Mass));
            OnPropertyChanged(nameof(TotalMass));
            OnPropertyChanged(nameof(CutLengthM));
            OnPropertyChanged(nameof(Pierces));
        }

        #endregion

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
