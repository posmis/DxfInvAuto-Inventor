// Сборка иконок InvAuto из SVG-исходников.
//
// Исходники: assets/icons/имя.svg — рисунок 32×32 (большие кнопки), assets/icons/имя.16.svg — отдельный
// рисунок 16×16 для мелких размеров (маленькие кнопки, дерево, PNG 16 px). Как в Inventor: мелкий размер
// рисуется отдельно, а не уменьшается из 32, иначе тонкие линии расплываются. Если одного из вариантов нет,
// используется другой.
//
// Результат:
//   src/InvAuto.Inventor/Framework/Ui/Icons.xaml    — векторные DrawingImage для окон WPF:
//                                                     Icon.ИмяВPascalCase (32) и Icon.ИмяВPascalCase.Small (16)
//   src/InvAuto.Inventor/Resources/Icons/*.png      — PNG 16 и 32 px для кнопок на ленте Inventor (см. RIBBON)
//   assets/icons/preview.png                         — обзорный лист для проверки на глаз
//
// Запуск из корня репозитория:
//   npm install playwright && npx playwright install chromium
//   node tools/icons/build-icons.mjs
//
// Поддерживаемое подмножество SVG (этого хватает для иконок и легко переводится в XAML):
//   <path d>, <rect x y width height rx>, <circle cx cy r>, <line>, <polyline>, <polygon>;
//   атрибуты fill, stroke, stroke-width, stroke-linecap, stroke-linejoin, stroke-dasharray.
//   transform не поддерживается — координаты пишутся сразу в сетке своего размера.

import { readFileSync, writeFileSync, readdirSync, mkdirSync } from 'node:fs';
import { join, basename, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';

const root = join(dirname(fileURLToPath(import.meta.url)), '..', '..');
const srcDir = join(root, 'assets', 'icons');
const xamlOut = join(root, 'src', 'InvAuto.Inventor', 'Framework', 'Ui', 'Icons.xaml');
const pngDir = join(root, 'src', 'InvAuto.Inventor', 'Resources', 'Icons');

// Иконки кнопок на ленте Inventor: имя SVG → Id функции (IconLoader ищет {Id}.16.png и {Id}.32.png).
const RIBBON = {
  'sheet-metal': 'SheetMetal',
  'data-folder': 'OpenDataFolder',
};

const files = readdirSync(srcDir).filter(f => f.endsWith('.svg')).sort();
const names = [...new Set(files.map(f => basename(f, '.svg').replace(/\.16$/, '')))].sort();

// ---------- SVG → XAML ----------

const pascal = name => name.split(/[-_]/).map(s => s[0].toUpperCase() + s.slice(1)).join('');
const attrs = tag => Object.fromEntries([...tag.matchAll(/([\w-]+)="([^"]*)"/g)].map(m => [m[1], m[2]]));
const num = v => Number(v ?? 0);

function geometry(el, a) {
  switch (el) {
    case 'path':
      return a.d;
    case 'rect': {
      const x = num(a.x), y = num(a.y), w = num(a.width), h = num(a.height), r = num(a.rx);
      if (!r) return `M${x},${y} H${x + w} V${y + h} H${x} Z`;
      return `M${x + r},${y} H${x + w - r} A${r},${r} 0 0 1 ${x + w},${y + r} V${y + h - r} ` +
             `A${r},${r} 0 0 1 ${x + w - r},${y + h} H${x + r} A${r},${r} 0 0 1 ${x},${y + h - r} ` +
             `V${y + r} A${r},${r} 0 0 1 ${x + r},${y} Z`;
    }
    case 'circle': {
      const cx = num(a.cx), cy = num(a.cy), r = num(a.r);
      return `M${cx - r},${cy} A${r},${r} 0 1 1 ${cx + r},${cy} A${r},${r} 0 1 1 ${cx - r},${cy} Z`;
    }
    case 'line':
      return `M${a.x1},${a.y1} L${a.x2},${a.y2}`;
    case 'polyline':
    case 'polygon': {
      const p = a.points.trim().split(/[\s,]+/);
      let d = `M${p[0]},${p[1]}`;
      for (let i = 2; i + 1 < p.length; i += 2) d += ` L${p[i]},${p[i + 1]}`;
      return el === 'polygon' ? d + ' Z' : d;
    }
  }
  return null;
}

const cap = v => ({ round: 'Round', square: 'Square' }[v] ?? 'Flat');
const join_ = v => ({ round: 'Round', bevel: 'Bevel' }[v] ?? 'Miter');

function drawing(el, a) {
  const g = geometry(el, a);
  if (!g) return '';
  const fill = a.fill && a.fill !== 'none' ? ` Brush="${a.fill}"` : '';
  let pen = '';
  if (a.stroke && a.stroke !== 'none') {
    const w = num(a['stroke-width'] ?? 1);
    const dash = a['stroke-dasharray']
      ? `<Pen.DashStyle><DashStyle Dashes="${a['stroke-dasharray'].trim().split(/[\s,]+/).map(d => +(num(d) / w).toFixed(4)).join(' ')}"/></Pen.DashStyle>`
      : '';
    const c = cap(a['stroke-linecap']);
    pen = `<GeometryDrawing.Pen><Pen Brush="${a.stroke}" Thickness="${w}" StartLineCap="${c}" EndLineCap="${c}" ` +
          `DashCap="${c}" LineJoin="${join_(a['stroke-linejoin'])}">${dash}</Pen></GeometryDrawing.Pen>`;
  }
  return `        <GeometryDrawing${fill} Geometry="${g}">${pen}</GeometryDrawing>`;
}

function load(file) {
  const svg = readFileSync(join(srcDir, file), 'utf8');
  if (/\btransform=/.test(svg)) throw new Error(`${file}: transform не поддерживается`);
  const size = Number(svg.match(/viewBox="0 0 (\d+) \d+"/)[1]);
  const body = [...svg.matchAll(/<(path|rect|circle|line|polyline|polygon)\b([^>]*)\/?>/g)]
    .map(m => drawing(m[1], attrs(m[2])))
    .filter(Boolean)
    .join('\n');
  return { svg, size, body };
}

const icons = names.map(name => {
  const large = files.includes(`${name}.svg`) ? load(`${name}.svg`) : null;
  const small = files.includes(`${name}.16.svg`) ? load(`${name}.16.svg`) : null;
  return { name, large: large ?? small, small: small ?? large };
});

const drawingImage = (key, v) => `
  <DrawingImage x:Key="${key}">
    <DrawingImage.Drawing>
      <DrawingGroup ClipGeometry="M0,0 H${v.size} V${v.size} H0 Z">
        <GeometryDrawing Brush="Transparent" Geometry="M0,0 H${v.size} V${v.size} H0 Z"/>
${v.body}
      </DrawingGroup>
    </DrawingImage.Drawing>
  </DrawingImage>`;

const xaml = `<!--
  СГЕНЕРИРОВАНО tools/icons/build-icons.mjs из assets/icons/*.svg — не редактировать вручную.
  Большие кнопки: <Image Source="{StaticResource Icon.Refresh}" Width="32" Height="32"/>
  Мелкие (16 px): <Image Source="{StaticResource Icon.Refresh.Small}" Width="16" Height="16"/>
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
${icons.map(i => drawingImage(`Icon.${pascal(i.name)}`, i.large) + drawingImage(`Icon.${pascal(i.name)}.Small`, i.small)).join('\n')}

</ResourceDictionary>
`;
writeFileSync(xamlOut, xaml, 'utf8');
console.log(`Icons.xaml: ${icons.length} иконок`);

// ---------- PNG ----------

// require, а не import: так playwright находится и в глобальной установке (NODE_PATH).
const { chromium } = createRequire(import.meta.url)('playwright');
const browser = await chromium.launch();
const page = await browser.newPage();

async function render(svg, size) {
  const sized = svg.replace(/width="\d+" height="\d+"/, `width="${size}" height="${size}"`);
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${sized}</body></html>`);
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
}

mkdirSync(pngDir, { recursive: true });
for (const [name, id] of Object.entries(RIBBON)) {
  const icon = icons.find(i => i.name === name);
  if (!icon) throw new Error(`Нет assets/icons/${name}.svg`);
  writeFileSync(join(pngDir, `${id}.16.png`), await render(icon.small.svg, 16));
  writeFileSync(join(pngDir, `${id}.32.png`), await render(icon.large.svg, 32));
  console.log(`PNG: ${id}.16.png, ${id}.32.png`);
}

// Обзорный лист: 16 px и 32 px в натуральную величину и они же увеличенные ×4 (видно каждый пиксель).
const sizeSvg = (svg, px) => svg.replace(/width="\d+" height="\d+"/, `width="${px}" height="${px}"`);
async function png64(svg, size) {
  return 'data:image/png;base64,' + (await render(svg, size)).toString('base64');
}
const cells = [];
for (const i of icons) {
  const s16 = await png64(i.small.svg, 16), s32 = await png64(i.large.svg, 32);
  cells.push(`
  <div class="cell">
    <img src="${s16}" width="16" height="16"><img src="${s32}" width="32" height="32">
    <img class="zoom" src="${s16}" width="64" height="64"><img class="zoom" src="${s32}" width="128" height="128">
    <div class="name">${i.name}</div>
  </div>`);
}
await page.setViewportSize({ width: 1000, height: 100 });
await page.setContent(`<html><head><style>
  body { margin: 0; padding: 16px; background: #F3F4F6; font: 12px "Segoe UI", sans-serif; color: #1F2328; }
  .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
  .cell { background: #fff; border: 1px solid #D6D9DD; padding: 10px; display: flex; align-items: flex-end; gap: 12px; }
  .zoom { image-rendering: pixelated; }
  .name { margin-left: auto; color: #6A737D; }
</style></head><body><div class="grid">${cells.join('')}</div></body></html>`);
await page.screenshot({ path: join(srcDir, 'preview.png'), fullPage: true });
console.log('assets/icons/preview.png');

await browser.close();
