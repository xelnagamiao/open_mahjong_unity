"""Resize the authored tile without losing its shading/outline normal data.

Run inside Blender, opening the canonical .blend first. This writes a separate
candidate .blend and a validation report; export that candidate with
export_edited_blend.py. Ratios are absolute targets, so repeating a run does not
compound the resize. The source file is never overwritten by this tool.
"""
import argparse
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Vector


def unit(v):
    length = math.sqrt(sum(c * c for c in v))
    assert math.isfinite(length) and length > 1e-12
    return tuple(c / length for c in v)


def decode_oct(uv):
    # Exactly the convention used by ThreeDTiles.shader DecodeOutlineNormal.
    x, y = (float(c) * 2.0 - 1.0 for c in uv)
    z = 1.0 - abs(x) - abs(y)
    t = min(max(-z, 0.0), 1.0)
    x += -t if x >= 0 else t
    y += -t if y >= 0 else t
    return unit((x, y, z))


def encode_oct(normal):
    n = unit(normal)
    denominator = sum(abs(c) for c in n)
    x, y, z = (c / denominator for c in n)
    if z < 0:
        x, y = ((1 - abs(y)) * (1 if x >= 0 else -1),
                (1 - abs(x)) * (1 if y >= 0 else -1))
    return ((x + 1) * 0.5, (y + 1) * 0.5)


def snapshot(mesh):
    return {
        "vertices": [list(v.co) for v in mesh.vertices],
        "polygons": [list(p.vertices) for p in mesh.polygons],
        "loop_vertices": [loop.vertex_index for loop in mesh.loops],
        "loop_normals": [list(n.vector) for n in mesh.corner_normals],
        "uv_layers": {uv.name: [list(item.uv) for item in uv.data]
                      for uv in mesh.uv_layers},
        "colors": {color.name: {"domain": color.domain,
                                  "type": color.data_type,
                                  "values": [list(item.color) for item in color.data]}
                   for color in mesh.color_attributes},
        "smooth_faces": [p.use_smooth for p in mesh.polygons],
        "materials": [material.name for material in mesh.materials],
    }


def dimensions(vertices):
    return [max(v[axis] for v in vertices) - min(v[axis] for v in vertices)
            for axis in range(3)]


def hash_data(value):
    return hashlib.sha256(json.dumps(value, separators=(",", ":")).encode()).hexdigest()


def angular_error(actual, expected):
    a, b = unit(actual), unit(expected)
    cross = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2],
             a[0] * b[1] - a[1] * b[0])
    return math.degrees(math.atan2(math.sqrt(sum(c * c for c in cross)),
                                   sum(x * y for x, y in zip(a, b))))


parser = argparse.ArgumentParser()
parser.add_argument("--height-ratio", type=float, default=1.33)
parser.add_argument("--thickness-ratio", type=float, default=0.65)
parser.add_argument("--output-blend", type=Path, required=True)
parser.add_argument("--report", type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])

source = Path(bpy.data.filepath)
assert source.suffix.lower() == ".blend", "Open the source blend first."
assert args.output_blend.resolve() != source.resolve(), "Use a separate candidate path."
assert 0 < args.height_ratio < 10 and 0 < args.thickness_ratio < 10
obj = bpy.data.objects["Cube"]
mesh = obj.data
assert obj.type == "MESH" and mesh.name == "Cube"
assert obj.parent is None and not obj.modifiers
assert tuple(obj.location) == tuple(obj.rotation_euler) == (0, 0, 0)
assert tuple(obj.scale) == (1, 1, 1)
assert [uv.name for uv in mesh.uv_layers] == [
    "TileFace", "TileBack", "TileEdge", "OutlineSmoothNormal"]
assert mesh.has_custom_normals
assert len(mesh.materials) == 1 and mesh.materials[0].name == "Material"
assert len(mesh.color_attributes) == 1
assert mesh.color_attributes[0].data_type == "FLOAT_COLOR"
before = snapshot(mesh)
old_dimensions = dimensions(before["vertices"])
target_dimensions = [old_dimensions[0], old_dimensions[0] * args.height_ratio,
                     old_dimensions[0] * args.thickness_ratio]
scale = [target_dimensions[i] / old_dimensions[i] for i in range(3)]

# The current asset's Blender and Unity mesh axes correspond X/Y/Z, with only
# an X handedness reflection and uniform unit conversion. A positive diagonal
# resize therefore uses the same inverse-transpose factors in either space.
def resize_normal(n):
    return unit(tuple(n[i] / scale[i] for i in range(3)))


target_lighting_normals = [resize_normal(n) for n in before["loop_normals"]]
target_outline_normals = [resize_normal(decode_oct(uv))
                          for uv in before["uv_layers"]["OutlineSmoothNormal"]]
for vertex, old_vertex in zip(mesh.vertices, before["vertices"]):
    vertex.co = tuple(old_vertex[i] * scale[i] for i in range(3))
mesh.update()
mesh.normals_split_custom_set(target_lighting_normals)
for item, normal in zip(mesh.uv_layers["OutlineSmoothNormal"].data, target_outline_normals):
    item.uv = encode_oct(normal)
mesh.update()
after = snapshot(mesh)

unchanged = {name: before[name] == after[name] for name in (
    "polygons", "loop_vertices", "colors", "smooth_faces", "materials")}
unchanged.update({name: before["uv_layers"][name] == after["uv_layers"][name]
                  for name in ("TileFace", "TileBack", "TileEdge")})
assert all(unchanged.values()), unchanged
actual_dimensions = dimensions(after["vertices"])
assert max(abs(a - b) for a, b in zip(actual_dimensions, target_dimensions)) < 1e-6
assert max(abs(sum(p[i] for p in (min(after["vertices"], key=lambda v: v[i]),
                                max(after["vertices"], key=lambda v: v[i]))))
           for i in range(3)) < 1e-6, "The mesh origin must remain centered."
lighting_errors = [angular_error(n, target) for n, target in
                   zip(after["loop_normals"], target_lighting_normals)]
outline_errors = [angular_error(decode_oct(uv), target) for uv, target in
                  zip(after["uv_layers"]["OutlineSmoothNormal"], target_outline_normals)]
assert max(lighting_errors) < 0.1, "Custom normal storage exceeded angular tolerance."
assert max(outline_errors) < 0.05, "Octahedral encoding exceeded angular tolerance."
normal_length_errors = [abs(Vector(n).length - 1.0) for n in after["loop_normals"]]
assert max(normal_length_errors) < 1e-5

# Equal spatial points must keep equal outline directions across UV/normal seams.
spatial_uvs = {}
max_seam_error = 0.0
for loop, uv in zip(mesh.loops, after["uv_layers"]["OutlineSmoothNormal"]):
    key = tuple(round(c, 7) for c in mesh.vertices[loop.vertex_index].co)
    if key in spatial_uvs:
        max_seam_error = max(max_seam_error, max(abs(a - b) for a, b in zip(uv, spatial_uvs[key])))
    else:
        spatial_uvs[key] = uv
assert max_seam_error < 1e-6, "Outline normal seams would open the hull."

report = {
    "source": str(source),
    "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest(),
    "candidate": str(args.output_blend),
    "blender_version": bpy.app.version_string,
    "requested_width_height_thickness_ratio": [1, args.height_ratio, args.thickness_ratio],
    "before_blender_dimensions": old_dimensions,
    "after_blender_dimensions": actual_dimensions,
    "expected_unity_mesh_dimensions": [d * 0.01 for d in actual_dimensions],
    "vertex_scale_factors": scale,
    "vertex_count": len(mesh.vertices),
    "triangle_count": sum(len(p.vertices) - 2 for p in mesh.polygons),
    "uv_count": len(mesh.uv_layers),
    "identity_object_transform": True,
    "unchanged": unchanged,
    "before_invariants_sha256": {k: hash_data(before[k]) for k in (
        "polygons", "loop_vertices", "colors", "smooth_faces", "materials")},
    "uv0_to_2_sha256": {name: hash_data(after["uv_layers"][name])
                        for name in ("TileFace", "TileBack", "TileEdge")},
    "max_lighting_normal_error_degrees": max(lighting_errors),
    "max_lighting_normal_length_error": max(normal_length_errors),
    "max_outline_normal_error_degrees": max(outline_errors),
    "max_outline_normal_seam_error": max_seam_error,
    "normal_transform": "normalize(inverse_transpose(diagonal_vertex_scale) * old_normal)",
    "outline_normal_transform": "Decode Unity object-space oct normal; inverse transpose; re-encode",
    "note": "Dimensions only; topology, original bevel shape, color regions and texture UVs retained.",
}
args.output_blend.parent.mkdir(parents=True, exist_ok=True)
# Disable .blend1 creation for this isolated output. Canonical backup is managed
# by the caller before replacing a source file.
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(args.output_blend), check_existing=False, relative_remap=False)
report["candidate_sha256"] = hashlib.sha256(args.output_blend.read_bytes()).hexdigest()
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print("TILE_RESIZE_VALIDATED", json.dumps(report, ensure_ascii=False))
