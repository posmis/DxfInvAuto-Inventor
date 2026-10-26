# InvAuto — инструменты автоматизации для Autodesk Inventor

Плагин для Inventor, построенный как расширяемая платформа: каждая функция — отдельный класс,
который автоматически получает свою кнопку на вкладке **InvAuto**.

## Что уже умеет

**Развертки в DXF** (вкладка InvAuto → Листовой металл), работает в сборке и в листовой детали:

- окно со списком всех листовых деталей сборки, включая вложенные подсборки:
  обозначение, наименование, материал, толщина, количество;
- выбор деталей галочками, поиск по списку, кнопки «Выбрать все» / «Снять все»;
- количество можно поправить прямо в таблице; «Кол-во изделий» умножает всё на N;
- шаблон имени файла: `{Name}-t{Thickness:0.0}мм-{Qty}шт` (по умолчанию как в старом плагине),
  доступны `{PartNumber}`, `{Description}`, `{Material}`, `{Assembly}` и любые пользовательские iProperties;
- раскладка по подпапкам, например `{Material}\{Thickness:0.0}мм`;
- версия DXF, имена слоёв контура и отверстий, линии гиба и касательные — без внешнего .ini;
- подавленные, справочные (Reference) и виртуальные компоненты не учитываются;
- для деталей без развертки она создаётся автоматически (отключается);
- статус по каждой детали в таблице (OK / пропущено / ошибка с причиной) и CSV-отчёт для Excel;
- настройки запоминаются между запусками.

**Логи и настройки** (Сервис) — открывает `%APPDATA%\InvAuto`, где лежат настройки и логи.

## Структура

```
src/
  InvAuto.Core/          логика без Inventor: шаблоны имён, настройки, отчёты (покрыта тестами)
  InvAuto.Inventor/      сам плагин
    AddInServer.cs       точка входа: находит функции и строит ленту
    Framework/           IFeature, FeatureContext, автопоиск функций, лента, иконки
    InventorApi/         работа с Inventor: iProperties, сбор листовых деталей, экспорт
    Features/            функции — по папке на каждую
    InvAuto.addin        регистрация плагина в Inventor
tests/InvAuto.Core.Tests/
```

## Сборка и установка

Нужны Windows, Visual Studio 2022 (или .NET SDK 8) и установленный Inventor.

1. Откройте `InvAuto.sln`.
2. Если у вас не Inventor 2024, укажите версию: в `InvAuto.Inventor.csproj` поменяйте `InventorVersion`
   или соберите командой `dotnet build -p:InventorVersion=2023`.
   Для Inventor 2025 и новее проект сам переключится на .NET 8, для более старых — на .NET Framework 4.8.
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

## Тесты

```
dotnet test tests/InvAuto.Core.Tests
```
