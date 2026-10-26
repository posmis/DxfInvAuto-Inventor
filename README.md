# InvAuto — инструменты автоматизации для Autodesk Inventor

Плагин для Inventor, построенный как расширяемая платформа: каждая функция — отдельный класс,
который автоматически получает свою кнопку на вкладке **InvAuto**.

## Что уже умеет

**Листовые детали** (вкладка InvAuto → Листовой металл), работает в сборке и в листовой детали.

Окно в стиле Inventor: сверху лента команд, слева дерево состава, справа список деталей.

- **Дерево состава изделия**: все компоненты с подсборками на любую глубину, колонки
  Обозначение / Наименование / Кол. (в родителе) / Всего (в изделии).
  Подавленные, справочные (Reference) и виртуальные компоненты не учитываются.
- **Листовые детали выбранного узла**: выберите в дереве сборку или подсборку — справа появятся все её
  листовые детали, включая детали вложенных подсборок. Количество считается на всё изделие.
- **Превью**: миниатюра детали и развертка (контур — тёмным, линии гиба — синим пунктиром).
  Развертка рисуется прямо из геометрии Inventor — пока не нажата кнопка выгрузки, никакие DXF не создаются.
- **Колонки настраиваются** (Вид → Колонки). По умолчанию: имя файла, толщина, габариты, количество,
  развертка, миниатюра. Можно добавить обозначение, наименование, материал, наличие развертки, итого на изделия,
  статус выгрузки, путь и любые пользовательские iProperties. Порядок меняется перетаскиванием заголовков.
- **Панель свойств** выбранной детали: крупная развертка, миниатюра, все свойства.
- **Выгрузка DXF**: «Выгрузить отмеченные» — отмеченные детали выбранного узла;
  «Выгрузить всё» — все листовые детали изделия. «Изделий: N» умножает количество.
  Статус по каждой детали в списке, CSV-отчёт для Excel.
- **Способ записи DXF**: по умолчанию — транслятор DXF Inventor с ini-файлом
  `Config/dxf_export_config.ini` (DXF 2004, только наружный и внутренние контуры и линии гиба,
  каждый в своём слое `IV_OUTER_PROFILE`, `IV_INTERIOR_PROFILES`, `IV_BEND`, `IV_BEND_DOWN`).
  Свой ini можно указать в «Параметрах выгрузки». Запасной способ — DataIO со строкой параметров.
- Шаблон имени файла: `{Name}-t{Thickness:0.0}мм-{Qty}шт`, доступны `{PartNumber}`, `{Description}`,
  `{Material}`, `{Length}`, `{Width}`, `{Assembly}` и любые iProperties; раскладка по подпапкам.

**Логи и настройки** (Сервис) — открывает `%APPDATA%\InvAuto`, где лежат настройки и логи.

## Структура

```
src/
  InvAuto.Core/          логика без Inventor: дерево состава и подсчёт количества, чтение DXF для превью,
                         шаблоны имён, настройки, отчёты (покрыта тестами)
  InvAuto.Inventor/      сам плагин
    AddInServer.cs       точка входа: находит функции и строит ленту
    Framework/           IFeature, FeatureContext, автопоиск функций, лента Inventor, иконки
    Framework/Ui/        общая тема окон (Theme.xaml), лента команд, контрол превью развертки
    InventorApi/         работа с Inventor: состав сборки, iProperties, превью, запись DXF
    Features/            функции — по папке на каждую
    Config/              dxf_export_config.ini — настройки транслятора DXF
    InvAuto.addin        регистрация плагина в Inventor
tests/InvAuto.Core.Tests/
```

## Сборка и установка

Нужны Windows, Visual Studio 2022 (17.8+, рабочая нагрузка «Разработка классических приложений .NET»)
и установленный Inventor.

1. Откройте `InvAuto.sln`.
2. Проект настроен на Inventor 2025 (.NET 8). Для другой версии поменяйте `InventorVersion`
   в `InvAuto.Inventor.csproj` или соберите командой `dotnet build -p:InventorVersion=2026`.
   Для версий до 2025 проект сам переключится на .NET Framework 4.8.
3. Соберите в конфигурации **Debug**: плагин сам скопируется в
   `%APPDATA%\Autodesk\ApplicationPlugins\InvAuto`. Закройте Inventor перед сборкой, иначе DLL будет занята.
4. Запустите Inventor — появится вкладка **InvAuto**.

Для отладки: Свойства проекта → Отладка → запуск внешней программы `Inventor.exe`.

Старый плагин можно оставить: у InvAuto свой GUID, они не конфликтуют.

## Как добавить новую функцию

1. Создайте папку `src/InvAuto.Inventor/Features/МояФункция/` и класс:

```csharp
public sealed class MyFeature : FeatureBase
{
    public override string Id => "MyFeature";              // уникальный, не менять после выпуска
    public override string DisplayName => "Моя функция";   // текст кнопки
    public override string Panel => "Чертежи";             // панель на вкладке InvAuto
    // Окна делайте на WPF с общей темой: <ResourceDictionary Source="/InvAuto;component/Framework/Ui/Theme.xaml"/>
    // — там готовы стили ленты (RibbonGroup, RibbonButton), таблиц и панелей.
    public override RibbonTargets Targets => RibbonTargets.Assembly | RibbonTargets.Drawing;

    public override void Execute(FeatureContext context)
    {
        var doc = context.App.ActiveDocument;
        var settings = context.Settings.Load<MySettings>(Id);  // свои настройки, сохраняются в XML
        context.Log.Info("Работаю с " + doc.DisplayName);
        context.ShowInfo("Готово");
    }
}
```

2. Всё. Кнопка появится автоматически, исключения из `Execute` перехватываются, пишутся в лог
   и показываются пользователю.
3. Иконки (необязательно): положите `Resources/Icons/MyFeature.16.png` и `MyFeature.32.png`.
4. Логику, которую можно проверить без Inventor (расчёты, имена, форматы), выносите в `InvAuto.Core`
   и покрывайте тестами: `dotnet test`.

Функции можно поставлять и отдельными DLL: сборка со своими классами `FeatureBase`,
положенная в папку `Plugins` рядом с `InvAuto.dll`, подхватится при запуске Inventor.

## Иконки

Исходники — SVG в `assets/icons/` (сетка 32×32, линии толщиной 2 по нечётным координатам, чтобы
иконка оставалась резкой и в 16 px). Из них скрипт делает:

- `src/InvAuto.Inventor/Framework/Ui/Icons.xaml` — векторные иконки для окон (`{StaticResource Icon.Refresh}`);
- `src/InvAuto.Inventor/Resources/Icons/*.png` — 16 и 32 px для кнопок на ленте Inventor;
- `assets/icons/preview.png` — обзорный лист.

Новая иконка: положите `имя.svg` в `assets/icons/` и выполните из корня репозитория

```
npm install playwright && npx playwright install chromium
node tools/icons/build-icons.mjs
```

Для кнопки на ленте Inventor добавьте соответствие «имя SVG → Id функции» в `RIBBON` в начале скрипта.
Сгенерированные файлы хранятся в репозитории, так что для сборки плагина Node.js не нужен.

## Тесты

```
dotnet test tests/InvAuto.Core.Tests
```
