using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InvAuto.Core.Materials;

namespace InvAuto.Features.Materials
{
    /// <summary>
    /// Выбор материала для выделенных деталей: материал из библиотеки или «по толщине детали».
    /// По умолчанию показаны только материалы той же толщины, что у деталей.
    /// </summary>
    public partial class MaterialPickerWindow : Window
    {
        private readonly MaterialLibrary _library;
        private readonly double? _thickness;
        private readonly string _currentId;

        /// <param name="thicknesses">Толщины выделенных деталей.</param>
        /// <param name="currentId">Материал, назначенный сейчас всем выделенным деталям (null — разные или по толщине).</param>
        public MaterialPickerWindow(MaterialLibrary library, IReadOnlyCollection<double> thicknesses, int partCount, string currentId)
        {
            InitializeComponent();
            _library = library;
            _currentId = currentId;

            var distinct = thicknesses.Select(t => Math.Round(t, 2)).Distinct().OrderBy(t => t).ToList();
            _thickness = distinct.Count == 1 ? distinct[0] : (double?)null;

            ScopeTitle.Text = partCount == 1 ? "Материал детали" : $"Материал для деталей: {partCount}";
            ScopeText.Text = _thickness.HasValue
                ? string.Format(CultureInfo.CurrentCulture, "Толщина {0:0.##} мм. Масса считается по толщине детали и плотности материала.", _thickness.Value)
                : "Толщины деталей разные: " + string.Join(", ", distinct.Select(t => t.ToString("0.##", CultureInfo.CurrentCulture))) + " мм.";

            if (_thickness.HasValue)
                SameThicknessBox.Content = string.Format(CultureInfo.CurrentCulture, "Только материалы толщиной {0:0.##} мм", _thickness.Value);
            else
            {
                SameThicknessBox.IsChecked = false;
                SameThicknessBox.Visibility = Visibility.Collapsed;
            }
            Fill();
        }

        /// <summary>Выбранный материал; null — «по толщине детали».</summary>
        public string SelectedId { get; private set; }

        private void Fill()
        {
            bool sameOnly = SameThicknessBox.IsChecked == true && _thickness.HasValue;
            var choices = new List<Choice>
            {
                new Choice(null, "По толщине детали",
                    "Материал по умолчанию из библиотеки для толщины каждой детали; вручную назначенный материал снимается")
            };
            choices.AddRange(_library.Materials
                .Where(m => !sameOnly || MaterialLibrary.SameThickness(m.Thickness, _thickness.Value))
                .OrderBy(m => m.Thickness).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(m => new Choice(m, m.Name, string.Format(CultureInfo.CurrentCulture,
                    "{0:0.##} мм · {1:0.#} кг/м³{2}", m.Thickness, m.Density, m.IsDefault ? " · по умолчанию для толщины" : string.Empty))));

            Choices.ItemsSource = choices;
            Choices.SelectedItem = choices.FirstOrDefault(c => c.Material?.Id == _currentId && _currentId != null) ?? choices[0];
            if (choices.Count == 1)
                WarningText.Text = sameOnly ? "В библиотеке нет материалов этой толщины — снимите фильтр или добавьте материал" : "Библиотека пуста";
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
                Fill();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var material = (Choices.SelectedItem as Choice)?.Material;
            WarningText.Text = material != null && _thickness.HasValue && !MaterialLibrary.SameThickness(material.Thickness, _thickness.Value)
                ? "Толщина материала не совпадает с толщиной деталей"
                : string.Empty;
            OkButton.IsEnabled = Choices.SelectedItem != null;
        }

        private void OnChoiceDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Choices.SelectedItem != null)
                OnOk(sender, e);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!(Choices.SelectedItem is Choice choice))
                return;
            SelectedId = choice.Material?.Id;
            DialogResult = true;
        }

        private sealed class Choice
        {
            public Choice(Material material, string title, string details)
            {
                Material = material;
                Title = title;
                Details = details;
            }

            public Material Material { get; }
            public string Title { get; }
            public string Details { get; }
        }
    }
}
