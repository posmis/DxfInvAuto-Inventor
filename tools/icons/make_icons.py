# Генератор SVG-исходников иконок InvAuto в стиле Inventor 2025 (рисунки на 32 и на 16 px).
# Геометрия (изометрия, зубцы шестерни) считается здесь, палитра — в константах ниже.
# Запуск из корня репозитория:  python tools/icons/make_icons.py assets/icons
# затем:                        node tools/icons/build-icons.mjs
# Иконку можно и нарисовать вручную: положить имя.svg (32×32) и/или имя.16.svg (16×16) в assets/icons.
import math, os, sys
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

def write(name, size, body):
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
write('part', 32, cube(16, 3.5, 13, 13, (P_TOP, P_LEFT, P_RIGHT), INK))
write('part', 16, cube(8, 1.5, 6.9, 6.5, (P_TOP, P_LEFT, P_RIGHT), INK))

# ---------- сборка: две детали ----------
write('assembly', 32,
      cube(20, 2.5, 9, 9, (P_TOP, P_LEFT, P_RIGHT), INK) +
      cube(11.5, 11.5, 9, 9, (A_TOP, A_LEFT, A_RIGHT), A_INK))
write('assembly', 16,
      cube(10.5, 1, 4.8, 4.8, (P_TOP, P_LEFT, P_RIGHT), INK) +
      cube(5.6, 5.8, 4.8, 4.8, (A_TOP, A_LEFT, A_RIGHT), A_INK))

# ---------- листовая деталь: уголок с толщиной ----------
def bracket(sc, ox, oy, w):
    c, s = math.cos(math.radians(30)), 0.5
    def iso(x, y, z):  # x вправо-вниз, y влево-вниз, z вверх
        return (ox + (x - y) * c * sc, oy + ((x + y) * s - z) * sc)
    L, D, H, T = 12, 7, 8, 1.1   # длина, глубина полки, высота стенки, толщина
    body = []
    # горизонтальная полка (верх и торцы)
    body.append(poly([iso(0, 0, T), iso(L, 0, T), iso(L, D, T), iso(0, D, T)], S_TOP, S_INK, w))
    body.append(poly([iso(0, D, T), iso(L, D, T), iso(L, D, 0), iso(0, D, 0)], S_LEFT, S_INK, w))
    body.append(poly([iso(L, 0, T), iso(L, D, T), iso(L, D, 0), iso(L, 0, 0)], S_RIGHT, S_INK, w))
    # вертикальная стенка по заднему краю (y = 0)
    body.append(poly([iso(0, 0, T), iso(L, 0, T), iso(L, 0, H), iso(0, 0, H)], S_LEFT, S_INK, w))
    body.append(poly([iso(0, -T, H), iso(L, -T, H), iso(L, 0, H), iso(0, 0, H)], S_TOP, S_INK, w))
    body.append(poly([iso(L, -T, H), iso(L, 0, H), iso(L, 0, T), iso(L, -T, 0), iso(L, -T, H)], S_RIGHT, S_INK, w))
    return body

write('sheet-metal-part', 32, bracket(1.55, 13.5, 6.5, 1))
write('sheet-metal-part', 16, bracket(0.76, 6.8, 3.0, 0.8))
write('sheet-metal', 32, bracket(1.55, 13.5, 6.5, 1))
write('sheet-metal', 16, bracket(0.76, 6.8, 3.0, 0.8))

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

write('export', 32, doc(6.5, 2.5, 17, 24, 5) + mini_flat(10.5, 11.5, 1) + arrow(18.5, 24.5, 12))
write('export-all', 32, doc(10.5, 1.5, 15, 19, 4, fill='#F3F5F7') + doc(4.5, 6.5, 15, 22, 4) +
      mini_flat(7.5, 14.5, 0.9) + arrow(18.5, 25, 12))

# ---------- папки ----------
def folder(k, w, open_=False):
    o = 0.5
    back = [(o + 1 * k, o + 4 * k), (o + 11 * k, o + 4 * k), (o + 13.5 * k, o + 7 * k), (o + 29 * k, o + 7 * k),
            (o + 29 * k, o + 26 * k), (o + 1 * k, o + 26 * k)]
    body = [poly(back, F_BACK, F_INK, w)]
    if open_:
        body.append(poly([(o + 1 * k, o + 26 * k), (o + 5.5 * k, o + 13 * k), (o + 31 * k, o + 13 * k), (o + 27 * k, o + 26 * k)],
                         F_FRONT, F_INK, w))
    else:
        body.append(poly([(o + 1 * k, o + 10 * k), (o + 29 * k, o + 10 * k), (o + 29 * k, o + 26 * k), (o + 1 * k, o + 26 * k)],
                         F_FRONT, F_INK, w))
    return body

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

write('open-folder', 32, folder(1, 1, True))
write('open-folder', 16, folder(0.5, 1, True))
write('data-folder', 32, folder(1, 1) + gear(23, 23, 7.5, 5.6, 2.2))
write('data-folder', 16, folder(0.5, 1) + gear(11.5, 11.5, 4.3, 3.1, 1.2))

# ---------- шестерня ----------
write('settings', 32, gear(16, 16, 13, 9.8, 4.2))
write('settings', 16, gear(8, 8, 7, 5.2, 2.2))

# ---------- обновить ----------
def refresh(c, r, w, head):
    # дуга 300° по часовой стрелке с разрывом сверху, на конце — залитый наконечник по касательной
    a0, a1 = math.radians(-60), math.radians(240)
    x0, y0 = c + r * math.cos(a0), c + r * math.sin(a0)
    x1, y1 = c + r * math.cos(a1), c + r * math.sin(a1)
    arc = f'<path d="M{f(x0)} {f(y0)} A{f(r)} {f(r)} 0 1 1 {f(x1)} {f(y1)}" fill="none" stroke="{BLUE}" stroke-width="{f(w)}"/>'
    tx, ty = -math.sin(a1), math.cos(a1)          # направление движения в конце дуги
    nx, ny = math.cos(a1), math.sin(a1)
    tip = (x1 + tx * head, y1 + ty * head)
    b1 = (x1 + nx * head * 0.7, y1 + ny * head * 0.7)
    b2 = (x1 - nx * head * 0.7, y1 - ny * head * 0.7)
    return [arc, poly([b1, tip, b2], BLUE, None)]
write('refresh', 32, refresh(16, 10, 2.2, 6))
write('refresh', 16, refresh(8, 5.2, 1.4, 3.6))

# ---------- развернуть / свернуть всё (дерево с +/−) ----------
def tree_icon(sign, k=1):
    q = lambda v: v * k
    body = [line([(q(4.5), q(6)), (q(4.5), q(12.5)), (q(8), q(12.5))], INK2, 1)]
    for (x, y) in ((1.5, 1.5), (8.5, 9.5)):
        body.append(poly([(q(x), q(y)), (q(x + 6), q(y)), (q(x + 6), q(y + 6)), (q(x), q(y + 6))], '#FFFFFF', INK2, 1))
        body.append(line([(q(x + 1.5), q(y + 3)), (q(x + 4.5), q(y + 3))], INK, k))
        if sign == '+':
            body.append(line([(q(x + 3), q(y + 1.5)), (q(x + 3), q(y + 4.5))], INK, k))
    return body
for k, size in ((1, 16), (2, 32)):
    write('expand-all', size, tree_icon('+', k))
    write('collapse-all', size, tree_icon('-', k))

# ---------- флажки ----------
def box(x, y, s, w=1):
    return poly([(x, y), (x + s, y), (x + s, y + s), (x, y + s)], '#FFFFFF', INK2, w)
def tick(x, y, s, w):
    return line([(x + 0.22 * s, y + 0.52 * s), (x + 0.42 * s, y + 0.72 * s), (x + 0.8 * s, y + 0.3 * s)], GREEN, w)

for k, size in ((1, 16), (2, 32)):
    q = lambda v: v * k
    write('check-all', size, [box(q(2.5), q(2.5), q(11)), tick(q(2.5), q(2.5), q(11), 1.8 * k)])
    write('check-none', size, [box(q(2.5), q(2.5), q(11))])
    write('check-selected', size, [
        poly([(q(0.5), q(2.5)), (q(15.5), q(2.5)), (q(15.5), q(7.5)), (q(0.5), q(7.5))], BLUE_L, None),
        box(q(1.5), q(3.5), q(3)), line([(q(2), q(5)), (q(2.8), q(5.8)), (q(4.1), q(4.2))], GREEN, k),
        line([(q(7), q(5.5)), (q(14), q(5.5))], INK2, k),
        box(q(1.5), q(10.5), q(3)), line([(q(7), q(12.5)), (q(12), q(12.5))], INK2, k)])

# ---------- колонки ----------
write('columns', 32, [
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 26.5), (3.5, 26.5)], '#FFFFFF', INK2),
    poly([(3.5, 5.5), (28.5, 5.5), (28.5, 10.5), (3.5, 10.5)], DOC_FOLD, INK2),
    poly([(12.5, 10.5), (19.5, 10.5), (19.5, 26.5), (12.5, 26.5)], BLUE_L, None),
    line([(12.5, 5.5), (12.5, 26.5)], INK2), line([(19.5, 5.5), (19.5, 26.5)], INK2),
    line([(3.5, 15.5), (28.5, 15.5)], '#C9D1D9'), line([(3.5, 20.5), (28.5, 20.5)], '#C9D1D9'),
    poly([(12.5, 5.5), (19.5, 5.5), (19.5, 26.5), (12.5, 26.5)], 'none', BLUE, 1.2)])

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
    body.append(poly([(q(6), q(cy + 2)), (q(10), q(cy + 2)), (q(10), q(15)), (q(6), q(15))], P_LEFT, INK, w))
    for y in (8.5, 10.5, 12.5):
        body.append(line([(q(6), q(y + 1)), (q(10), q(y))], INK2, w * 0.8))
    # боковые грани головки (три видимые)
    body.append(poly([hexa[0], hexa[5], down(hexa[5]), down(hexa[0])], P_LEFT, INK, w))
    body.append(poly([hexa[5], hexa[4], down(hexa[4]), down(hexa[5])], P_RIGHT, INK, w))
    body.append(poly([hexa[4], hexa[3], down(hexa[3]), down(hexa[4])], '#93A3B3', INK, w))
    body.append(poly(hexa, P_TOP, INK, w))
    return body
write('standard-part', 16, bolt(1, 1))
write('standard-part', 32, bolt(2, 1))

print('ok')
