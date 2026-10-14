# -*- coding: utf-8 -*-
"""verify_fbx.py - re-import the exported FBX files into an empty scene and print what Unity will see.

  "C:/Program Files/Blender Foundation/Blender 4.3/blender.exe" -b --factory-startup \
      -P ArtSource/Characters/scripts/verify_fbx.py
"""
import os
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
FILES = [os.path.join(PROJECT, "Assets", "Art", "Characters", n) for n in ("Inkling.fbx", "Squid.fbx")]

for path in FILES:
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials):
        for d in list(coll):
            coll.remove(d)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=False)
    print("=" * 70)
    print(os.path.basename(path), "%.1f KB" % (os.path.getsize(path) / 1024.0))
    tris = 0
    for o in sorted(bpy.data.objects, key=lambda o: o.name):
        par = o.parent.name if o.parent else "-"
        if o.parent_type == 'BONE':
            par += ":" + o.parent_bone
        line = "  %-12s %-9s parent=%-18s" % (o.name, o.type, par)
        if o.type == 'MESH':
            t = sum(len(p.vertices) - 2 for p in o.data.polygons)
            tris += t
            line += " tris=%d mats=%s vgroups=%d mods=%s" % (
                t, [m.name for m in o.data.materials], len(o.vertex_groups), [m.type for m in o.modifiers])
        mw = o.matrix_world
        line += " loc=(%.3f, %.3f, %.3f)" % tuple(mw.translation)
        if o.name in ("InkTankFill", "WeaponSocket"):
            line += " localY->world(%.2f, %.2f, %.2f) localZ->world(%.2f, %.2f, %.2f)" % (
                tuple(mw.col[1][:3]) + tuple(mw.col[2][:3]))
        print(line)
        if o.type == 'ARMATURE':
            print("  bones:", ", ".join(b.name for b in o.data.bones))
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    zs = [(o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices]
    ys = [(o.matrix_world @ v.co).y for o in meshes for v in o.data.vertices]
    xs = [(o.matrix_world @ v.co).x for o in meshes for v in o.data.vertices]
    print("  TOTAL tris=%d  bbox x[%.3f, %.3f] y[%.3f, %.3f] z[%.3f, %.3f]" % (
        tris, min(xs), max(xs), min(ys), max(ys), min(zs), max(zs)))
    for m in bpy.data.materials:
        try:
            c = m.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value
            print("  material %-10s base(linear)=(%.3f, %.3f, %.3f)" % (m.name, c[0], c[1], c[2]))
        except Exception:
            print("  material", m.name)
