using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Inventor;

namespace InvAuto.Framework
{
    /// <summary>
    /// Создаёт панель InvAuto на вкладке «Среды» в нужных лентах Inventor и кнопки для каждой функции.
    /// Ошибки внутри функций перехватываются здесь, чтобы ни одна из них не уронила Inventor.
    /// </summary>
    public sealed class RibbonBuilder : IDisposable
    {
        // Кнопки InvAuto — отдельная панель на стандартной вкладке «Среды» (Environments).
        private const string EnvironmentsTab = "id_TabEnvironments";
        private const string PanelName = "InvAuto";
        private const string PanelInternalName = "id_PanelInvAuto";

        private static readonly Dictionary<RibbonTargets, string> RibbonNames = new Dictionary<RibbonTargets, string>
        {
            { RibbonTargets.ZeroDoc, "ZeroDoc" },
            { RibbonTargets.Part, "Part" },
            { RibbonTargets.Assembly, "Assembly" },
            { RibbonTargets.Drawing, "Drawing" }
        };

        private readonly FeatureContext _context;
        private readonly string _clientId;

        // Ссылки держим, иначе COM-обёртки соберёт GC и обработчики нажатий перестанут срабатывать.
        private readonly List<(ButtonDefinition Button, ButtonDefinitionSink_OnExecuteEventHandler Handler)> _buttons =
            new List<(ButtonDefinition, ButtonDefinitionSink_OnExecuteEventHandler)>();

        public RibbonBuilder(FeatureContext context, string clientId)
        {
            _context = context;
            _clientId = clientId;
        }

        public void Build(IEnumerable<IFeature> features)
        {
            foreach (var feature in features)
            {
                try
                {
                    var button = CreateButton(feature);
                    foreach (var target in RibbonNames.Keys.Where(t => feature.Targets.HasFlag(t)))
                        AddToRibbon(button, feature, RibbonNames[target]);
                }
                catch (Exception ex)
                {
                    _context.Log.Error($"Не удалось создать кнопку для '{feature.Id}'", ex);
                }
            }
        }

        private ButtonDefinition CreateButton(IFeature feature)
        {
            string internalName = InternalName(feature);
            ControlDefinitions definitions = _context.App.CommandManager.ControlDefinitions;

            // При повторной загрузке плагина без перезапуска Inventor определение уже существует.
            ButtonDefinition button = TryGet(() => (ButtonDefinition)definitions[internalName]);
            if (button == null)
            {
                object smallIcon = IconLoader.Load(feature.Id, 16) ?? Type.Missing;
                object largeIcon = IconLoader.Load(feature.Id, 32) ?? Type.Missing;

                button = definitions.AddButtonDefinition(
                    feature.DisplayName,
                    internalName,
                    CommandTypesEnum.kNonShapeEditCmdType,
                    _clientId,
                    feature.Description,
                    feature.Description,
                    smallIcon,
                    largeIcon,
                    ButtonDisplayEnum.kAlwaysDisplayText);
            }

            ButtonDefinitionSink_OnExecuteEventHandler handler = _ => Run(feature);
            button.OnExecute += handler;
            _buttons.Add((button, handler));
            return button;
        }

        private void Run(IFeature feature)
        {
            try
            {
                _context.Log.Info($"Запуск '{feature.Id}'");
                feature.Execute(_context);
            }
            catch (Exception ex)
            {
                _context.Log.Error($"Ошибка в '{feature.Id}'", ex);
                _context.ShowError($"{feature.DisplayName}: {ex.Message}\n\nПодробности в логе: %APPDATA%\\InvAuto\\logs");
            }
        }

        private void AddToRibbon(ButtonDefinition button, IFeature feature, string ribbonName)
        {
            Ribbon ribbon = _context.App.UserInterfaceManager.Ribbons[ribbonName];

            RibbonTab tab = TryGet(() => ribbon.RibbonTabs[EnvironmentsTab]);
            if (tab == null)
            {
                _context.Log.Warn($"В ленте {ribbonName} нет вкладки «Среды» — кнопка {feature.Id} не добавлена");
                return;
            }

            RibbonPanel panel = TryGet(() => tab.RibbonPanels[PanelInternalName])
                ?? tab.RibbonPanels.Add(PanelName, PanelInternalName, _clientId);

            if (TryGet(() => panel.CommandControls[button.InternalName]) == null)
                panel.CommandControls.AddButton(button, feature.LargeButton);
        }

        public void Dispose()
        {
            foreach (var (button, handler) in _buttons)
            {
                try
                {
                    button.OnExecute -= handler;
                    Marshal.ReleaseComObject(button);
                }
                catch
                {
                    // Inventor уже закрывается — ничего страшного.
                }
            }
            _buttons.Clear();
        }

        private static string InternalName(IFeature feature) => "InvAuto." + feature.Id;

        /// <summary>Индексаторы коллекций Inventor бросают исключение, если элемента нет.</summary>
        private static T TryGet<T>(Func<T> getter) where T : class
        {
            try
            {
                return getter();
            }
            catch
            {
                return null;
            }
        }
    }
}
