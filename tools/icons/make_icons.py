# Генератор SVG-исходников иконок InvAuto в стиле Inventor 2025 (рисунки на 32 и на 16 px).
# Геометрия (изометрия, зубцы шестерни) считается здесь, палитра — в константах ниже.
# Запуск из корня репозитория:  python tools/icons/make_icons.py assets/icons
# затем:                        node tools/icons/build-icons.mjs
# Иконку можно и нарисовать вручную: положить имя.svg (32×32) и/или имя.16.svg (16×16) в assets/icons.
import math, os, re, sys
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join('assets', 'icons')

INK = '#2E3943'      # основной контур
INK2 = '#5C6977'     # второстепенные линии
# грани деталей (свет сверху-слева)
P_TOP, P_LEFT, P_RIGHT = '#F2F5F8', '#D3DCE4', '#AEBCC9'
# листовой металл
S_TOP, S_LEFT, S_RIGHT, S_INK = '#E1EDF8', '#B9D0E5', '#8FB0CE', '#2B587F'
# сборка: вторая деталь — тёплая
A_TOP, A_LEFT, A_RIGHT, A_INK = '#F7E3B0', '#E6C578', '#C99F45', '#6B4E14'
BLUE, BLUE_L = '#1F6FBF', '#D6E7F7'
GREEN = '#2E8540'
F_BACK, F_FRONT, F_INK = '#E2BE6C', '#F0D48E', '#9C7428'
DOC, DOC_FOLD = '#FFFFFF', '#E3E8ED'

def svg(size, body):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" width="{size}" height="{size}">\n'
            + '\n'.join('  ' + b for b in body) + '\n</svg>\n')

def f(v):
    return ('%.2f' % v).rstrip('0').rstrip('.')

def poly(points, fill, stroke=INK, w=1, close=True, extra=''):
    d = 'M' + ' L'.join(f'{f(x)} {f(y)}' for x, y in points) + (' Z' if close else '')
    s = f' stroke="{stroke}" stroke-width="{f(w)}" stroke-linejoin="miter"' if stroke else ''
    return f'<path d="{d}" fill="{fill}"{s}{extra}/>'

def line(points, stroke=INK, w=1, extra=''):
    return poly(points, 'none', stroke, w, close=False, extra=extra)

ERRORS = []

# Минимальное пустое поле от края до рисунка (с учётом толщины линий), px.
MARGIN = {16: 0.9, 32: 1.4}

def _stroke_half(item):
    if 'stroke="' not in item or 'stroke="none"' in item:
        return 0.0
    m = re.search(r'stroke-width="([^"]+)"', item)
    return float(m.group(1)) / 2 if m else 0.5

def bbox(body):
    """Точные габариты рисунка с учётом толщины линий: (x0, y0, x1, y1)."""
    xs, ys = [], []
    for item in body:
        hw = _stroke_half(item)
        for d in re.findall(r' d="([^"]+)"', item):
            if 'A' in d:
                raise SystemExit('дуги (A) не поддерживаются генератором — используйте ломаную (arc_points)')
            for x, y in re.findall(r'[ML](-?[\d.]+) (-?[\d.]+)', d):
                xs += [float(x) - hw, float(x) + hw]
                ys += [float(y) - hw, float(y) + hw]
        for cx, cy, r in re.findall(r'cx="([^"]+)" cy="([^"]+)" r="([^"]+)"', item):
            cx, cy, r = float(cx), float(cy), float(r) + hw
            xs += [cx - r, cx + r]
            ys += [cy - r, cy + r]
    return min(xs), min(ys), max(xs), max(ys)

def shift(body, dx, dy):
    def move_path(m):
        return f'{m.group(1)}{f(float(m.group(2)) + dx)} {f(float(m.group(3)) + dy)}'
    def move_circle(m):
        return f'cx="{f(float(m.group(1)) + dx)}" cy="{f(float(m.group(2)) + dy)}"'
    out = []
    for item in body:
        item = re.sub(r' d="([^"]+)"', lambda m: ' d="' + re.sub(r'([ML])(-?[\d.]+) (-?[\d.]+)', move_path, m.group(1)) + '"', item)
        item = re.sub(r'cx="([^"]+)" cy="([^"]+)"', move_circle, item)
        out.append(item)
    return out

def center(name, size, body, snap):
    """
    Ставит рисунок по центру поля. snap=True — края рисунка (с учётом толщины линий) попадают точно на
    границу пикселя, поэтому прямые линии толщиной 1 px резкие; остаток от точного центра не больше 0,5 px
    (только если ширина рисунка нечётная). snap=False — точный центр (изометрия, круги).
    """
    x0, y0, x1, y1 = bbox(body)
    dx, dy = size / 2 - (x0 + x1) / 2, size / 2 - (y0 + y1) / 2
    if snap:
        dx = math.floor(x0 + dx + 0.5) - x0
        dy = math.floor(y0 + dy + 0.5) - y0
    body = shift(body, dx, dy)
    x0, y0, x1, y1 = bbox(body)
    m = MARGIN[size]
    if min(x0, y0, size - x1, size - y1) < m - 1e-6:
        ERRORS.append(f'{name} ({size} px): рисунок {x1 - x0:.2f}×{y1 - y0:.2f} не помещается с полем {m} px — уменьшите его')
    return body

def write(name, size, body, snap=True):
    body = center(name, size, body, snap)
    suffix = '.16' if size == 16 else ''
    with open(os.path.join(OUT, f'{name}{suffix}.svg'), 'w', encoding='utf-8') as fh:
        fh.write(svg(size, body))

# ---------- изометрический куб ----------
def cube(cx, top, s, h, faces, ink, w=1):
    dx, dy = s * math.cos(math.radians(30)), s * 0.5
    t = [(cx, top), (cx + dx, top + dy), (cx, top + 2 * dy), (cx - dx, top + dy)]
    left = [t[3], t[2], (t[2][0], t[2][1] + h), (t[3][0], t[3][1] + h)]
    right = [t[2], t[1], (t[1][0], t[1][1] + h), (t[2][0], t[2][1] + h)]
    return [poly(left, faces[1], ink, w), poly(right, faces[2], ink, w), poly(t, faces[0], ink, w)]

# ---------- деталь ----------
write('part', 32, cube(16, 3.5, 13, 13, (P_TOP, P_LEFT, P_RIGHT), INK), snap=False)
write('part', 16, cube(8, 1.5, 6.6, 6.2, (P_TOP, P_LEFT, P_RIGHT), INK), snap=False)

# ---------- сборка: две детали ----------
write('assembly', 32,
      cube(20, 2.5, 9, 9, (P_TOP, P_LEFT, P_RIGHT), INK) +
      cube(11.5, 11.5, 9, 9, (A_TOP, A_LEFT, A_RIGHT), A_INK), snap=False)
write('assembly', 16,
      cube(10.5, 1, 4.4, 4.4, (P_TOP, P_LEFT, P_RIGHT), INK) +
      cube(5.6, 5.2, 4.4, 4.4, (A_TOP, A_LEFT, A_RIGHT), A_INK), snap=False)

# ---------- листовая деталь: уголок с толщиной ----------
def bracket(size, margin, w):
    """Гнутый уголок в изометрии: полка и стенка с видимой толщиной. Вписывается в поле по центру."""
    c, s = math.cos(math.radians(30)), 0.5
    def iso(x, y, z):  # x вправо-вниз, y влево-вниз, z вверх
        return ((x - y) * c, (x + y) * s - z)
    L, D, H, T = 12, 7, 8, 1.1   # длина, глубина полки, высота стенки, толщина
    faces = [
        # горизонтальная полка (верх и торцы)
        ([iso(0, 0, T), iso(L, 0, T), iso(L, D, T), iso(0, D, T)], S_TOP),
        ([iso(0, D, T), iso(L, D, T), iso(L, D, 0), iso(0, D, 0)], S_LEFT),
        ([iso(L, 0, T), iso(L, D, T), iso(L, D, 0), iso(L, 0, 0)], S_RIGHT),
        # вертикальная стенка по заднему краю (y = 0)
        ([iso(0, 0, T), iso(L, 0, T), iso(L, 0, H), iso(0, 0, H)], S_LEFT),
        ([iso(0, -T, H), iso(L, -T, H), iso(L, 0, H), iso(0, 0, H)], S_TOP),
        ([iso(L, -T, H), iso(L, 0, H), iso(L, 0, T), iso(L, -T, 0), iso(L, -T, H)], S_RIGHT),
    ]
    pts = [p for face, _ in faces for p in face]
    x0, x1 = min(p[0] for p in pts), max(p[0] for p in pts)
    y0, y1 = min(p[1] for p in pts), max(p[1] for p in pts)
    k = (size - 2 * margin) / max(x1 - x0, y1 - y0)
    ox = (size - (x1 - x0) * k) / 2 - x0 * k
    oy = (size - (y1 - y0) * k) / 2 - y0 * k
    return [poly([(ox + x * k, oy + y * k) for x, y in face], fill, S_INK, w) for face, fill in faces]

write('sheet-metal-part', 32, bracket(32, 2.6, 1), snap=False)
write('sheet-metal-part', 16, bracket(16, 1.9, 0.8), snap=False)
write('sheet-metal', 32, bracket(32, 2.6, 1), snap=False)
write('sheet-metal', 16, bracket(16, 1.9, 0.8), snap=False)

# ---------- развертка (сформировать развертки) ----------
def net(o, k, w, dash):
    a, b, c, d = o + 6 * k, o + 12 * k, o + 20 * k, o + 26 * k
    outline = [(b, a - 3 * k), (c, a - 3 * k), (c, b), (d + 3 * k, b), (d + 3 * k, c), (c, c), (c, d + 3 * k),
               (b, d + 3 * k), (b, c), (a - 3 * k, c), (a - 3 * k, b), (b, b)]
    return [poly(outline, BLUE_L, S_INK, w),
            line([(b, b), (c, b)], BLUE, w, f' stroke-dasharray="{dash}"'),
            line([(c, b), (c, c)], BLUE, w, f' stroke-dasharray="{dash}"'),
            line([(c, c), (b, c)], BLUE, w, f' stroke-dasharray="{dash}"'),
            line([(b, c), (b, b)], BLUE, w, f' stroke-dasharray="{dash}"')]
write('flat-pattern', 32, net(0.5, 1, 1, '2 1.5'))
write('flat-pattern', 16, net(0.5, 0.5, 1, '1.5 1'))

# ---------- документ и стрелка ----------
def doc(x, y, wd, ht, fold, w=1, fill=DOC):
    return [poly([(x, y), (x + wd - fold, y), (x + wd, y + fold), (x + wd, y + ht), (x, y + ht)], fill, INK2, w),
            poly([(x + wd - fold, y), (x + wd - fold, y + fold), (x + wd, y + fold)], DOC_FOLD, INK2, w)]

def arrow(x, y, s, color=BLUE):
    # стрелка вправо с белой обводкой, чтобы читалась поверх документа
    pts = [(x, y - 0.22 * s), (x + 0.5 * s, y - 0.22 * s), (x + 0.5 * s, y - 0.5 * s), (x + s, y),
           (x + 0.5 * s, y + 0.5 * s), (x + 0.5 * s, y + 0.22 * s), (x, y + 0.22 * s)]
    return [poly(pts, color, '#FFFFFF', 1.2)]

def mini_flat(x, y, k, w=1):
    return [poly([(x, y), (x + 6 * k, y), (x + 6 * k, y + 3 * k), (x + 9 * k, y + 3 * k), (x + 9 * k, y + 8 * k), (x, y + 8 * k)],
                 BLUE_L, S_INK, w)]

write('export', 32, doc(5.5, 3.5, 16, 22, 5) + mini_flat(8.5, 11.5, 1) + arrow(17, 21.5, 11))
write('export-all', 32, doc(10.5, 2.5, 14, 18, 4, fill='#F3F5F7') + doc(4.5, 6.5, 14, 20, 4) +
      mini_flat(7.5, 13.5, 0.9) + arrow(17, 22.5, 11))

# ---------- папки ----------
def folder(size, open_=False):
    """Папка на пиксельной сетке: задняя стенка с вкладкой и передняя (закрытая или открытая)."""
    if size == 32:
        back = [(2.5, 5.5), (11.5, 5.5), (14, 8.5), (27.5, 8.5), (27.5, 26.5), (2.5, 26.5)]
        front = ([(2.5, 26.5), (6.5, 13.5), (29.5, 13.5), (25.5, 26.5)] if open_
                 else [(2.5, 11.5), (27.5, 11.5), (27.5, 26.5), (2.5, 26.5)])
    else:
        back = [(1.5, 2.5), (6, 2.5), (7.5, 4), (13.5, 4), (13.5, 13.5), (1.5, 13.5)]
        front = ([(1.5, 13.5), (3.5, 7.5), (14.5, 7.5), (12.5, 13.5)] if open_
                 else [(1.5, 6.5), (13.5, 6.5), (13.5, 13.5), (1.5, 13.5)])
    return [poly(back, F_BACK, F_INK, 1), poly(front, F_FRONT, F_INK, 1)]

def gear(cx, cy, r_out, r_in, r_hole, teeth=8, w=1, fill='#DDE3E9'):
    pts = []
    for i in range(teeth):
        a0 = 2 * math.pi * i / teeth
        step = 2 * math.pi / teeth
        for frac, r in ((0.0, r_in), (0.12, r_out), (0.38, r_out), (0.5, r_in)):
            a = a0 + step * frac - math.pi / 2
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
        a = a0 + step - math.pi / 2
    body = [poly(pts, fill, INK, w)]
    body.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r_hole)}" fill="#FFFFFF" stroke="{INK}" stroke-width="{f(w)}"/>')
    return body

write('open-folder', 32, folder(32, True))
write('open-folder', 16, folder(16, True))
write('data-folder', 32, folder(32) + gear(21.5, 20.5, 6.5, 4.9, 1.9))
write('data-folder', 16, folder(16) + gear(10.8, 10.3, 3.6, 2.7, 1.1))

# ---------- шестерня ----------
write('settings', 32, gear(16, 16, 13, 9.8, 4.2), snap=False)
write('settings', 16, gear(8, 8, 6.5, 4.8, 2), snap=False)

# ---------- обновить ----------
def arc_points(cx, cy, r, a0, a1, step_deg=6):
    """Дуга ломаной (по часовой стрелке на экране от a0 до a1) — габариты считаются точно."""
    n = max(2, int(abs(a1 - a0) / math.radians(step_deg)))
    return [(cx + r * math.cos(a0 + (a1 - a0) * k / n), cy + r * math.sin(a0 + (a1 - a0) * k / n)) for k in range(n + 1)]

def refresh(c, r, w, head):
    # дуга 300° по часовой стрелке с разрывом сверху, на конце — залитый наконечник по касательной
    a0, a1 = math.radians(-60), math.radians(240)
    x0, y0 = c + r * math.cos(a0), c + r * math.sin(a0)
    x1, y1 = c + r * math.cos(a1), c + r * math.sin(a1)
    arc = line(arc_points(c, c, r, a0, a1), BLUE, w)
    tx, ty = -math.sin(a1), math.cos(a1)          # направление движения в конце дуги
    nx, ny = math.cos(a1), math.sin(a1)
    tip = (x1 + tx * head, y1 + ty * head)
    b1 = (x1 + nx * head * 0.7, y1 + ny * head * 0.7)
    b2 = (x1 - nx * head * 0.7, y1 - ny * head * 0.7)
    return [arc, poly([b1, tip, b2], BLUE, None)]
write('refresh', 32, refresh(16, 10, 2.2, 6), snap=False)
write('refresh', 16, refresh(8, 5.2, 1.4, 3.6), snap=False)

# ---------- развернуть / свернуть всё (дерево с +/−) ----------
def tree_icon(sign, k=1):
    q = lambda v: v * k
    body = [line([(q(4.5), q(7.5)), (q(4.5), q(11.5)), (q(8.5), q(11.5))], INK2, 1)]
    for (x, y) in ((1.5, 1.5), (8.5, 8.5)):
        body.append(poly([(q(x), q(y)), (q(x + 6), q(y)), (q(x + 6), q(y + 6)), (q(x), q(y + 6))], '#FFFFFF', INK2, 1))
        body.append(line([(q(x + 1.5), q(y + 3)), (q(x + 4.5), q(y + 3))], INK, k))
        if sign == '+':
            body.append(line([(q(x + 3), q(y + 1.5)), (q(x + 3), q(y + 4.5))], INK, k))
    return body
# Только 16 px: эти иконки стоят на маленьких кнопках.
write('expand-all', 16, tree_icon('+'))
write('collapse-all', 16, tree_icon('-'))

# ---------- флажки ----------
def box(x, y, s, w=1):
    return poly([(x, y), (x + s, y), (x + s, y + s), (x, y + s)], '#FFFFFF', INK2, w)
def tick(x, y, s, w):
    return line([(x + 0.22 * s, y + 0.52 * s), (x + 0.42 * s, y + 0.72 * s), (x + 0.8 * s, y + 0.3 * s)], GREEN, w)

# Только 16 px: эти иконки стоят на маленьких кнопках.
write('check-all', 16, [box(2.5, 2.5, 11), tick(2.5, 2.5, 11, 1.8)])
write('check-none', 16, [box(2.5, 2.5, 11)])
write('check-selected', 16, [
    poly([(1, 2.5), (15, 2.5), (15, 7.5), (1, 7.5)], BLUE_L, None),
    box(1.5, 3.5, 3), line([(2, 5), (2.8, 5.8), (4.1, 4.2)], GREEN, 1),
    line([(7, 5.5), (13, 5.5)], INK2, 1),
    box(1.5, 10.5, 3), line([(7, 12.5), (12, 12.5)], INK2, 1)])

# ---------- колонки ----------
write('columns', 32, [
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 26.5), (3.5, 26.5)], '#FFFFFF', INK2),
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 10.5), (3.5, 10.5)], DOC_FOLD, INK2),
    poly([(12.5, 10.5), (19.5, 10.5), (19.5, 26.5), (12.5, 26.5)], BLUE_L, None),
    line([(12.5, 5.5), (12.5, 26.5)], INK2), line([(19.5, 5.5), (19.5, 26.5)], INK2),
    line([(3.5, 15.5), (28.5, 15.5)], '#C9D1D9'), line([(3.5, 20.5), (28.5, 20.5)], '#C9D1D9'),
    poly([(12.5, 5.5), (19.5, 5.5), (19.5, 26.5), (12.5, 26.5)], 'none', BLUE, 1)])

# ---------- панель свойств ----------
write('details', 32, [
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 26.5), (3.5, 26.5)], '#FFFFFF', INK2),
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 8.5), (3.5, 8.5)], DOC_FOLD, INK2),
    poly([(18.5, 8.5), (28.5, 8.5), (28.5, 26.5), (18.5, 26.5)], BLUE_L, INK2),
    line([(20.5, 12.5), (26.5, 12.5)], BLUE, 1.2), line([(20.5, 16.5), (26.5, 16.5)], BLUE, 1.2),
    line([(20.5, 20.5), (24.5, 20.5)], BLUE, 1.2),
    line([(6.5, 12.5), (15.5, 12.5)], '#C9D1D9'), line([(6.5, 16.5), (15.5, 16.5)], '#C9D1D9'),
    line([(6.5, 20.5), (13.5, 20.5)], '#C9D1D9')])

# ---------- линии гиба: пластина с вырезами по линии гиба ----------
slots = []
for x in (4.5, 11, 17.5, 24):
    slots.append(poly([(x, 15), (x + 3.5, 15), (x + 3.5, 17), (x, 17)], BLUE, None))
write('bend-lines', 32, [
    poly([(2.5, 6.5), (29.5, 6.5), (29.5, 25.5), (2.5, 25.5)], S_TOP, S_INK),
    line([(2.5, 16), (29.5, 16)], BLUE, 0.8, ' stroke-dasharray="1 1"')] + slots)

# ---------- стандартное изделие: болт с шестигранной головкой ----------
def bolt(k, w):
    q = lambda v: v * k
    cx, cy, rx, ry, hh = 8, 3.6, 5.6, 2.4, 2.6     # головка: центр верхней грани, радиусы, высота
    hexa = [(cx + rx * c, cy + ry * s_) for c, s_ in ((-1, 0), (-0.5, -1), (0.5, -1), (1, 0), (0.5, 1), (-0.5, 1))]
    hexa = [(q(x), q(y)) for x, y in hexa]
    down = lambda p: (p[0], p[1] + q(hh))
    body = []
    # шток с резьбой
    body.append(poly([(q(6), q(cy + 2)), (q(10), q(cy + 2)), (q(10), q(13.6)), (q(6), q(13.6))], P_LEFT, INK, w))
    for y in (7.6, 9.4, 11.2):
        body.append(line([(q(6), q(y + 1)), (q(10), q(y))], INK2, w * 0.8))
    # боковые грани головки (три видимые)
    body.append(poly([hexa[0], hexa[5], down(hexa[5]), down(hexa[0])], P_LEFT, INK, w))
    body.append(poly([hexa[5], hexa[4], down(hexa[4]), down(hexa[5])], P_RIGHT, INK, w))
    body.append(poly([hexa[4], hexa[3], down(hexa[3]), down(hexa[4])], '#93A3B3', INK, w))
    body.append(poly(hexa, P_TOP, INK, w))
    return body
write('standard-part', 16, bolt(1, 1), snap=False)
write('standard-part', 32, bolt(2, 1), snap=False)

# ---------- линии гиба по отдельности: пластина, одна линия подсвечена ----------
HILITE = '#F0A030'
def bend_each(size):
    if size == 32:
        plate = [(2.5, 6.5), (29.5, 6.5), (29.5, 25.5), (2.5, 25.5)]
        xs, y0, y1, band = (9.5, 16.5, 23.5), 6.5, 25.5, 2.5
    else:
        plate = [(1.5, 3.5), (14.5, 3.5), (14.5, 12.5), (1.5, 12.5)]
        xs, y0, y1, band = (4.5, 8, 11.5), 3.5, 12.5, 1.5
    body = [poly(plate, S_TOP, S_INK)]
    mid = xs[1]
    body.append(poly([(mid - band, y0 + 0.5), (mid + band, y0 + 0.5), (mid + band, y1 - 0.5), (mid - band, y1 - 0.5)], HILITE, None))
    dash = ' stroke-dasharray="2 1.5"' if size == 32 else ' stroke-dasharray="1.5 1"'
    for x in xs:
        body.append(line([(x, y0), (x, y1)], BLUE, 1, dash))
    return body
write('bend-lines-each', 32, bend_each(32))
write('bend-lines-each', 16, bend_each(16))

# ---------- просмотр развертки: развертка и лупа ----------
def flat_view(size):
    if size == 32:
        body = net(0.5, 0.82, 1, '2 1.5')
        cx, cy, r, w, handle = 20.5, 20.5, 5.5, 2, (25, 25, 29.5, 29.5)
    else:
        body = net(0.5, 0.4, 1, '1.5 1')
        cx, cy, r, w, handle = 10, 10, 2.8, 1.4, (12.1, 12.1, 14, 14)
    body.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="#FFFFFF" fill-opacity="0.85" stroke="{INK}" stroke-width="{f(w)}"/>')
    body.append(line([(handle[0], handle[1]), (handle[2], handle[3])], INK, w + 0.6))
    return body
write('flat-view', 32, flat_view(32), snap=False)
write('flat-view', 16, flat_view(16), snap=False)

# ---------- список деталей: добавить / добавить все / удалить / очистить ----------
RED = '#C62828'

def sheet_list(x, y, wd, ht, rows, k=1, fill='#FFFFFF'):
    """Лист списка: рамка, строки с маленькой листовой деталью слева."""
    body = [poly([(x, y), (x + wd, y), (x + wd, y + ht), (x, y + ht)], fill, INK2, 1)]
    step = (ht - 2 * k) / rows
    for i in range(rows):
        ry = y + k + step * i + step / 2
        m = min(1.6 * k, step * 0.3)
        body.append(poly([(x + 1.5 * k, ry - m), (x + 1.5 * k + 2 * m, ry - m), (x + 1.5 * k + 2 * m, ry + m),
                          (x + 1.5 * k, ry + m)], S_LEFT, S_INK, 0.8 if k > 1 else 0.6))
        body.append(line([(x + 2.5 * k + 2 * m, ry), (x + wd - 1.5 * k, ry)], '#9AA6B2', 1))
    return body

def badge(cx, cy, r, sign, w):
    body = [f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="#FFFFFF" stroke="none"/>']
    a = r * 0.62
    color = GREEN if sign == '+' else RED
    if sign in '+-':
        body.append(line([(cx - a, cy), (cx + a, cy)], color, w))
    if sign == '+':
        body.append(line([(cx, cy - a), (cx, cy + a)], color, w))
    if sign == 'x':
        d = a * 0.8
        body.append(line([(cx - d, cy - d), (cx + d, cy + d)], color, w))
        body.append(line([(cx - d, cy + d), (cx + d, cy - d)], color, w))
    return body

write('list-add', 32, sheet_list(3.5, 3.5, 19, 22, 4, 2) + badge(23, 23, 7.5, '+', 2.6))
write('list-add', 16, sheet_list(1.5, 1.5, 9.5, 10.5, 3) + badge(11, 11, 3.8, '+', 1.6))
write('list-add-all', 32, sheet_list(8.5, 2.5, 16, 17, 3, 2, '#F3F5F7') + sheet_list(3.5, 7.5, 16, 19, 3, 2) +
      badge(23, 23, 7, '+', 2.6))
write('list-remove', 32, sheet_list(3.5, 3.5, 19, 22, 4, 2) + badge(23, 23, 7.5, '-', 2.6))
write('list-remove', 16, sheet_list(1.5, 1.5, 9.5, 10.5, 3) + badge(11, 11, 3.8, '-', 1.6))
write('list-clear', 16, sheet_list(1.5, 1.5, 9.5, 10.5, 3) + badge(11, 11, 3.8, 'x', 1.5))

# ---------- сортировка: строки убывающей длины и стрелка ----------
def sort_icon(size):
    if size == 32:
        bars = [line([(4, y), (4 + ln, y)], INK2, 2.2) for y, ln in ((7, 15), (13, 11), (19, 7), (25, 3.5))]
        arrow_ = [line([(25, 4.5), (25, 25)], BLUE, 2.2),
                  poly([(20, 21), (30, 21), (25, 28)], BLUE, None)]
    else:
        bars = [line([(2, y), (2 + ln, y)], INK2, 1.4) for y, ln in ((3.5, 7.5), (6.8, 5.5), (10.1, 3.5), (13.4, 1.6))]
        arrow_ = [line([(12.5, 2), (12.5, 12)], BLUE, 1.4),
                  poly([(9.6, 10.4), (15.4, 10.4), (12.5, 14.4)], BLUE, None)]
    return bars + arrow_
write('sort', 32, sort_icon(32))
write('sort', 16, sort_icon(16))

# ---------- панель инструментов просмотра развертки (только 16 px) ----------
write('nav-prev', 16, [line([(10, 3), (5, 8), (10, 13)], INK, 1.6)])
write('nav-next', 16, [line([(6, 3), (11, 8), (6, 13)], INK, 1.6)])

def magnifier(sign):
    body = [f'<circle cx="6.5" cy="6.5" r="4.5" fill="#FFFFFF" stroke="{INK}" stroke-width="1.3"/>',
            line([(9.8, 9.8), (14, 14)], INK, 2)]
    body.append(line([(4.5, 6.5), (8.5, 6.5)], BLUE, 1.3))
    if sign == '+':
        body.append(line([(6.5, 4.5), (6.5, 8.5)], BLUE, 1.3))
    return body
write('zoom-in', 16, magnifier('+'), snap=False)
write('zoom-out', 16, magnifier('-'), snap=False)

write('zoom-fit', 16, [
    line([(2, 5), (2, 2), (5, 2)], INK, 1.2), line([(11, 2), (14, 2), (14, 5)], INK, 1.2),
    line([(14, 11), (14, 14), (11, 14)], INK, 1.2), line([(5, 14), (2, 14), (2, 11)], INK, 1.2),
    poly([(4.5, 5.5), (11.5, 5.5), (11.5, 10.5), (4.5, 10.5)], S_TOP, S_INK, 1)])

def dimension(y=8):
    return [line([(1.5, y - 4), (1.5, y + 4)], INK2, 1), line([(14.5, y - 4), (14.5, y + 4)], INK2, 1),
            line([(4, y), (12, y)], BLUE, 1.2),
            poly([(2, y), (5, y - 2.2), (5, y + 2.2)], BLUE, None), poly([(14, y), (11, y - 2.2), (11, y + 2.2)], BLUE, None)]
write('measure-distance', 16, dimension())

write('measure-angle', 16, [
    line([(2, 13.5), (14.5, 13.5)], INK, 1.3), line([(2, 13.5), (11.5, 3)], INK, 1.3),
    line(arc_points(2, 13.5, 8, math.radians(-48), 0, 4), BLUE, 1.3)])

write('bend-numbers', 16, [
    line([(1.5, 14.5), (14.5, 1.5)], BLUE, 1, ' stroke-dasharray="1.5 1"'),
    f'<circle cx="8" cy="8" r="4.6" fill="#FFFFFF" stroke="{INK}" stroke-width="1"/>',
    line([(7.2, 6.4), (8.3, 5.6), (8.3, 10.6)], INK, 1.1)], snap=False)

write('measure-clear', 16, [line([(1.5, 2), (1.5, 8)], INK2, 1), line([(11.5, 2), (11.5, 8)], INK2, 1),
                            line([(1.5, 5), (11.5, 5)], BLUE, 1.2)] + badge(11, 11, 3.8, 'x', 1.5))

# ---------- материалы: стопка листов разных материалов / лист с отметкой ----------
def plate(cx, top, s, h, faces, ink, w):
    # тонкие боковые грани дают острые углы — со «скосом» на стыках нет торчащих иголок
    return [p.replace('stroke-linejoin="miter"', 'stroke-linejoin="bevel"') for p in cube(cx, top, s, h, faces, ink, w)]

def plate_stack(size):
    if size == 32:
        s, h, step, top, w = 12.5, 2.4, 5.2, 3.5, 1
    else:
        s, h, step, top, w = 6.4, 1.3, 2.6, 1.4, 0.8
    c = size / 2
    body = []
    layers = [((A_TOP, A_LEFT, A_RIGHT), A_INK), ((P_TOP, P_LEFT, P_RIGHT), INK), ((S_TOP, S_LEFT, S_RIGHT), S_INK)]
    for i, (faces, ink) in enumerate(layers):
        body += plate(c, top + step * (2 - i), s, h, faces, ink, w)
    return body
write('material-library', 32, plate_stack(32), snap=False)
write('material-library', 16, plate_stack(16), snap=False)

def assign_icon(size):
    if size == 32:
        body = plate(13, 4, 12, 3, (S_TOP, S_LEFT, S_RIGHT), S_INK, 1)
        cx, cy, r, w = 23, 23, 7, 2.4
    else:
        body = plate(6.5, 1.5, 6, 1.5, (S_TOP, S_LEFT, S_RIGHT), S_INK, 0.8)
        cx, cy, r, w = 11, 11, 3.8, 1.5
    body.append(f'<circle cx="{f(cx)}" cy="{f(cy)}" r="{f(r)}" fill="#FFFFFF" stroke="none"/>')
    body.append(line([(cx - r * 0.55, cy + r * 0.02), (cx - r * 0.12, cy + r * 0.45), (cx + r * 0.58, cy - r * 0.4)], GREEN, w))
    return body
write('material-assign', 32, assign_icon(32), snap=False)
write('material-assign', 16, assign_icon(16), snap=False)

# ---------- ведомость: лист с таблицей ----------
def report_icon(size):
    if size == 32:
        x, y, wd, ht, fold, k = 6.5, 2.5, 19, 27, 5, 1
    else:
        x, y, wd, ht, fold, k = 2.5, 1.5, 11, 13, 3, 0.55
    body = doc(x, y, wd, ht, fold)
    top = y + fold + 2.5 * k
    left, right = x + 3 * k, x + wd - 3 * k
    rows = 4 if size == 32 else 3
    step = (y + ht - 3 * k - top) / rows
    body.append(poly([(left, top), (right, top), (right, top + step), (left, top + step)], BLUE_L, None))
    for i in range(rows + 1):
        body.append(line([(left, top + step * i), (right, top + step * i)], INK2, 0.8 if size == 32 else 0.6))
    mid = left + (right - left) * 0.55
    body.append(line([(mid, top), (mid, top + step * rows)], INK2, 0.8 if size == 32 else 0.6))
    return body
write('report', 32, report_icon(32))
write('report', 16, report_icon(16))

if ERRORS:
    raise SystemExit('\n'.join(ERRORS))
print('ok')
