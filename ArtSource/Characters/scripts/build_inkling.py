# -*- coding: utf-8 -*-
"""build_inkling.py - procedural Inkling (humanoid form) for the Splatoon_C Unity project.

Run (headless, reproducible):
  "C:/Program Files/Blender Foundation/Blender 4.3/blender.exe" -b --factory-startup \
      -P ArtSource/Characters/scripts/build_inkling.py -- [--preview-dir <dir>] [--no-render]

Outputs:
  Assets/Art/Characters/Inkling.fbx      (Unity asset)
  ArtSource/Characters/Inkling.blend     (source)
  <preview-dir>/inkling_front.png, inkling_side.png, inkling_back34.png, inkling_posed.png
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
ART = os.path.dirname(SCRIPTS)                       # ArtSource/Characters
PROJECT = os.path.dirname(os.path.dirname(ART))       # repo root
BLEND_OUT = os.path.join(ART, "Inkling.blend")
FBX_OUT = os.path.join(PROJECT, "Assets", "Art", "Characters", "Inkling.fbx")
PREVIEW_DIR, DO_RENDER = preview_dir_from_argv()
TARGET_H = 1.55
PREVIEW_TEAM = "#F07A1A"

# ---------------------------------------------------------------------------------------
# materials (names are used by game code)
# ---------------------------------------------------------------------------------------
MATS = [
    ("Skin", "#F3C9A0", 0.55), ("Tentacle", "#F0F0F0", 0.22), ("Mask", "#1A1A1E", 0.4),
    ("EyeWhite", "#FFFFFF", 0.25), ("Pupil", "#1B1B22", 0.2), ("Shirt", "#EDEDED", 0.75),
    ("Shorts", "#2B2D38", 0.7), ("Shoes", "#E8E8E8", 0.5), ("ShoeSole", "#3A3A44", 0.6),
    ("TankGlass", "#CFE8F5", 0.05), ("TankInk", "#FFFFFF", 0.2),
]
BODY_MATS = ["Skin", "Tentacle", "Mask", "EyeWhite", "Pupil", "Shirt", "Shorts", "Shoes", "ShoeSole"]

FWD = (0, -1, 0)
UP = (0, 0, 1)

# ---------------------------------------------------------------------------------------
# design dimensions (design units ~ metres; everything is uniformly rescaled so the top of
# the hair lands exactly at TARGET_H)
# ---------------------------------------------------------------------------------------
# rev2: head a little bigger and lower (shorter neck ~4 cm); head ~1/4.2 of height, a compromise
# between the brief (~1/4.5) and the art-director request (~1/3.9)
HEAD = Ellip((0.0, 0.0, 1.320), (0.156, 0.151, 0.170))
NECK_Z, HEADJ_Z = 1.125, 1.180        # Chest->Neck and Neck->Head joint heights
ARM_DROP = math.radians(35.0)          # A-pose: arms 35 deg below horizontal
UA_LEN, LA_LEN, HAND_LEN = 0.20, 0.18, 0.085
HIP_Z, KNEE_Z, ANKLE_Z = 0.66, 0.37, 0.09
LEG_X = 0.080


def arm_geo(sd):
    d = V((sd * math.cos(ARM_DROP), 0.0, -math.sin(ARM_DROP)))
    S = V((sd * 0.140, 0.0, 1.050))
    E = S + d * UA_LEN
    Wr = E + d * LA_LEN
    return d, S, E, Wr, Wr + d * HAND_LEN


def leg_geo(sd):
    x = sd * LEG_X
    return V((x, 0.0, HIP_Z)), V((x, -0.012, KNEE_Z)), V((x, 0.004, ANKLE_Z)), V((x, -0.100, 0.030))


def sname(base, sd):
    return base + (".L" if sd > 0 else ".R")


# torso lofting table: z, half-width, half-depth, y offset, band material (to next ring)
TORSO = [
    (0.648, 0.070, 0.050, 0.006, "Shorts"),    # rev2: crotch tucked up between the leg tubes
    (0.656, 0.112, 0.074, 0.006, "Shorts"),    # (no more "diaper" U-seam from the front)
    (0.668, 0.134, 0.086, 0.006, "Shorts"),
    (0.712, 0.138, 0.088, 0.006, "Shorts"),
    (0.752, 0.132, 0.086, 0.004, "Shorts"),
    (0.786, 0.124, 0.082, 0.002, "Shirt"),     # shorts top -> hem underside
    (0.776, 0.135, 0.091, 0.002, "Shirt"),     # shirt hem overhang
    (0.800, 0.134, 0.090, 0.002, "Shirt"),
    (0.830, 0.129, 0.087, 0.001, "Shirt"),
    (0.860, 0.124, 0.084, 0.000, "Shirt"),     # waist
    (0.895, 0.126, 0.086, -0.002, "Shirt"),
    (0.930, 0.130, 0.088, -0.003, "Shirt"),
    (0.962, 0.135, 0.089, -0.004, "Shirt"),
    (0.992, 0.139, 0.090, -0.004, "Shirt"),    # chest
    (1.045, 0.142, 0.086, -0.002, "Shirt"),    # shoulder line
    (1.082, 0.120, 0.075, 0.000, "Shirt"),
    (1.108, 0.078, 0.060, 0.004, "Shirt"),     # collar
    (1.114, 0.057, 0.054, 0.006, "Skin"),      # collar lip
    (1.110, 0.048, 0.047, 0.008, "Skin"),      # neck base
    (1.165, 0.044, 0.042, 0.008, "Skin"),
    (1.232, 0.042, 0.040, 0.008, "Skin"),      # inside head
]
TORSO_E = 2.4
TORSO_SEG = 18
TORSO_MONO = [r for r in TORSO[7:] if r[0] <= 1.1085]   # monotonic-z part used for surface queries


def torso_at(z):
    tab = [(r[0], (r[1], r[2], r[3])) for r in TORSO_MONO]
    return interp(tab, z)


def torso_w(p):
    z = p.z
    # rings that are only millimetres apart (shirt hem, collar) must share identical weights,
    # otherwise the short edges between them shear when the spine bends
    zw = 0.781 if 0.770 <= z <= 0.790 else (1.109 if 1.100 <= z <= 1.118 else z)
    w = chain_weights(zw, [0.790, 0.915, NECK_Z + 0.005, HEADJ_Z], ["Hips", "Spine", "Chest", "Neck", "Head"],
                      [0.065, 0.065, 0.025, 0.02])
    for sd in (1, -1):
        d, S, E, Wr, Ht = arm_geo(sd)
        if p.x * sd > 0.06:
            k = 0.45 * (1.0 - smoothstep(0.035, 0.115, (p - S).length))
            if k > 0:
                w = wadd((w, 1 - k), ({sname("UpperArm", sd): 1.0}, k))
        if p.x * sd > 0.015 and z < 0.71:
            k = 0.5 * smoothstep(0.71, 0.635, z) * smoothstep(0.015, 0.075, p.x * sd)
            if k > 0:
                w = wadd((w, 1 - k), ({sname("UpperLeg", sd): 1.0}, k))
    return w


# ---------------------------------------------------------------------------------------
# part builders
# ---------------------------------------------------------------------------------------

def build_torso(mb):
    rings, mats = [], []
    for z, a, b, yo, m in TORSO:
        rings.append(se_ring(V((0, yo, z)), V((1, 0, 0)), V((0, 1, 0)), a, b, TORSO_SEG, TORSO_E))
        mats.append(m)
    loft(mb, rings, mats[:-1], torso_w, cap0='fan', cap1='fan', capmat0="Shorts", capmat1="Skin")


def build_arm(mb, sd):
    d, S, E, Wr, Ht = arm_geo(sd)
    path = Path([S - d * 0.035, S, E, Wr, Wr + d * 0.012])
    c = path.ctrl_s
    sS, sE, sW = c[1], c[2], c[3]
    specs = [
        (c[0], 0.038, 0.038, "Shirt"),
        (sS - 0.010, 0.048, 0.048, "Shirt"),
        (sS + 0.030, 0.051, 0.051, "Shirt"),
        (sS + 0.080, 0.053, 0.053, "Shirt"),     # sleeve hem
        (sS + 0.086, 0.048, 0.048, "Shirt"),
        (sS + 0.087, 0.038, 0.038, "Skin"),
        (sE - 0.045, 0.036, 0.036, "Skin"),
        (sE - 0.015, 0.034, 0.034, "Skin"),
        (sE + 0.015, 0.034, 0.034, "Skin"),
        (sE + 0.050, 0.035, 0.035, "Skin"),
        (sW - 0.045, 0.030, 0.030, "Skin"),
        (sW, 0.027, 0.027, "Skin"),
        (c[4], 0.026, 0.026, "Skin"),
    ]
    bones = ["Chest", sname("UpperArm", sd), sname("LowerArm", sd), sname("Hand", sd)]
    wf = lambda s: chain_weights(s, [sS + 0.004, sE, sW - 0.008], bones, [0.035, 0.04, 0.03])
    tube(mb, path, specs, 10, wf, side_fn=lambda p, T, s: V(FWD), cap0='dome', cap1='flat', dome_k=2)


def hand_frame(sd):
    d, S, E, Wr, Ht = arm_geo(sd)
    f = V(FWD)
    n = V((-sd * math.sin(ARM_DROP), 0.0, -math.cos(ARM_DROP)))   # palm normal (towards body/down)
    return d, f, n, Wr


def build_hand(mb, sd):
    d, f, n, Wr = hand_frame(sd)
    hn = sname("Hand", sd)
    rot = Matrix((d, f, n)).transposed()
    ctr = Wr + d * 0.046 + n * 0.003
    ellipsoid(mb, ctr, (0.050, 0.040, 0.032), 10, 7, "Skin", {hn: 1.0}, rot=rot)
    # thumb
    base = ctr + f * 0.024 + n * 0.011 - d * 0.014
    tdir = (d * 0.55 + f * 0.55 + n * 0.35).normalized()
    path = Path([base, base + tdir * 0.018, base + tdir * 0.035])
    specs = [(0.0, 0.0135, 0.0135, "Skin"), (path.total, 0.013, 0.013, "Skin")]
    tube(mb, path, specs, 6, lambda s: {hn: 1.0}, cap0='flat', cap1='dome', dome_k=2)


def build_leg(mb, sd):
    H, K, A, Toe = leg_geo(sd)
    path = Path([H + V((0, 0, 0.05)), H, K, A, A + V((0, 0, -0.03))])
    c = path.ctrl_s
    sH, sK, sA = c[1], c[2], c[3]
    specs = [
        (c[0], 0.062, 0.062, "Shorts"),
        (sH, 0.074, 0.074, "Shorts"),            # rev2: fuller hip (was .068)
        (sH + 0.055, 0.076, 0.076, "Shorts"),
        (sH + 0.105, 0.077, 0.077, "Shorts"),    # shorts hem
        (sH + 0.112, 0.072, 0.072, "Shorts"),
        (sH + 0.113, 0.056, 0.056, "Skin"),
        (sH + 0.165, 0.054, 0.054, "Skin"),
        (sK - 0.060, 0.049, 0.049, "Skin"),
        (sK - 0.022, 0.046, 0.046, "Skin"),
        (sK + 0.022, 0.047, 0.047, "Skin"),
        (sK + 0.075, 0.050, 0.050, "Skin"),
        (sK + 0.165, 0.042, 0.042, "Skin"),
        (sA - 0.030, 0.036, 0.036, "Skin"),
        (sA, 0.035, 0.035, "Skin"),
        (c[4], 0.034, 0.034, "Skin"),
    ]
    bones = ["Hips", sname("UpperLeg", sd), sname("LowerLeg", sd), sname("Foot", sd)]

    def leg_w(p, s):
        # rev2: the shorts part of the leg tube uses the SAME position-based weights as the torso
        # shell it overlaps (torso_w), fading into the leg chain by the hem, so both shorts layers
        # move together when the thigh is raised (no layered step at the hip)
        ch = chain_weights(s, [sH + 0.02, sK, sA], bones, [0.05, 0.04, 0.02])
        b = smoothstep(sH - 0.015, sH + 0.100, s)
        if b >= 1.0:
            return ch
        return wadd((torso_w(p), 1.0 - b), (ch, b))
    wf = lambda s: (lambda p, s=s: leg_w(p, s))
    tube(mb, path, specs, 12, wf, side_fn=lambda p, T, s: V(FWD), cap0='flat', cap1='flat')


def build_shoe(mb, sd):
    x = sd * LEG_X
    fn = sname("Foot", sd)
    yc, L, Wd = -0.040, 0.120, 0.055

    def foot_ring(z, inset, n=16):
        pts = []
        for j in range(n):
            t = 2 * math.pi * j / n
            cu, sv = math.cos(t), math.sin(t)
            e = 2.6
            u = math.copysign(abs(cu) ** (2 / e), cu)
            v = math.copysign(abs(sv) ** (2 / e), sv)
            yy = (L - inset) * v
            wide = 1.0 + 0.10 * max(0.0, -yy / L)
            pts.append(V((x + (Wd - inset) * u * wide, yc + yy, z)))
        return pts
    rings = [foot_ring(0.000, 0.006), foot_ring(0.005, 0.0), foot_ring(0.021, 0.0), foot_ring(0.027, 0.006)]
    loft(mb, rings, "ShoeSole", {fn: 1.0}, cap0='fan', cap1='fan')

    cz, ry = 0.030, 0.113

    def dfm(p):
        q = p.copy()
        ly = (p.y - (yc - 0.002)) / ry          # -1 toe .. +1 heel
        h = q.z - 0.022
        if h > 0:
            q.z = 0.022 + h * (1.0 - 0.45 * smoothstep(0.25, -0.95, ly))
        else:
            q.z = 0.022 + h * 0.15
        return q
    ellipsoid(mb, V((x, yc - 0.002, cz)), (0.051, ry, 0.080), 14, 9, "Shoes", {fn: 1.0}, deform=dfm)
    # ankle collar ring (dark trim)
    cc = V((x, 0.004, 0.103))
    rings = []
    for k in range(5):
        a = 2 * math.pi * k / 5
        rr = 0.041 + 0.008 * math.cos(a)
        rings.append(se_ring(cc + V((0, 0, 0.008 * math.sin(a))), V((1, 0, 0)), V((0, 1, 0)), rr, rr * 0.95, 12))
    torus_loft(mb, rings, "ShoeSole", {fn: 0.6, sname("LowerLeg", sd): 0.4})


def build_head(mb):
    C = HEAD.C

    def dfm(p):
        q = p.copy()
        t = smoothstep(C.z - 0.045, C.z - 0.165, p.z)
        q.x = p.x * (1.0 - 0.20 * t)
        if p.y > 0:
            q.y = p.y * (1.0 - 0.18 * t)
        return q
    ellipsoid(mb, C, HEAD.r, 24, 16, "Skin", {"Head": 1.0}, deform=dfm)


def hairline(phi):
    # rev2: lower at the front (15, was 22) so the cap hugs the forehead instead of a helmet brim;
    # lifted around the temples (phi 75-105) so the mask's swept-back points stay visible
    return sinterp([(0, 15), (25, 24), (50, 27), (70, 27), (80, 24.5), (88, 20.5), (96, 16), (106, 7),
                    (118, -4), (140, -20), (165, -31), (180, -33)], abs(phi))


def hair_off(phi, lam):
    back = (1.0 - math.cos(math.radians(phi))) * 0.5
    return 0.010 + 0.013 * back * math.sqrt(max(0.0, math.cos(math.radians(lam)))) + \
        0.008 * max(0.0, math.sin(math.radians(lam)))


def build_hair_cap(mb):
    n_phi = 24
    phis = [-180.0 + 360.0 * j / n_phi for j in range(n_phi)]
    n_out = 6
    LAM_TOP = 84.0

    def row(i, n, off_fn):
        pts = []
        for ph in phis:
            b = hairline(ph)
            lam = b + (LAM_TOP - b) * (i / n) ** 0.9
            pts.append(HEAD.pt(ph, lam, off_fn(ph, lam)))
        return pts
    inner = [[HEAD.pt(ph, hairline(ph) + 3.0, -0.006) for ph in phis]]   # boundary buried in the skull
    # rounded bead along the hairline (half-round profile from the skull out to the outer shell)
    bead = []
    for a in (-90.0, -45.0, 0.0, 45.0):
        ring = []
        for ph in phis:
            b = hairline(ph)
            off = hair_off(ph, b)
            ra = math.radians(a)
            ring.append(HEAD.pt(ph, b - 2.6 * math.cos(ra), 0.5 * off + 0.5 * off * math.sin(ra)))
        bead.append(ring)
    outer = [row(i, n_out, hair_off) for i in range(0, n_out + 1)]
    rings = inner + bead + outer
    loft(mb, rings, "Tentacle", {"Head": 1.0}, cap0=None, cap1=HEAD.pt(0, 90, hair_off(0, 90)))


# rev2: deeper dip over the nose bridge, and both edges converge into a swept-back point at the
# temples (just above the front of the ear), instead of a blunt goggle band
MASK_PHI = 95.0
MASK_TOP = [(0, 6), (10, 14), (22, 19), (40, 21), (58, 23.5), (70, 23), (80, 19.5), (88, 15.5), (95, 12)]
MASK_BOT = [(0, -6), (10, -15), (22, -20), (40, -20), (58, -16), (70, -7.5), (80, 1.5), (88, 7.5), (95, 11)]


def build_mask(mb):
    PHI = MASK_PHI
    ncol, m = 22, 3
    rings = []
    for j in range(ncol + 1):
        ph = -PHI + 2 * PHI * j / ncol
        top = sinterp(MASK_TOP, abs(ph))
        bot = sinterp(MASK_BOT, abs(ph))
        outer = [HEAD.pt(ph, bot + (top - bot) * i / m, 0.0045) for i in range(m + 1)]
        inner = [HEAD.pt(ph, top, -0.004), HEAD.pt(ph, bot, -0.004)]
        ring = outer + [HEAD.pt(ph, top + 0.8, 0.0012)] + inner + [HEAD.pt(ph, bot - 0.8, 0.0012)]
        rings.append(ring)
    loft(mb, rings, "Mask", {"Head": 1.0}, cap0='fan', cap1='fan')


EYE_PHI, EYE_LAM = 24.0, -2.0
EYE_W = (0.037, 0.047, 0.009)       # rev2: bigger, flatter eye white (half-width, half-height, depth)
PUPIL_W = (0.024, 0.033, 0.005)     # rev2: big dark pupil
GLINT_W = (0.007, 0.009, 0.0026)
EYE_BASE = 0.003                     # eye rim sits 1.5 mm under the mask surface (mask is at +4.5 mm)


def conform_head(planar, h):
    """Project a point of an eye's flat tangent frame radially onto the head ellipsoid and lift it by h
    along the surface normal -> the eye decals wrap around the skull instead of standing off it."""
    C = HEAD.C
    rx, ry, rz = HEAD.r
    q = planar - C
    s = math.sqrt((q.x / rx) ** 2 + (q.y / ry) ** 2 + (q.z / rz) ** 2)
    S = C + q / s
    n = V(((S.x - C.x) / rx ** 2, (S.y - C.y) / ry ** 2, (S.z - C.z) / rz ** 2)).normalized()
    return S + n * h


def dome_h(u, v, a, b, d):
    return d * math.sqrt(max(0.0, 1.0 - (u / a) ** 2 - (v / b) ** 2))


def build_face(mb):
    for sd in (1, -1):
        ph, lam = EYE_PHI * sd, EYE_LAM
        n = HEAD.normal(ph, lam)
        rot = frame_zy(n, UP)
        X, Y, Z = rot.col[0], rot.col[1], rot.col[2]
        ec = HEAD.pt(ph, lam, 0.0)
        u0, v0 = -sd * 0.0065, -0.002                 # pupil: a touch inward and down
        gu, gv = u0 + sd * 0.0075, v0 + 0.0125         # glint: upper outer part of the pupil

        def h_white(u, v):
            return EYE_BASE + dome_h(u, v, EYE_W[0], EYE_W[1], EYE_W[2])

        def h_pupil(u, v):
            return h_white(u, v) - 0.0012 + dome_h(u - u0, v - v0, PUPIL_W[0], PUPIL_W[1], PUPIL_W[2])

        def wrap(base_fn):
            def f(p, base_fn=base_fn):
                d = p - ec
                u, v, hl = d.dot(X), d.dot(Y), d.dot(Z)
                return conform_head(ec + X * u + Y * v, base_fn(u, v) + hl)
            return f
        ellipsoid(mb, ec, EYE_W, 16, 5, "EyeWhite", {"Head": 1.0}, rot=rot, hemi=True,
                  deform=wrap(lambda u, v: EYE_BASE))
        ellipsoid(mb, ec + X * u0 + Y * v0, PUPIL_W, 12, 3, "Pupil", {"Head": 1.0}, rot=rot, hemi=True,
                  deform=wrap(lambda u, v: h_white(u, v) - 0.0012))
        ellipsoid(mb, ec + X * gu + Y * gv, GLINT_W, 8, 2, "EyeWhite", {"Head": 1.0}, rot=rot, hemi=True,
                  deform=wrap(lambda u, v: h_pupil(u, v) - 0.0008))
    # tiny mouth
    n = HEAD.normal(0, -41)
    rot = frame_zy(n, UP)
    ellipsoid(mb, HEAD.pt(0, -41, -0.0008), (0.010, 0.0035, 0.0028), 6, 2, "Pupil", {"Head": 1.0}, rot=rot, hemi=True)
    # pointed ears
    for sd in (1, -1):
        # rev2: ears point out and back (elf ears), not up like horns
        root = HEAD.pt(90 * sd, -4, -0.014)
        dr = V((sd * 0.88, 0.30, 0.28)).normalized()
        path = Path([root, root + dr * 0.043, root + dr * 0.085])
        L = path.total
        specs = [(0.000, 0.032, 0.012, "Skin"), (0.26 * L, 0.031, 0.012, "Skin"), (0.52 * L, 0.023, 0.010, "Skin"),
                 (0.78 * L, 0.013, 0.0065, "Skin"), (L, 0.004, 0.003, "Skin")]
        tube(mb, path, specs, 8, lambda s: {"Head": 1.0}, side_fn=lambda p, T, s: V(UP),
             cap0='flat', cap1='point', tip_len1=0.012)


def tentacle_defs():
    C = HEAD.C
    defs = []
    # rev2: front strands hug the cheeks (no "earmuff" loop) and are flat paddles that widen
    # towards the tip, ending just above the shoulders
    for sd in (1, -1):   # 1,2: front side strands (L, R)
        pts = [HEAD.pt(55 * sd, 26, -0.006), HEAD.pt(66 * sd, 12, 0.016), HEAD.pt(72 * sd, -6, 0.022),
               C + V((0.150 * sd, -0.065, -0.140)), C + V((0.157 * sd, -0.068, -0.200))]
        defs.append(dict(pts=pts, piv=1, mid=3, front=True, sd=sd,
                         w=[(0, 0.024), (0.4, 0.030), (0.75, 0.040), (0.9, 0.042), (1, 0.036)],
                         t=[(0, 0.018), (1, 0.016)], cap=0.022, dome_k=2))
    # rev2: back strands lie close to the skull, are flat and wide so neighbours overlap into one
    # lobed mass, and stop well above the ink-tank cap with a slight outward kick
    for sd in (1, -1):   # 3,4: back inner strands
        pts = [HEAD.pt(165 * sd, 45, 0.0), HEAD.pt(168 * sd, 18, 0.025), HEAD.pt(169 * sd, -8, 0.028),
               HEAD.pt(168 * sd, -30, 0.030), C + V((0.075 * sd, 0.185, -0.200))]
        defs.append(dict(pts=pts, piv=2, mid=3, front=False, sd=sd,
                         w=[(0, 0.036), (0.5, 0.042), (0.85, 0.048), (1, 0.040)],
                         t=[(0, 0.018), (1, 0.017)], cap=0.030, dome_k=3))
    for sd in (1, -1):   # 5,6: back outer strands
        pts = [HEAD.pt(136 * sd, 40, 0.0), HEAD.pt(139 * sd, 15, 0.025), HEAD.pt(142 * sd, -10, 0.030),
               C + V((0.145 * sd, 0.140, -0.130)), C + V((0.185 * sd, 0.135, -0.175))]
        defs.append(dict(pts=pts, piv=2, mid=3, front=False, sd=sd,
                         w=[(0, 0.036), (0.5, 0.043), (0.85, 0.047), (1, 0.040)],
                         t=[(0, 0.018), (1, 0.017)], cap=0.030, dome_k=3))
    return defs


def build_sucker(mb, c, nrm, tang, r, w):
    """Cheap suction cup: 6-sided raised rim with a dimple (18 tris), base sunk into the strand."""
    rot = frame_zy(nrm, tang)
    X, Y = rot.col[0], rot.col[1]
    dirs = [X * math.cos(2 * math.pi * j / 6) + Y * math.sin(2 * math.pi * j / 6) for j in range(6)]
    base = [c + d * r - nrm * 0.0020 for d in dirs]
    rim = [c + d * (r * 0.70) + nrm * 0.0032 for d in dirs]
    loft(mb, [base, rim], "Tentacle", w, cap0=None, cap1=c + nrm * 0.0008)


def build_tentacles(mb, bones_out):
    for k, td in enumerate(tentacle_defs(), start=1):
        path = Path(td["pts"])
        c = path.ctrl_s
        s_piv, s_mid = c[td["piv"]], c[td["mid"]]
        an, bn = "Tentacle_%d_A" % k, "Tentacle_%d_B" % k
        n_r = 10
        specs = []
        for i in range(n_r + 1):
            f = i / n_r
            s = path.total * f
            specs.append((s, interp(td["w"], f), interp(td["t"], f), "Tentacle"))
        # wide flat strands need a longer blend at the A->B joint or their edges stretch when they bend
        bw = (0.035, 0.045) if td["front"] else (0.045, 0.070)
        wf = lambda s, s_piv=s_piv, s_mid=s_mid, an=an, bn=bn, bw=bw: chain_weights(
            s, [s_piv, s_mid], ["Head", an, bn], list(bw))
        if td["front"]:
            side = lambda p, T, s: V((1, 0, 0))
        else:
            side = lambda p, T, s: T.cross((p - HEAD.C).normalized())
        tube(mb, path, specs, 12, wf, side_fn=side, cap0='flat', cap1='dome', cap_len1=td["cap"],
             dome_k=td["dome_k"])
        # bones
        p_piv, _ = path.at(s_piv)
        p_mid, _ = path.at(s_mid)
        p_end, T_end = path.at(path.total)
        p_tip = p_end + T_end * td["cap"]
        outv = (p_piv - HEAD.C).normalized()
        bones_out.append((an, p_piv, p_mid, "Head", False, tuple(outv)))
        outm = (p_mid - HEAD.C).normalized()
        bones_out.append((bn, p_mid, p_tip, an, True, tuple(outm)))
        # suction cups on the front face of the two front strands (rev2: 3 cheap cups per strand)
        if td["front"]:
            for f in (0.60, 0.74, 0.88):
                s = path.total * f
                p, T = path.at(s)
                N = V((1, 0, 0)) - T * T.x
                N.normalize()
                B = T.cross(N)
                rb = interp(td["t"], f)
                build_sucker(mb, p + B * rb, B, T, 0.0085, wf(s))


def build_straps(mb):
    e = TORSO_E
    for sd in (1, -1):
        xs = 0.070 * sd
        # profile of the torso surface in the YZ plane at x = xs
        front, back = [], []
        zs = [0.885, 0.93, 0.98, 1.02, 1.05, 1.07, 1.082, 1.090]
        for z in zs:
            a, b, yo = torso_at(z)
            h = b * max(0.0, 1.0 - (abs(xs) / a) ** e) ** (1.0 / e)
            front.append(V((0, yo - h, z)))
            back.append(V((0, yo + h, z)))
        a, b, yo = torso_at(1.096)
        top = V((0, yo, 1.097))
        prof = front + [top] + back[::-1][:-2]
        # push outward in the YZ plane
        cen = V((0, 0.0, 0.99))
        pts = []
        for i, p in enumerate(prof):
            q0 = prof[max(i - 1, 0)]
            q1 = prof[min(i + 1, len(prof) - 1)]
            t = (q1 - q0).normalized()
            nrm = V((0, -t.z, t.y))
            if nrm.dot(p - cen) < 0:
                nrm = -nrm
            pts.append(V((xs, p.y, p.z)) + nrm * 0.0065)
        path = Path(pts)
        n = 12
        specs = [(path.total * i / n, 0.017, 0.0055, "ShoeSole") for i in range(n + 1)]
        tube(mb, path, specs, 4, lambda s: {"Chest": 1.0}, side_fn=lambda p, T, s: V((1, 0, 0)),
             cap0='flat', cap1='flat', phase=math.pi / 4)


# rev2: the tank is the main team-colour read from the gameplay camera behind the player, so it is
# now a wide, flat, boxy flask (was a narrow round water bottle); cap top stays below the shoulders
TANK_Y = 0.140
TANK_Z0, TANK_Z1 = 0.830, 0.980          # glass bottom / top
TANK_A, TANK_B, TANK_E = 0.090, 0.040, 3.2
TANK_CL = 0.024                          # rounded end length
TANK_ZC = 0.5 * (TANK_Z0 + TANK_Z1)


def build_tank():
    glass = MB()
    z0, z1 = TANK_Z0 + TANK_CL, TANK_Z1 - TANK_CL
    path = Path([V((0, TANK_Y, z0)), V((0, TANK_Y, 0.5 * (z0 + z1))), V((0, TANK_Y, z1))])
    specs = [(0.0, TANK_A, TANK_B, "TankGlass"), (path.total * 0.5, TANK_A + 0.002, TANK_B + 0.002, "TankGlass"),
             (path.total, TANK_A, TANK_B, "TankGlass")]
    tube(glass, path, specs, 14, lambda s: {}, side_fn=lambda p, T, s: V((1, 0, 0)), e=TANK_E,
         cap0='dome', cap1='dome', cap_len0=TANK_CL, cap_len1=TANK_CL)
    # cap (dark) on top; its base is sunk into the glass dome, its top stays under the 1.045 shoulder line
    path = Path([V((0, TANK_Y, 0.966)), V((0, TANK_Y, 1.000)), V((0, TANK_Y, 1.036))])
    specs = [(0.0, 0.027, 0.027, "ShoeSole"), (0.050, 0.031, 0.031, "ShoeSole"), (0.057, 0.030, 0.030, "ShoeSole"),
             (path.total, 0.025, 0.025, "ShoeSole")]
    tube(glass, path, specs, 12, lambda s: {}, cap0='flat', cap1='flat')
    # bottom holder cup
    rings = []
    for z, a, b in ((0.812, 0.078, 0.030), (0.818, 0.096, 0.046), (0.858, 0.099, 0.049), (0.864, 0.097, 0.047),
                    (0.862, 0.088, 0.038)):
        rings.append(se_ring(V((0, TANK_Y, z)), V((1, 0, 0)), V((0, 1, 0)), a, b, 14, TANK_E))
    loft(glass, rings, "ShoeSole", {}, cap0='fan', cap1='fan')
    # backplate
    ellipsoid(glass, V((0, 0.098, TANK_ZC)), (0.085, 0.018, 0.095), 10, 6, "ShoeSole", {})
    fill = MB()
    fz0, fz1, fcl = 0.860, 0.952, 0.018
    path = Path([V((0, TANK_Y, fz0)), V((0, TANK_Y, 0.5 * (fz0 + fz1))), V((0, TANK_Y, fz1))])
    specs = [(0.0, 0.080, 0.034, "TankInk"), (path.total * 0.5, 0.081, 0.035, "TankInk"),
             (path.total, 0.080, 0.034, "TankInk")]
    tube(fill, path, specs, 14, lambda s: {}, side_fn=lambda p, T, s: V((1, 0, 0)), e=TANK_E,
         cap0='dome', cap1='flat', cap_len0=fcl)
    return glass, fill


# ---------------------------------------------------------------------------------------
# assembly
# ---------------------------------------------------------------------------------------

def main():
    clear_scene()
    for n, h, r in MATS:
        make_material(n, h, rough=r)
    coll = get_coll("Inkling")

    body = MB()
    tent_bones = []
    build_torso(body)
    for sd in (1, -1):
        build_arm(body, sd)
        build_hand(body, sd)
        build_leg(body, sd)
        build_shoe(body, sd)
    build_head(body)
    build_hair_cap(body)
    build_mask(body)
    build_face(body)
    build_tentacles(body, tent_bones)
    build_straps(body)
    glass, fill = build_tank()

    # ---- uniform rescale so the top of the hair is exactly TARGET_H
    zmax = max(max(v.z for v in body.V), max(v.z for v in glass.V))
    k = TARGET_H / zmax
    print("design height %.4f -> scale %.4f" % (zmax, k))
    chin = HEAD.C.z - HEAD.r[2]
    print("HEAD height %.3f m = 1/%.2f of total; visible neck (chin - collar) %.3f m" % (
        (zmax - chin) * k, zmax / (zmax - chin), (chin - 1.114) * k))
    for mb in (body, glass, fill):
        mb.V = [v * k for v in mb.V]

    # ---- armature
    bones = [("Root", (0, 0, 0), (0, 0, 0.10), None, False, FWD),
             ("Hips", (0, 0, HIP_Z), (0, 0, 0.790), "Root", False, FWD),
             ("Spine", (0, 0, 0.790), (0, 0, 0.915), "Hips", True, FWD),
             ("Chest", (0, 0, 0.915), (0, 0, NECK_Z), "Spine", True, FWD),
             ("Neck", (0, 0, NECK_Z), (0, 0.004, HEADJ_Z), "Chest", True, FWD),
             ("Head", (0, 0.004, HEADJ_Z), (0, 0.0, 1.460), "Neck", True, FWD)]
    for sd in (1, -1):
        d, S, E, Wr, Ht = arm_geo(sd)
        bones += [(sname("UpperArm", sd), S, E, "Chest", False, FWD),
                  (sname("LowerArm", sd), E, Wr, sname("UpperArm", sd), True, FWD),
                  (sname("Hand", sd), Wr, Ht, sname("LowerArm", sd), True, FWD)]
    for sd in (1, -1):
        H, K, A, Toe = leg_geo(sd)
        bones += [(sname("UpperLeg", sd), H, K, "Hips", False, FWD),
                  (sname("LowerLeg", sd), K, A, sname("UpperLeg", sd), True, FWD),
                  (sname("Foot", sd), A, Toe, sname("LowerLeg", sd), True, UP)]
    bones += tent_bones
    rig = create_armature("Rig", bones, coll, scale=k)

    body_obj = build_object(body, "Body", BODY_MATS, coll)
    skin_to(body_obj, rig)
    tank_obj = build_object(glass, "InkTank", ["TankGlass", "ShoeSole"], coll, groups=False)
    fill_obj = build_object(fill, "InkTankFill", ["TankInk"], coll, groups=False)

    # InkTank: origin at glass centre, bone-parented to Chest (rigid)
    tank_c = V((0, TANK_Y, TANK_ZC)) * k
    me = tank_obj.data
    for v in me.vertices:
        v.co = v.co - tank_c
    parent_to_bone(tank_obj, rig, "Chest", Matrix.Translation(tank_c))
    # InkTankFill: origin at the BOTTOM of the fill, local +Y = world up (scale local Y = ink level)
    zmin = min(v.co.z for v in fill_obj.data.vertices)
    fill_origin = V((0, TANK_Y * k, zmin))
    fmat = Matrix.Translation(fill_origin) @ Matrix.Rotation(math.radians(90.0), 4, 'X')
    inv = fmat.inverted()
    for v in fill_obj.data.vertices:
        v.co = inv @ v.co
    parent_to_bone(fill_obj, rig, "Chest", fmat)

    # WeaponSocket: in the right palm; local +Z = barrel direction (along the hand when aiming),
    # local +Y = gun "up" (thumb side; becomes world up when the arm is raised to aim forward)
    d, f, n, Wr = hand_frame(-1)
    sock = bpy.data.objects.new("WeaponSocket", None)
    sock.empty_display_type = 'ARROWS'
    sock.empty_display_size = 0.08
    coll.objects.link(sock)
    sock_pos = (Wr + d * 0.043 + n * 0.012) * k
    rotm = Matrix((f.cross(d), f, d)).transposed()      # columns X, Y, Z
    parent_to_bone(sock, rig, "Hand.R", Matrix.Translation(sock_pos) @ rotm.to_4x4())

    rep_ = weight_stress_test(rig, body_obj, 40.0)
    worst = sorted(rep_.items(), key=lambda kv: -kv[1][0])
    print("WEIGHT40 worst stretch per bone:", ", ".join("%s %.2f/%.2f" % (n_, a_, b_) for n_, (a_, b_) in worst[:12]))
    print("WEIGHT40 global max stretch %.3f, min squash %.3f" % (max(v[0] for v in rep_.values()),
                                                                min(v[1] for v in rep_.values())))
    wsum = [sum(g.weight for g in v.groups) for v in body_obj.data.vertices]
    ninf = [len(v.groups) for v in body_obj.data.vertices]
    print("WEIGHTS verts=%d unweighted=%d sum[min %.4f max %.4f] max influences=%d" % (
        len(wsum), sum(1 for w in wsum if w < 0.999), min(wsum), max(wsum), max(ninf)))
    tris = {o.name: tri_count(o) for o in (body_obj, tank_obj, fill_obj)}
    print("TRIS", tris, "TOTAL", sum(tris.values()))
    print("HEIGHT body zmax = %.4f" % max(v.co.z for v in body_obj.data.vertices))

    # ------------------------------------------------------------------ previews
    if DO_RENDER:
        render_previews(rig, body_obj, tank_obj, fill_obj, sock, k)

    # ------------------------------------------------------------------ export
    reset_pose(rig)
    restore_export_colors()
    bpy.context.view_layer.update()
    export_fbx(FBX_OUT, [rig, body_obj, tank_obj, fill_obj, sock])
    save_blend(BLEND_OUT)


def aim_pose(rig, k):
    """Arms raised to aim forward (thumb up), tentacles swung 35 deg, left knee bent."""
    bpy.context.view_layer.update()
    for sd in (1, -1):
        d, S, E, Wr, Ht = arm_geo(sd)
        f = V(FWD)
        src = Matrix((d, f, d.cross(f))).transposed()
        tgt_d = V((0.0, -1.0, 0.0))
        tgt_f = V((0.0, 0.0, 1.0))
        tgt = Matrix((tgt_d, tgt_f, tgt_d.cross(tgt_f))).transposed()
        R = (tgt @ src.inverted()).to_4x4()
        pb = rig.pose.bones[sname("UpperArm", sd)]
        Sk = S * k
        M = Matrix.Translation(Sk) @ R @ Matrix.Translation(-Sk) @ pb.bone.matrix_local
        pb.matrix = M
        bpy.context.view_layer.update()
    for i in range(1, 7):
        pa = rig.pose.bones["Tentacle_%d_A" % i]
        pa.rotation_mode = 'XYZ'
        pa.rotation_euler = (math.radians(35), 0, 0)
        pbb = rig.pose.bones["Tentacle_%d_B" % i]
        pbb.rotation_mode = 'XYZ'
        pbb.rotation_euler = (math.radians(20), 0, 0)
    ul = rig.pose.bones["UpperLeg.L"]
    ul.rotation_mode = 'XYZ'
    ul.rotation_euler = (math.radians(40), 0, 0)
    ll = rig.pose.bones["LowerLeg.L"]
    ll.rotation_mode = 'XYZ'
    ll.rotation_euler = (math.radians(-60), 0, 0)
    bpy.context.view_layer.update()


def render_previews(rig, body_obj, tank_obj, fill_obj, sock, k):
    st = Stage()
    set_mat_color("Tentacle", PREVIEW_TEAM)
    set_mat_color("TankInk", PREVIEW_TEAM)
    set_mat_color("TankGlass", "#CFE8F5", alpha=0.35)
    tgt = V((0, 0, 0.775))
    st.view(tgt, 1.62, 0, 4)
    st.render(os.path.join(PREVIEW_DIR, "inkling_front.png"))
    st.view(tgt, 1.62, 90, 4)
    st.render(os.path.join(PREVIEW_DIR, "inkling_side.png"))
    st.view(tgt, 1.62, 145, 14)
    st.render(os.path.join(PREVIEW_DIR, "inkling_back34.png"))
    # close-up checks (not deliverables)
    hc = V((0, 0, (HEAD.C.z - 0.03) * k))
    st.view(hc, 0.50, 0, 2)
    st.render(os.path.join(PREVIEW_DIR, "check_head_front.png"))
    st.view(hc, 0.50, 90, 2)
    st.render(os.path.join(PREVIEW_DIR, "check_head_side.png"))
    st.view(V((0, 0, 1.10 * k)), 0.80, 180, 12)
    st.render(os.path.join(PREVIEW_DIR, "check_back.png"))
    # posed test with a stand-in gun on the WeaponSocket
    gun = MB()
    path = Path([V((0, 0, -0.03)), V((0, 0, 0.06)), V((0, 0, 0.16))])
    tube(gun, path, [(0, 0.028, 0.028, "ShoeSole"), (path.total, 0.022, 0.022, "ShoeSole")], 10, lambda s: {},
         side_fn=lambda p, T, s: V((1, 0, 0)), cap0='flat', cap1='flat')
    ellipsoid(gun, V((0, 0.035, 0.02)), (0.016, 0.03, 0.02), 8, 5, "ShoeSole", {})
    gun_obj = build_object(gun, "PreviewGun", ["ShoeSole"], st.coll, groups=False)
    gun_obj.parent = sock
    gun_obj.matrix_parent_inverse = Matrix.Identity(4)
    aim_pose(rig, k)
    lo, hi = deform_stats(body_obj)
    print("STRETCH max:", ["%.3f @ %s" % (r, tuple(round(c, 3) for c in p)) for r, i, p in hi])
    print("STRETCH min:", ["%.3f @ %s" % (r, tuple(round(c, 3) for c in p)) for r, i, p in lo])
    sm = sock.matrix_world
    print("SOCKET posed: pos", tuple(round(c, 3) for c in sm.translation),
          "Z(barrel)", tuple(round(c, 3) for c in sm.col[2][:3]), "Y(up)", tuple(round(c, 3) for c in sm.col[1][:3]))
    st.view(V((0, -0.12, 0.80)), 1.70, 55, 10)
    st.render(os.path.join(PREVIEW_DIR, "inkling_posed.png"))
    st.view(V((0.03, -0.06, 0.64 * k)), 0.55, 60, 6)
    st.render(os.path.join(PREVIEW_DIR, "check_posed_hip.png"))
    st.view(V((0, 0.02, 1.15 * k)), 0.75, 150, 12)
    st.render(os.path.join(PREVIEW_DIR, "check_posed_back.png"))
    bpy.data.objects.remove(gun_obj, do_unlink=True)
    reset_pose(rig)


main()
