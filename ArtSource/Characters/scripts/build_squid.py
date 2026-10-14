# -*- coding: utf-8 -*-
"""build_squid.py - procedural squid (swim) form for the Splatoon_C Unity project.

Run (headless, reproducible):
  "C:/Program Files/Blender Foundation/Blender 4.3/blender.exe" -b --factory-startup \
      -P ArtSource/Characters/scripts/build_squid.py -- [--preview-dir <dir>] [--no-render]

Orientation: swims along Blender -Y (mantle tip at -Y), tentacles trail towards +Y,
belly on the ground (z = 0), origin at the bottom centre of the bounding box.

Shape: one teardrop "core" (round body at the back that tapers into the pointed mantle),
two flat fins on the sides of the mantle (plan view = arrowhead), big eyes + mask band on the
top of the body (the face looks up when lying flat), 5 stubby tentacles at the rear.
"""
import os
import sys
import math

sys.dont_write_bytecode = True      # keep scripts/ free of __pycache__
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import importlib
import charlib
importlib.reload(charlib)
from charlib import *  # noqa: F401,F403

import bpy
from mathutils import Vector, Matrix

V = Vector
SCRIPTS = os.path.dirname(os.path.abspath(__file__))
ART = os.path.dirname(SCRIPTS)
PROJECT = os.path.dirname(os.path.dirname(ART))
BLEND_OUT = os.path.join(ART, "Squid.blend")
FBX_OUT = os.path.join(PROJECT, "Assets", "Art", "Characters", "Squid.fbx")
PREVIEW_DIR, DO_RENDER = preview_dir_from_argv()
TARGET_LEN = 0.85
PREVIEW_TEAM = "#F07A1A"

MATS = [("Body", "#FFFFFF", 0.22), ("Mask", "#1A1A1E", 0.4), ("EyeWhite", "#FFFFFF", 0.25), ("Pupil", "#1B1B22", 0.2)]
MESH_MATS = ["Body", "Mask", "EyeWhite", "Pupil"]
N_TENT = 5

# local frame of the core: local -Y ("face") = world +Z, local +Z ("up" = towards the mantle tip) = world -Y
CORE_FRAME = Matrix(((-1, 0, 0), (0, 0, -1), (0, -1, 0))).transposed()
CORE_C = V((0.0, 0.020, 0.088))
# rev2: taller body (RY .10->.12) and a shorter, fuller mantle (EXT .24->.17) -> no more flat "bird beak"
CORE_RX, CORE_RY, CORE_RZ, CORE_EXT = 0.150, 0.120, 0.165, 0.170
FIN_TIP_X = 0.250
MANTLE_PIVOT = V((0.0, -0.070, 0.088))
MANTLE_DROP = 0.0          # rev2: mantle no longer slopes down to its tip


def mantle_drop_at(y):
    """World-space downward shift of the mantle axis at world y (inverse of TearDrop.local z)."""
    d = CORE_C.y - y
    if d <= 0:
        return 0.0
    lo, hi = 0.0, 1.0
    for _ in range(40):
        m = 0.5 * (lo + hi)
        if CORE_RZ * m + CORE_EXT * m ** 2.2 < d:
            lo = m
        else:
            hi = m
    return MANTLE_DROP * lo * lo


class TearDrop:
    """Round at the back (lam < 0), stretched into a point towards lam = +90 (the mantle tip)."""

    def local(self, phi, lam):
        p, l = math.radians(phi), math.radians(lam)
        flat, drop = 1.0, 0.0
        if l <= 0.0:
            R = math.cos(l)
            z = CORE_RZ * math.sin(l)
        else:
            s = math.sin(l)
            R = max(0.0, math.cos(l)) ** (1.0 + 0.35 * s)     # rev2: fuller mantle (was 1+.6s)
            z = CORE_RZ * s + CORE_EXT * s ** 2.2
            flat = 1.0 - 0.30 * s ** 1.3          # the mantle is flatter than it is wide (rev2: .45->.30) ...
            drop = MANTLE_DROP * s ** 2            # ... and slopes down towards its tip (hood, not beak)
        return V((CORE_RX * math.sin(p) * R, -CORE_RY * math.cos(p) * R * flat + drop, z))

    def raw(self, phi, lam):
        return CORE_C + CORE_FRAME @ self.local(phi, lam)

    def normal(self, phi, lam):
        h = 0.25
        a = self.raw(phi + h, lam) - self.raw(phi - h, lam)
        b = self.raw(phi, lam + h) - self.raw(phi, lam - h)
        n = a.cross(b).normalized()
        axis_pt = CORE_C + CORE_FRAME @ V((0, 0, self.local(phi, lam).z))
        if n.dot(self.raw(phi, lam) - axis_pt) < 0:
            n = -n
        return n

    def pt(self, phi, lam, off=0.0):
        return self.raw(phi, lam) + self.normal(phi, lam) * off


CORE = TearDrop()


def flatten_belly(p):
    q = p.copy()
    if q.z < CORE_C.z:
        q.z = CORE_C.z + (q.z - CORE_C.z) * 0.90     # rev2: rounder belly (.82->.90)
    return q


def mantle_w(p):
    return wblend("Root", "Mantle", smoothstep(MANTLE_PIVOT.y + 0.07, MANTLE_PIVOT.y - 0.09, p.y))


def build_core(mb):
    n_phi, n_lam = 16, 14
    rings = []
    for i in range(1, n_lam):
        lam = -90.0 + 180.0 * i / n_lam
        rings.append([flatten_belly(CORE.raw(-180.0 + 360.0 * j / n_phi, lam)) for j in range(n_phi)])
    rear = flatten_belly(CORE.raw(0, -90))
    tip = flatten_belly(CORE.raw(0, 90))
    loft(mb, rings, "Body", mantle_w, cap0=rear, cap1=tip)


def mantle_y_at_halfwidth(xw):
    """World y (on the mantle side, phi = 90) where the mantle's half-width equals xw."""
    lo, hi = 0.0, 90.0
    for _ in range(50):
        m = 0.5 * (lo + hi)
        if abs(CORE.raw(90.0, m).x) > xw:
            lo = m
        else:
            hi = m
    return CORE.raw(90.0, lo).y


def build_fins(mb):
    """Flat fins along the sides of the mantle; their leading edges continue the line from the
    mantle tip, so the plan view reads as an arrowhead."""
    n_u = 6
    us = [(1 - math.cos(math.pi * k / n_u)) * 0.5 for k in range(n_u + 1)]
    # rev2: the fin root starts ON the mantle surface (x = .035), so the fin leading edge grows out
    # of the mantle instead of floating beside the tip (the old "forked tip" in side view)
    xs = [0.035, 0.065, 0.100, 0.140, 0.178, 0.210, 0.235, FIN_TIP_X]
    y_root = mantle_y_at_halfwidth(xs[0] + 0.004)
    for sd in (1, -1):
        rings = []
        for x in xs:
            a = (x - xs[0]) / (FIN_TIP_X - xs[0])
            y_le = y_root + (-0.080 - y_root) * a ** 0.97
            y_te = sinterp([(0.0, 0.010), (0.35, -0.030), (0.70, -0.052), (0.92, -0.058), (1.0, -0.062)], a)
            if x >= FIN_TIP_X - 1e-6:
                mid = 0.5 * (y_le + y_te)
                y_le, y_te = mid - 0.006, mid + 0.006
            t = 0.040 * (1 - a) ** 1.2 + 0.012          # rev2: thicker, no paper-thin fins
            zc = CORE_C.z - 0.004 + 0.012 * a * a        # rev2: slight dihedral (tips up) instead of droop
            chord = y_te - y_le

            def h(u):
                return t * (u ** 0.5 * (1 - u) ** 0.5) / 0.5
            upper = [V((sd * x, y_le + chord * u, zc - mantle_drop_at(y_le + chord * u) + h(u))) for u in us]
            lower = [V((sd * x, y_le + chord * u, zc - mantle_drop_at(y_le + chord * u) - 0.8 * h(u)))
                     for u in us[::-1][1:-1]]
            rings.append(upper + lower)
        loft(mb, rings, "Body", mantle_w, cap0='fan', cap1='fan')


SQ_MASK_TOP = [(0, 22), (16, 29), (32, 34), (56, 31), (80, 22), (88, 14)]
SQ_MASK_BOT = [(0, 2), (16, -12), (32, -19), (56, -16), (80, -9), (88, -2)]
EYE_PHI, EYE_LAM = 33.0, 6.0


def build_mask(mb):
    PHI = 88.0
    ncol, m = 16, 3
    rings = []
    for j in range(ncol + 1):
        ph = -PHI + 2 * PHI * j / ncol
        top = sinterp(SQ_MASK_TOP, abs(ph))
        bot = sinterp(SQ_MASK_BOT, abs(ph))
        outer = [CORE.pt(ph, bot + (top - bot) * i / m, 0.005) for i in range(m + 1)]
        inner = [CORE.pt(ph, top, -0.005), CORE.pt(ph, bot, -0.005)]
        rings.append(outer + [CORE.pt(ph, top + 1.0, 0.0015)] + inner + [CORE.pt(ph, bot - 1.0, 0.0015)])
    loft(mb, rings, "Mask", {"Root": 1.0}, cap0='fan', cap1='fan')


def build_eyes(mb):
    for sd in (1, -1):
        ph, lam = EYE_PHI * sd, EYE_LAM
        n = CORE.normal(ph, lam)
        rot = frame_zy(n, V((0, -1, 0)))
        X, Y, Z = rot.col[0], rot.col[1], rot.col[2]
        ec = CORE.pt(ph, lam, 0.0)
        ellipsoid(mb, ec, (0.050, 0.058, 0.019), 14, 4, "EyeWhite", {"Root": 1.0}, rot=rot, hemi=True)
        inward = V((-math.copysign(1, ec.x), 0, 0))            # towards the other eye
        inward = (inward - Z * inward.dot(Z)).normalized()
        pc = ec + inward * 0.011 + Y * 0.004 + Z * (0.0180 - 0.0050)
        ellipsoid(mb, pc, (0.024, 0.034, 0.0075), 10, 3, "Pupil", {"Root": 1.0}, rot=rot, hemi=True)
        hc = pc - inward * 0.008 + Y * 0.014 + Z * 0.0050
        ellipsoid(mb, hc, (0.0070, 0.0092, 0.003), 6, 2, "EyeWhite", {"Root": 1.0}, rot=rot, hemi=True)


def inside_core(p):
    """True if p is inside the (unflattened) round back half of the core."""
    q = CORE_FRAME.inverted() @ (p - CORE_C)
    return (q.x / CORE_RX) ** 2 + (q.y / CORE_RY) ** 2 + (min(q.z, 0.0) / CORE_RZ) ** 2 < 1.0


def tentacle_defs():
    defs = []
    # rev2: narrower fan, staggered lengths and tapered, inward-curling tips -> reads as a squid's
    # tentacle bundle instead of five equal fingers of a rubber glove
    angs = [-26.0, -13.0, 0.0, 13.0, 26.0][:N_TENT]
    lens = {0: 0.300, 13: 0.255, 26: 0.205}

    def hdir(deg):
        r = math.radians(deg)
        return V((math.sin(r), math.cos(r), 0.0))
    for a in angs:
        ra = math.radians(a)
        curl = -10.0 * a / 26.0                  # outer tips turn ~10 deg back towards the centre
        root = V((0.040 * math.sin(ra), 0.080, 0.070))
        L = lens[int(round(abs(a)))]
        p1 = root + hdir(a) * (L * 0.40) + V((0, 0, -0.020))
        p2 = p1 + hdir(a + 0.5 * curl) * (L * 0.35) + V((0, 0, -0.010))
        p3 = p2 + hdir(a + curl) * (L * 0.25) + V((0, 0, 0.014))        # tips curl up a little
        defs.append([root, p1, p2, p3])
    return defs


TENT_CAP = 0.026


def build_tentacles(mb, bones_out):
    for k, pts in enumerate(tentacle_defs(), start=1):
        path = Path(pts)
        bn = "Tentacle_%d" % k
        s_exit = path.total * 0.3
        for i in range(200):
            s = path.total * i / 199
            p, _ = path.at(s)
            if not inside_core(p):
                s_exit = s
                break
        n_r = 5
        prof_w = [(0, 0.042), (0.5, 0.031), (1, 0.020)]       # rev2: strong taper to bullet tips,
        prof_t = [(0, 0.035), (0.5, 0.025), (1, 0.016)]       # a bit flatter than wide
        specs = [(path.total * i / n_r, interp(prof_w, i / n_r), interp(prof_t, i / n_r), "Body") for i in range(n_r + 1)]
        wf = lambda s, s_exit=s_exit, bn=bn: chain_weights(s, [s_exit - 0.01], ["Root", bn], [0.035])
        tube(mb, path, specs, 10, wf, side_fn=lambda p, T, s: V((0, 0, 1)).cross(T), cap0='flat', cap1='dome',
             cap_len1=TENT_CAP, dome_k=3)
        p_piv, _ = path.at(max(0.0, s_exit - 0.01))
        p_end, T_end = path.at(path.total)
        bones_out.append((bn, p_piv, p_end + T_end * TENT_CAP, "Root", False, (0, 0, 1)))


def main():
    clear_scene()
    for n, h, r in MATS:
        make_material(n, h, rough=r)
    coll = get_coll("Squid")
    mb = MB()
    tb = []
    build_core(mb)
    build_fins(mb)
    build_mask(mb)
    build_eyes(mb)
    build_tentacles(mb, tb)

    # ---- normalise: length along Y = TARGET_LEN, origin at bottom centre of the bounding box
    ys = [v.y for v in mb.V]
    xs = [v.x for v in mb.V]
    zs = [v.z for v in mb.V]
    k = TARGET_LEN / (max(ys) - min(ys))
    off = V(((max(xs) + min(xs)) * 0.5, (max(ys) + min(ys)) * 0.5, min(zs)))
    print("design length %.4f -> scale %.4f, offset %s" % (max(ys) - min(ys), k, tuple(round(c, 4) for c in off)))

    def T(p):
        return (V(p) - off) * k
    mb.V = [T(v) for v in mb.V]

    tip = flatten_belly(CORE.raw(0, 90))
    bones = [("Root", V((0, 0, 0)), V((0, 0, 0.10)), None, False, (0, -1, 0)),
             ("Mantle", T(MANTLE_PIVOT), T(V((0, tip.y + 0.02, MANTLE_PIVOT.z))), "Root", False, (0, 0, 1))]
    for bn, h, t, par, con, rv in tb:
        bones.append((bn, T(h), T(t), par, con, rv))
    rig = create_armature("Rig", bones, coll)
    body = build_object(mb, "Body", MESH_MATS, coll, sharp_deg=70.0)   # rev2: fewer hard creases
    skin_to(body, rig)

    ys = [v.co.y for v in body.data.vertices]
    xs = [v.co.x for v in body.data.vertices]
    zs = [v.co.z for v in body.data.vertices]
    print("TRIS", tri_count(body), "LEN %.4f WIDTH %.4f HEIGHT %.4f  ymin %.4f ymax %.4f zmin %.4f" % (
        max(ys) - min(ys), max(xs) - min(xs), max(zs) - min(zs), min(ys), max(ys), min(zs)))
    rep_ = weight_stress_test(rig, body, 40.0)
    print("WEIGHT40 per bone:", ", ".join("%s %.2f/%.2f" % (n_, a_, b_) for n_, (a_, b_) in rep_.items()))
    wsum = [sum(g.weight for g in v.groups) for v in body.data.vertices]
    print("WEIGHTS unweighted=%d max influences=%d" % (sum(1 for w in wsum if w < 0.999),
                                                       max(len(v.groups) for v in body.data.vertices)))

    if DO_RENDER:
        st = Stage()
        set_mat_color("Body", PREVIEW_TEAM)
        tgt = V((0, 0, 0.09))
        # 3/4 from behind and above = roughly what the gameplay camera sees while swimming
        st.view(tgt, 0.95, -145, 40)
        st.render(os.path.join(PREVIEW_DIR, "squid_34.png"))
        st.view(tgt, 0.95, 90, 4)
        st.render(os.path.join(PREVIEW_DIR, "squid_side.png"))
        # extra checks (not deliverables): front 3/4, top view, a wiggle pose
        st.view(tgt, 0.95, -40, 45)
        st.render(os.path.join(PREVIEW_DIR, "squid_front34_check.png"))
        st.view(tgt, 0.95, 180, 80)
        st.render(os.path.join(PREVIEW_DIR, "squid_top_check.png"))
        for pb in rig.pose.bones:
            if pb.name.startswith("Tentacle") or pb.name == "Mantle":
                pb.rotation_mode = 'XYZ'
                pb.rotation_euler = (0, 0, math.radians(30 if pb.name != "Mantle" else 12))
        bpy.context.view_layer.update()
        st.view(tgt, 0.95, 150, 35)
        st.render(os.path.join(PREVIEW_DIR, "squid_wiggle_check.png"))
        reset_pose(rig)

    reset_pose(rig)
    restore_export_colors()
    bpy.context.view_layer.update()
    export_fbx(FBX_OUT, [rig, body])
    save_blend(BLEND_OUT)


main()
