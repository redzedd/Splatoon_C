# -*- coding: utf-8 -*-
"""charlib.py - shared helpers for the procedural Splatoon-style character builds.

Used by build_inkling.py / build_squid.py (Blender 4.3, run headless with -b -P).
Everything is generated from code: no modifiers other than Armature ever exist, so
"apply all modifiers except Armature" is satisfied by construction.

Coordinate convention (Blender): Z up, character faces -Y, character's left = +X.
"""
import bpy
import bmesh
import math
import os
import sys
import tempfile
from mathutils import Vector, Matrix

# --------------------------------------------------------------------------------------
# small math helpers
# --------------------------------------------------------------------------------------

def hex_rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))


def srgb_to_lin(x):
    return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4


def lin_from_hex(h):
    return tuple(srgb_to_lin(c) for c in hex_rgb(h))


def clamp(x, a=0.0, b=1.0):
    return a if x < a else (b if x > b else x)


def smoothstep(e0, e1, x):
    if e1 == e0:
        return 1.0 if x >= e1 else 0.0
    t = clamp((x - e0) / (e1 - e0))
    return t * t * (3.0 - 2.0 * t)


def interp(tab, x):
    """Piecewise-linear lookup. tab = [(x, value), ...] sorted by x; value float or tuple."""
    if x <= tab[0][0]:
        return tab[0][1]
    for (x0, v0), (x1, v1) in zip(tab, tab[1:]):
        if x <= x1:
            t = (x - x0) / (x1 - x0) if x1 > x0 else 1.0
            if isinstance(v0, tuple):
                return tuple(a + (b - a) * t for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * t
    return tab[-1][1]


def sinterp(tab, x):
    """Like interp but eased (smoothstep) between keys -> C1-looking profiles."""
    if x <= tab[0][0]:
        return tab[0][1]
    for (x0, v0), (x1, v1) in zip(tab, tab[1:]):
        if x <= x1:
            t = (x - x0) / (x1 - x0) if x1 > x0 else 1.0
            t = t * t * (3 - 2 * t)
            if isinstance(v0, tuple):
                return tuple(a + (b - a) * t for a, b in zip(v0, v1))
            return v0 + (v1 - v0) * t
    return tab[-1][1]


def wblend(a, b, t):
    """Weight dict blending bone a -> bone b by t (0..1)."""
    t = clamp(t)
    if a == b or t <= 0.0:
        return {a: 1.0}
    if t >= 1.0:
        return {b: 1.0}
    return {a: 1.0 - t, b: t}


def wadd(*ws):
    out = {}
    for w, k in ws:
        for n, v in w.items():
            out[n] = out.get(n, 0.0) + v * k
    return wnorm(out)


def wnorm(w):
    w = {k: v for k, v in w.items() if v > 1e-4}
    s = sum(w.values())
    return {k: v / s for k, v in w.items()} if s > 0 else w


def chain_weights(s, joints, bones, widths):
    """Weights along a 1-D chain.  bones[i] owns s in (joints[i-1], joints[i]);
    joints[i] is the boundary between bones[i] and bones[i+1]; widths[i] = half blend width."""
    w = {bones[0]: 1.0}
    for j, (sj, bw) in enumerate(zip(joints, widths)):
        t = smoothstep(sj - bw, sj + bw, s)
        if t <= 0.0:
            break
        # everything accumulated so far fades to the next bone by t
        w = {k: v * (1.0 - t) for k, v in w.items()}
        w[bones[j + 1]] = w.get(bones[j + 1], 0.0) + t
    return wnorm(w)


def basis(fwd, up):
    """Rotation matrix whose columns are X=right, Y=fwd(normalized), Z=up(orthogonalized)."""
    y = Vector(fwd).normalized()
    z = Vector(up) - y * Vector(up).dot(y)
    z.normalize()
    x = y.cross(z)
    return Matrix((x, y, z)).transposed()


def frame_zy(z_axis, y_hint):
    """Rotation matrix with local Z = z_axis and local Y as close as possible to y_hint."""
    z = Vector(z_axis).normalized()
    y = Vector(y_hint) - z * Vector(y_hint).dot(z)
    y.normalize()
    x = y.cross(z)
    return Matrix((x, y, z)).transposed()


# --------------------------------------------------------------------------------------
# mesh builder: verts + per-vertex bone weights + faces + per-face material name
# --------------------------------------------------------------------------------------

class MB:
    def __init__(self):
        self.V, self.W, self.F, self.M = [], [], [], []

    def v(self, co, w):
        self.V.append(Vector(co))
        self.W.append(dict(w))
        return len(self.V) - 1

    def f(self, ids, mat):
        self.F.append(tuple(ids))
        self.M.append(mat)

    def tris(self):
        return sum(len(f) - 2 for f in self.F)

    def extend(self, other):
        off = len(self.V)
        self.V += other.V
        self.W += other.W
        self.F += [tuple(i + off for i in f) for f in other.F]
        self.M += other.M


def _w_of(w, p):
    return w(p) if callable(w) else w


def loft(mb, rings, mats, wts, cap0=None, cap1=None, capmat0=None, capmat1=None):
    """Bridge consecutive closed rings (same vertex count) with quads.
    mats: material name or list (one per band).  wts: weight dict/callable or list per ring.
    cap0/cap1: None (open), 'fan' (centroid) or a Vector tip point."""
    ids = []
    for i, ring in enumerate(rings):
        w = wts[i] if isinstance(wts, list) else wts
        ids.append([mb.v(p, _w_of(w, p)) for p in ring])
    n = len(rings[0])
    for i in range(len(rings) - 1):
        m = mats[i] if isinstance(mats, list) else mats
        for j in range(n):
            k = (j + 1) % n
            mb.f((ids[i][j], ids[i][k], ids[i + 1][k], ids[i + 1][j]), m)
    for cap, ring_ids, ring_pts, cm, w in (
            (cap0, ids[0], rings[0], capmat0 or (mats[0] if isinstance(mats, list) else mats),
             wts[0] if isinstance(wts, list) else wts),
            (cap1, ids[-1], rings[-1], capmat1 or (mats[-1] if isinstance(mats, list) else mats),
             wts[-1] if isinstance(wts, list) else wts)):
        if cap is None:
            continue
        c = sum(ring_pts, Vector()) / len(ring_pts) if isinstance(cap, str) else Vector(cap)
        ci = mb.v(c, _w_of(w, c))
        for j in range(n):
            mb.f((ci, ring_ids[(j + 1) % n], ring_ids[j]), cm)
    return ids


def torus_loft(mb, rings, mat, w):
    """Like loft, but the last ring connects back to the first (closed torus-like surface)."""
    ids = [[mb.v(p, _w_of(w, p)) for p in r] for r in rings]
    K, n = len(rings), len(rings[0])
    for i in range(K):
        i2 = (i + 1) % K
        for j in range(n):
            j2 = (j + 1) % n
            mb.f((ids[i][j], ids[i][j2], ids[i2][j2], ids[i2][j]), mat)
    return ids


def se_ring(center, ax_u, ax_v, ra, rb, n, e=2.0, phase=0.0):
    """Closed superellipse ring (exponent e; 2 = ellipse, bigger = boxier)."""
    pts = []
    for j in range(n):
        t = 2.0 * math.pi * j / n + phase
        c, s = math.cos(t), math.sin(t)
        cu = math.copysign(abs(c) ** (2.0 / e), c)
        sv = math.copysign(abs(s) ** (2.0 / e), s)
        pts.append(Vector(center) + Vector(ax_u) * (ra * cu) + Vector(ax_v) * (rb * sv))
    return pts


class Path:
    """Catmull-Rom spline through control points, arc-length parameterised."""

    def __init__(self, pts, n=600):
        P = [Vector(p) for p in pts]
        ext = [2 * P[0] - P[1]] + P + [2 * P[-1] - P[-2]]
        segs = len(P) - 1
        per = max(1, n // segs)
        dense = []
        for i in range(segs):
            p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
            for k in range(per):
                u = k / per
                dense.append(0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u
                                    + (-p0 + 3 * p1 - 3 * p2 + p3) * u * u * u))
        dense.append(P[-1].copy())
        L = [0.0]
        for a, b in zip(dense, dense[1:]):
            L.append(L[-1] + (b - a).length)
        self.dense, self.L = dense, L
        self.total = L[-1]
        self.ctrl_s = [L[min(i * per, len(L) - 1)] for i in range(len(P))]

    def at(self, s):
        s = clamp(s, 0.0, self.total)
        L = self.L
        lo, hi = 0, len(L) - 1
        while hi - lo > 1:
            mid = (lo + hi) // 2
            if L[mid] <= s:
                lo = mid
            else:
                hi = mid
        t = (s - L[lo]) / (L[hi] - L[lo]) if L[hi] > L[lo] else 0.0
        p = self.dense[lo].lerp(self.dense[hi], t)
        a = self.dense[max(lo - 2, 0)]
        b = self.dense[min(hi + 2, len(self.dense) - 1)]
        return p, (b - a).normalized()

    def cs(self, idx, off=0.0):
        return self.ctrl_s[idx] + off


def tube(mb, path, specs, nseg, w_fn, side_fn=None, cap0='dome', cap1='dome', e=2.0,
         dome_k=3, cap_len0=None, cap_len1=None, phase=0.0, tip_len1=None):
    """Sweep a (super)elliptic cross-section along `path`.
    specs: [(s, ra, rb, mat), ...] with s = arc length; mat is the band material to the next ring
    (the last spec's mat is used for the end dome/cap).
    ra is measured along the 'side' direction N, rb along B = T x N.
    w_fn(s) -> weight dict.  side_fn(point, tangent, s) -> side vector (default: world Z/X).
    cap0/cap1: 'dome' (rounded), 'flat', 'point' (cap1 only, tip_len1 ahead) or None."""
    def frame(s):
        p, T = path.at(s)
        side = side_fn(p, T, s) if side_fn else (Vector((0, 0, 1)) if abs(T.z) < 0.9 else Vector((1, 0, 0)))
        N = side - T * side.dot(T)
        if N.length < 1e-6:
            N = T.orthogonal()
        N.normalize()
        B = T.cross(N)
        return p, T, N, B

    rings, mats, wts = [], [], []
    # start dome (built tip -> body)
    s0, ra0, rb0, m0 = specs[0]
    p0, T0, N0, B0 = frame(s0)
    if cap0 == 'dome':
        cl = cap_len0 if cap_len0 is not None else 0.5 * (ra0 + rb0)
        for k in range(dome_k, 0, -1):
            a = k / (dome_k + 1) * math.pi / 2
            c = p0 - T0 * (cl * math.sin(a))
            rings.append(se_ring(c, N0, B0, ra0 * math.cos(a), rb0 * math.cos(a), nseg, e, phase))
            mats.append(m0)
            wts.append(w_fn(s0))
    for i, (s, ra, rb, m) in enumerate(specs):
        p, T, N, B = frame(s)
        rings.append(se_ring(p, N, B, ra, rb, nseg, e, phase))
        mats.append(m)
        wts.append(w_fn(s))
    s1, ra1, rb1, m1 = specs[-1]
    p1, T1, N1, B1 = frame(s1)
    if cap1 == 'dome':
        cl = cap_len1 if cap_len1 is not None else 0.5 * (ra1 + rb1)
        for k in range(1, dome_k + 1):
            a = k / (dome_k + 1) * math.pi / 2
            c = p1 + T1 * (cl * math.sin(a))
            rings.append(se_ring(c, N1, B1, ra1 * math.cos(a), rb1 * math.cos(a), nseg, e, phase))
            mats.append(m1)
            wts.append(w_fn(s1))
    tip0 = (p0 - T0 * (cap_len0 if cap_len0 is not None else 0.5 * (ra0 + rb0))) if cap0 == 'dome' else \
        ('fan' if cap0 == 'flat' else None)
    if cap1 == 'dome':
        tip1 = p1 + T1 * (cap_len1 if cap_len1 is not None else 0.5 * (ra1 + rb1))
    elif cap1 == 'point':
        tip1 = p1 + T1 * (tip_len1 or 0.02)
    elif cap1 == 'flat':
        tip1 = 'fan'
    else:
        tip1 = None
    band_mats = mats[:-1]
    loft(mb, rings, band_mats, wts, cap0=tip0, cap1=tip1, capmat0=m0, capmat1=m1)
    return rings


def ellipsoid(mb, center, radii, nseg, nring, mat, w, rot=None, deform=None, mat_fn=None, hemi=False):
    """UV ellipsoid. rot: 3x3 matrix whose columns are the local axes. deform(p)->p (world).
    hemi=True: only the local +Z half (nring rings down to the equator, closed by a flat fan)."""
    rot = rot or Matrix.Identity(3)
    C = Vector(center)
    rx, ry, rz = radii

    def P(th, ph):
        loc = Vector((rx * math.sin(th) * math.cos(ph), ry * math.sin(th) * math.sin(ph), rz * math.cos(th)))
        p = C + rot @ loc
        return deform(p) if deform else p

    rings = []
    if hemi:
        for i in range(1, nring + 1):
            th = 0.5 * math.pi * i / nring
            rings.append([P(th, 2 * math.pi * j / nseg) for j in range(nseg)])
        bot = 'fan'
    else:
        for i in range(1, nring):
            th = math.pi * i / nring
            rings.append([P(th, 2 * math.pi * j / nseg) for j in range(nseg)])
        bot = P(math.pi, 0.0)
    top = P(0.0, 0.0)
    if mat_fn is None:
        loft(mb, rings, mat, w, cap0=top, cap1=bot)
    else:
        start = len(mb.F)
        loft(mb, rings, mat, w, cap0=top, cap1=bot)
        for fi in range(start, len(mb.F)):
            c = sum((mb.V[i] for i in mb.F[fi]), Vector()) / len(mb.F[fi])
            mb.M[fi] = mat_fn(c)


class Ellip:
    """Surface parameterisation of an ellipsoid (e.g. the head) in its own frame:
    phi = azimuth in degrees from the front (-Y local, + towards +X), lam = elevation in degrees."""

    def __init__(self, center, radii, frame=None):
        self.C = Vector(center)
        self.r = radii
        self.frame = frame or Matrix.Identity(3)

    def dir_local(self, phi, lam):
        p, l = math.radians(phi), math.radians(lam)
        return Vector((math.sin(p) * math.cos(l), -math.cos(p) * math.cos(l), math.sin(l)))

    def normal(self, phi, lam):
        d = self.dir_local(phi, lam)
        rx, ry, rz = self.r
        return (self.frame @ Vector((d.x / rx, d.y / ry, d.z / rz))).normalized()

    def pt(self, phi, lam, off=0.0):
        d = self.dir_local(phi, lam)
        rx, ry, rz = self.r
        loc = Vector((d.x * rx, d.y * ry, d.z * rz))
        n = Vector((d.x / rx, d.y / ry, d.z / rz)).normalized()
        return self.C + self.frame @ (loc + n * off)


# --------------------------------------------------------------------------------------
# Blender object helpers
# --------------------------------------------------------------------------------------

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.cameras,
                 bpy.data.lights, bpy.data.images, bpy.data.actions):
        for d in list(coll):
            coll.remove(d)
    for c in list(bpy.data.collections):
        bpy.data.collections.remove(c)


def get_coll(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def make_material(name, hexcol, rough=0.5, spec=0.5):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = lin_from_hex(hexcol)
    bsdf.inputs["Base Color"].default_value = (*col, 1.0)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Specular IOR Level"].default_value = spec
    bsdf.inputs["Alpha"].default_value = 1.0
    m.diffuse_color = (*col, 1.0)
    m["export_hex"] = hexcol
    return m


def set_mat_color(name, hexcol, alpha=1.0):
    m = bpy.data.materials[name]
    bsdf = m.node_tree.nodes["Principled BSDF"]
    col = lin_from_hex(hexcol)
    bsdf.inputs["Base Color"].default_value = (*col, 1.0)
    bsdf.inputs["Alpha"].default_value = alpha
    m.diffuse_color = (*col, alpha)
    m.surface_render_method = 'BLENDED' if alpha < 1.0 else 'DITHERED'


def restore_export_colors():
    for m in bpy.data.materials:
        if "export_hex" in m:
            set_mat_color(m.name, m["export_hex"], 1.0)


def build_object(mb, name, mat_names, coll, sharp_deg=48.0, groups=True):
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in mb.V], [], mb.F)
    me.update()
    me.validate(verbose=True)
    assert len(me.polygons) == len(mb.F) and len(me.vertices) == len(mb.V), "mesh validate changed topology"
    for mn in mat_names:
        me.materials.append(bpy.data.materials[mn])
    idx = {mn: i for i, mn in enumerate(mat_names)}
    for p, mn in zip(me.polygons, mb.M):
        p.material_index = idx[mn]
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.shade_smooth()
    me.set_sharp_from_angle(angle=math.radians(sharp_deg))
    obj = bpy.data.objects.new(name, me)
    coll.objects.link(obj)
    if groups:
        vg = {}
        for vi, w in enumerate(mb.W):
            for bn, wt in w.items():
                if wt <= 1e-4:
                    continue
                g = vg.get(bn)
                if g is None:
                    g = vg[bn] = obj.vertex_groups.new(name=bn)
                g.add([vi], float(wt), 'REPLACE')
    return obj


def tri_count(obj):
    me = obj.data
    return sum(len(p.vertices) - 2 for p in me.polygons)


def create_armature(name, bones, coll, scale=1.0):
    """bones: list of (name, head, tail, parent, connect, roll_z_vector)."""
    arm = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, arm)
    coll.objects.link(rig)
    arm.display_type = 'STICK'
    rig.show_in_front = True
    bpy.context.view_layer.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    for bn, head, tail, parent, connect, rollv in bones:
        eb = arm.edit_bones.new(bn)
        eb.head = Vector(head) * scale
        eb.tail = Vector(tail) * scale
        eb.align_roll(Vector(rollv))
        eb.use_deform = True
        if parent:
            eb.parent = arm.edit_bones[parent]
            eb.use_connect = connect
    bpy.ops.object.mode_set(mode='OBJECT')
    return rig


def skin_to(obj, rig):
    obj.parent = rig
    obj.parent_type = 'OBJECT'
    obj.matrix_parent_inverse = Matrix.Identity(4)
    mod = obj.modifiers.new("Armature", 'ARMATURE')
    mod.object = rig
    mod.use_vertex_groups = True
    mod.use_bone_envelopes = False


def parent_to_bone(obj, rig, bone_name, world_matrix):
    obj.parent = rig
    obj.parent_type = 'BONE'
    obj.parent_bone = bone_name
    bpy.context.view_layer.update()
    obj.matrix_world = world_matrix
    bpy.context.view_layer.update()


def reset_pose(rig):
    for pb in rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


def deform_stats(obj, top=6):
    """Compare edge lengths of the evaluated (posed) mesh with the rest mesh."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    me = ev.to_mesh()
    rest = obj.data
    ratios = []
    for e in rest.edges:
        a, b = e.vertices
        l0 = (rest.vertices[a].co - rest.vertices[b].co).length
        if l0 < 1e-5:
            continue
        l1 = (me.vertices[a].co - me.vertices[b].co).length
        ratios.append((l1 / l0, e.index, (rest.vertices[a].co + rest.vertices[b].co) * 0.5))
    ev.to_mesh_clear()
    ratios.sort(key=lambda r: r[0])
    return ratios[:top], ratios[-top:][::-1]


def weight_stress_test(rig, obj, deg=40.0, skip=("Root",), min_edge=0.012):
    """Rotate every bone alone by +/-deg about its local X and Z axes; report the worst edge
    stretch (max length ratio) and squash (min ratio) of the skinned mesh for each bone.
    Only edges >= min_edge are measured (mm-sized hem/lip edges would dominate the ratio)."""
    from mathutils import Euler
    rest = obj.data
    edges = [(e.vertices[0], e.vertices[1]) for e in rest.edges]
    l0 = [(rest.vertices[a].co - rest.vertices[b].co).length for a, b in edges]
    keep = [i for i, L in enumerate(l0) if L >= min_edge]
    edges = [edges[i] for i in keep]
    l0 = [l0[i] for i in keep]
    report = {}
    for pb in rig.pose.bones:
        if pb.name in skip:
            continue
        worst_hi, worst_lo = 1.0, 1.0
        for ax in ((1, 0, 0), (0, 0, 1), (-1, 0, 0), (0, 0, -1)):
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = Euler([math.radians(deg) * c for c in ax], 'XYZ')
            bpy.context.view_layer.update()
            dg = bpy.context.evaluated_depsgraph_get()
            ev = obj.evaluated_get(dg)
            me = ev.to_mesh()
            vs = me.vertices
            for (a, b), L in zip(edges, l0):
                if L < 1e-5:
                    continue
                r = (vs[a].co - vs[b].co).length / L
                if r > worst_hi:
                    worst_hi = r
                if r < worst_lo:
                    worst_lo = r
            ev.to_mesh_clear()
            pb.rotation_euler = (0, 0, 0)
        report[pb.name] = (worst_hi, worst_lo)
    bpy.context.view_layer.update()
    return report


# --------------------------------------------------------------------------------------
# preview rendering (EEVEE Next; grey backdrop; 3-point sun lighting that follows the camera)
# --------------------------------------------------------------------------------------

def preview_dir_from_argv(default_name="splatoon_art_preview"):
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = None
    render = True
    for i, a in enumerate(argv):
        if a == '--preview-dir' and i + 1 < len(argv):
            out = argv[i + 1]
        if a == '--no-render':
            render = False
    if out is None:
        out = os.path.join(tempfile.gettempdir(), default_name)
    os.makedirs(out, exist_ok=True)
    return out, render


class Stage:
    def __init__(self, coll_name="_Preview"):
        sc = bpy.context.scene
        self.scene = sc
        self.coll = get_coll(coll_name)
        sc.render.engine = 'BLENDER_EEVEE_NEXT'
        sc.render.resolution_x = 1024
        sc.render.resolution_y = 1024
        sc.render.resolution_percentage = 100
        sc.render.film_transparent = False
        sc.render.image_settings.file_format = 'PNG'
        sc.view_settings.view_transform = 'Standard'
        sc.view_settings.look = 'None'
        sc.eevee.taa_render_samples = 48
        try:
            sc.eevee.use_gtao = True
        except Exception:
            pass
        world = bpy.data.worlds.get("PreviewWorld") or bpy.data.worlds.new("PreviewWorld")
        world.use_nodes = True
        bg = world.node_tree.nodes["Background"]
        g = srgb_to_lin(0.50)
        bg.inputs[0].default_value = (g, g, g, 1.0)
        bg.inputs[1].default_value = 0.55
        sc.world = world
        # floor
        me = bpy.data.meshes.new("PreviewFloor")
        s = 30.0
        me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
        fl = bpy.data.objects.new("PreviewFloor", me)
        self.coll.objects.link(fl)
        fm = bpy.data.materials.new("PreviewFloorMat")
        fm.use_nodes = True
        fb = fm.node_tree.nodes["Principled BSDF"]
        fg = srgb_to_lin(0.55)
        fb.inputs["Base Color"].default_value = (fg, fg, fg, 1)
        fb.inputs["Roughness"].default_value = 0.9
        me.materials.append(fm)
        # camera
        cd = bpy.data.cameras.new("PreviewCam")
        cd.lens = 85
        cd.clip_start = 0.05
        cd.clip_end = 100
        self.cam = bpy.data.objects.new("PreviewCam", cd)
        self.coll.objects.link(self.cam)
        sc.camera = self.cam
        # lights (suns): key / fill / rim
        self.lights = {}
        for nm, en, ang in (("Key", 2.6, 6.0), ("Fill", 0.9, 12.0), ("Rim", 2.4, 4.0)):
            ld = bpy.data.lights.new("Preview" + nm, 'SUN')
            ld.energy = en
            ld.angle = math.radians(ang)
            lo = bpy.data.objects.new("Preview" + nm, ld)
            self.coll.objects.link(lo)
            self.lights[nm] = lo

    @staticmethod
    def _dir(az, el):
        a, e = math.radians(az), math.radians(el)
        return Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e)))

    def view(self, target, height, az, el, lens=85.0, margin=1.18):
        """az: 0 = camera in front of the character (at -Y), 90 = at +X (character's left)."""
        self.cam.data.lens = lens
        fov = 2 * math.atan(18.0 / lens)
        dist = (height * 0.5 * margin) / math.tan(fov / 2)
        d = self._dir(az, el)
        self.cam.location = Vector(target) + d * dist
        self.cam.rotation_euler = (-d).to_track_quat('-Z', 'Y').to_euler()
        for nm, daz, el2 in (("Key", -40, 38), ("Fill", 55, 12), ("Rim", 165, 30)):
            ld = self._dir(az + daz, el2)
            self.lights[nm].rotation_euler = (-ld).to_track_quat('-Z', 'Y').to_euler()

    def render(self, path):
        self.scene.render.filepath = path
        bpy.ops.render.render(write_still=True)
        print("RENDERED", path)


# --------------------------------------------------------------------------------------
# export
# --------------------------------------------------------------------------------------

def save_blend(path):
    """Save the .blend source without leaving a .blend1 backup next to it."""
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=path, check_existing=False)
    print("SAVED", path)


def export_fbx(path, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'ARMATURE', 'MESH', 'EMPTY'},
        global_scale=1.0,
        apply_unit_scale=True,
        apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=False,
        use_mesh_modifiers=True,
        mesh_smooth_type='FACE',
        use_tspace=False,
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        use_armature_deform_only=True,
        armature_nodetype='NULL',
        bake_anim=False,
        embed_textures=False,
        path_mode='AUTO',
    )
    print("EXPORTED", path)
