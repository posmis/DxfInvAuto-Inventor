// Сборка иконок InvAuto из SVG-исходников (assets/icons/*.svg, сетка 32×32).
//
// Результат:
//   src/InvAuto.Inventor/Framework/Ui/Icons.xaml    — векторные DrawingImage для окон WPF (ключ Icon.ИмяВPascalCase)
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

const icons = files.map(f => {
  const svg = readFileSync(join(srcDir, f), 'utf8');
  const name = basename(f, '.svg');
  const body = [...svg.matchAll(/<(path|rect|circle|line|polyline|polygon)\b([^>]*)\/?>/g)]
    .map(m => drawing(m[1], attrs(m[2])))
    .filter(Boolean)
    .join('\n');
  return { name, svg, body };
});

const xaml = `<!--
  СГЕНЕРИРОВАНО tools/icons/build-icons.mjs из assets/icons/*.svg — не редактировать вручную.
  Использование: <Image Source="{StaticResource Icon.Refresh}" Width="32" Height="32"/>
-->
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
${icons.map(i => `
  <DrawingImage x:Key="Icon.${pascal(i.name)}">
    <DrawingImage.Drawing>
      <DrawingGroup ClipGeometry="M0,0 H32 V32 H0 Z">
        <GeometryDrawing Brush="Transparent" Geometry="M0,0 H32 V32 H0 Z"/>
${i.body}
      </DrawingGroup>
    </DrawingImage.Drawing>
  </DrawingImage>`).join('\n')}

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
  const sized = svg.replace(/width="32" height="32"/, `width="${size}" height="${size}"`);
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:transparent">${sized}</body></html>`);
  return page.screenshot({ omitBackground: true, clip: { x: 0, y: 0, width: size, height: size } });
}

mkdirSync(pngDir, { recursive: true });
for (const [name, id] of Object.entries(RIBBON)) {
  const icon = icons.find(i => i.name === name);
  if (!icon) throw new Error(`Нет assets/icons/${name}.svg`);
  for (const size of [16, 32])
    writeFileSync(join(pngDir, `${id}.${size}.png`), await render(icon.svg, size));
  console.log(`PNG: ${id}.16.png, ${id}.32.png`);
}

// Обзорный лист: каждая иконка в 16, 24, 32 и 64 px на светлом фоне.
const cells = icons.map(i => `
  <div class="cell">
    ${[16, 24, 32, 64].map(s => i.svg.replace(/width="32" height="32"/, `width="${s}" height="${s}"`)).join('')}
    <div class="name">${i.name}</div>
  </div>`).join('');
await page.setViewportSize({ width: 900, height: 100 });
await page.setContent(`<html><head><style>
  body { margin: 0; padding: 16px; background: #F3F4F6; font: 12px "Segoe UI", sans-serif; color: #1F2328; }
  .grid { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
  .cell { background: #fff; border: 1px solid #D6D9DD; padding: 10px; display: flex; align-items: flex-end; gap: 14px; }
  .name { margin-left: auto; color: #6A737D; }
</style></head><body><div class="grid">${cells}</div></body></html>`);
await page.screenshot({ path: join(srcDir, 'preview.png'), fullPage: true });
console.log('assets/icons/preview.png');

await browser.close();
