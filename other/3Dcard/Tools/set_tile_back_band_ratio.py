"""Move the authored white/back side boundary while preserving the outer body.

Run in Blender after opening the canonical source. The ratio measures the back
portion of the complete front-to-back depth, including the rear bevel. Only
the existing straight-side boundary and its depth UV move; no new topology is
created. A separate candidate is required so the source is never overwritten.
"""
import argparse
import hashlib
import json
import math
import sys
from collections import defaultdict
from pathlib import Path

import bpy
from mathutils import Vector


def snapshot(mesh):
    return {
        'vertices': [list(v.co) for v in mesh.vertices],
        'polygons': [list(p.vertices) for p in mesh.polygons],
        'loop_vertices': [loop.vertex_index for loop in mesh.loops],
        'loop_normals': [list(n.vector) for n in mesh.corner_normals],
        'polygon_normals': [list(p.normal) for p in mesh.polygons],
        'uv_layers': {uv.name: [list(item.uv) for item in uv.data]
                      for uv in mesh.uv_layers},
        'colors': {color.name: {'domain': color.domain,
                               'type': color.data_type,
                               'values': [list(item.color) for item in color.data]}
                   for color in mesh.color_attributes},
        'smooth_faces': [p.use_smooth for p in mesh.polygons],
        'materials': [material.name for material in mesh.materials],
    }


def bounds(vertices):
    return [[min(v[a] for v in vertices), max(v[a] for v in vertices)]
            for a in range(3)]


def angular_error(a, b):
    a, b = Vector(a).normalized(), Vector(b).normalized()
    return math.degrees(math.atan2(a.cross(b).length, a.dot(b)))


def near_color(actual, wanted):
    return max(abs(a - b) for a, b in zip(actual, wanted)) < 1e-6


def check_closed_convex(mesh):
    points = [v.co.copy() for v in mesh.vertices]
    welded = {}
    index = []
    for point in points:
        key = tuple(round(c, 6) for c in point)
        if key not in welded:
            welded[key] = len(welded)
        index.append(welded[key])
    edge_uses = defaultdict(list)
    for face in mesh.polygons:
        assert len(face.vertices) == 3
        a, b, c = [points[i] for i in face.vertices]
        cross = (b-a).cross(c-a)
        assert cross.length > 1e-8, 'Degenerate triangle.'
        normal = cross.normalized()
        assert normal.dot(a) > 0, 'Winding must face out from the centered origin.'
        assert max(normal.dot(p-a) for p in points) < 2e-6, 'Body must remain convex.'
        ids = [index[i] for i in face.vertices]
        for start, end in zip(ids, ids[1:] + ids[:1]):
            edge_uses[tuple(sorted((start, end)))].append((start, end))
    for key, uses in edge_uses.items():
        assert len(uses) == 2 and uses[0] == tuple(reversed(uses[1])), (key, uses)
    assert len(welded) - len(edge_uses) + len(mesh.polygons) == 2
    return {'welded_vertices': len(welded), 'spatial_edges': len(edge_uses),
            'triangles': len(mesh.polygons), 'closed_outward_convex': True}


parser = argparse.ArgumentParser()
parser.add_argument('--back-ratio', type=float, default=0.45)
parser.add_argument('--output-blend', type=Path, required=True)
parser.add_argument('--report', type=Path, required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
source = Path(bpy.data.filepath)
assert source.suffix.lower() == '.blend'
assert args.output_blend.resolve() != source.resolve(), 'Write a separate candidate.'
assert math.isfinite(args.back_ratio) and 0 < args.back_ratio < 1
obj = bpy.data.objects['Cube']
mesh = obj.data
assert obj.type == 'MESH' and mesh.name == 'Cube' and obj.parent is None and not obj.modifiers
assert tuple(obj.location) == tuple(obj.rotation_euler) == (0, 0, 0)
assert tuple(obj.scale) == (1, 1, 1)
assert [u.name for u in mesh.uv_layers] == ['TileFace', 'TileBack', 'TileEdge', 'OutlineSmoothNormal']
assert mesh.has_custom_normals
assert len(mesh.materials) == 1 and mesh.materials[0].name == 'Material'
assert len(mesh.color_attributes) == 1
color = mesh.color_attributes[0]
assert color.domain == 'CORNER' and color.data_type == 'FLOAT_COLOR'
before = snapshot(mesh)
before_bounds = bounds(before['vertices'])
front_z, back_z = before_bounds[2]
depth = back_z - front_z
assert front_z < 0 < back_z
colors_by_z = defaultdict(set)
for loop, item in zip(mesh.loops, color.data):
    z = round(mesh.vertices[loop.vertex_index].co.z, 6)
    if near_color(item.color, (0, 0, 1, 1)):
        colors_by_z[z].add('white')
    elif near_color(item.color, (0, 0, 0, 1)):
        colors_by_z[z].add('back')
boundary_levels = [z for z, roles in colors_by_z.items() if roles == {'white', 'back'}]
assert len(boundary_levels) == 1, 'Expected one discontinuous white/back side boundary.'
boundary_z = boundary_levels[0]
boundary_vertices = [v.index for v in mesh.vertices if abs(v.co.z-boundary_z) < 1e-6]
boundary_set = set(boundary_vertices)
boundary_loops = [l.index for l in mesh.loops if l.vertex_index in boundary_set]
front_side_z = max(z for z, roles in colors_by_z.items() if roles == {'white'} and z < boundary_z)
back_side_z = min(z for z, roles in colors_by_z.items() if roles == {'back'} and z > boundary_z)
target_z = back_z - depth * args.back_ratio
assert front_side_z + depth*1e-4 < target_z < back_side_z - depth*1e-4, 'Boundary must stay inside the straight side.'
edge_uv = mesh.uv_layers['TileEdge']
for loop, item in zip(mesh.loops, edge_uv.data):
    normalized_depth = (mesh.vertices[loop.vertex_index].co.z-front_z)/depth
    assert abs(item.uv.y-normalized_depth) < 1e-6, 'Unexpected side UV layout: do not blindly remap it.'
old_back_ratio = (back_z-boundary_z)/depth
for vertex_id in boundary_vertices:
    mesh.vertices[vertex_id].co.z = target_z
mesh.update()
max_face_normal_error = max(angular_error(p.normal, old) for p, old in zip(mesh.polygons, before['polygon_normals']))
assert max_face_normal_error < 0.005, 'A face plane changed; this tool must not reshape bevels.'
# The moved vertices slide in the existing straight face planes. Keep Blender's
# stored custom normal data instead of recalculating or re-encoding it: calling
# normals_split_custom_set again introduces more quantization than leaving it
# intact. Blender's corner-space reconstruction still has a small float error,
# which is bounded explicitly below.
for loop_id in boundary_loops:
    edge_uv.data[loop_id].uv.y = 1.0 - args.back_ratio
mesh.update()
after = snapshot(mesh)
unchanged = {name: before[name] == after[name] for name in
             ('polygons', 'loop_vertices', 'colors', 'smooth_faces', 'materials')}
unchanged.update({name: before['uv_layers'][name] == after['uv_layers'][name]
                  for name in ('TileFace', 'TileBack', 'OutlineSmoothNormal')})
unchanged['body_bounds'] = before_bounds == bounds(after['vertices'])
unchanged['all_vertex_xy'] = all(a[:2] == b[:2] for a, b in zip(before['vertices'], after['vertices']))
unchanged['non_boundary_vertices'] = all(before['vertices'][i] == after['vertices'][i]
                                       for i in range(len(mesh.vertices)) if i not in boundary_set)
unchanged['edge_uv_u'] = all(a[0] == b[0] for a, b in zip(before['uv_layers']['TileEdge'], after['uv_layers']['TileEdge']))
assert all(unchanged.values()), unchanged
normal_error = max(angular_error(a, b) for a, b in zip(after['loop_normals'], before['loop_normals']))
assert normal_error < 0.01
for loop, uv in zip(mesh.loops, mesh.uv_layers['TileEdge'].data):
    assert abs(uv.uv.y - (mesh.vertices[loop.vertex_index].co.z-front_z)/depth) < 1e-6
spatial_outline = {}
for loop, uv in zip(mesh.loops, mesh.uv_layers['OutlineSmoothNormal'].data):
    key = tuple(round(c, 6) for c in mesh.vertices[loop.vertex_index].co)
    if key in spatial_outline:
        assert max(abs(a-b) for a, b in zip(uv.uv, spatial_outline[key])) < 1e-6
    else:
        spatial_outline[key] = list(uv.uv)
geometry = check_closed_convex(mesh)
report = {
    'source': str(source), 'source_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
    'candidate': str(args.output_blend), 'blender_version': bpy.app.version_string,
    'before_back_depth_ratio': old_back_ratio, 'after_back_depth_ratio': args.back_ratio,
    'before_white_depth_ratio': 1-old_back_ratio, 'after_white_depth_ratio': 1-args.back_ratio,
    'ratio_definition': 'Complete front-to-back depth; rear portion includes rear bevel.',
    'before_boundary_blender_z': boundary_z, 'after_boundary_blender_z': target_z,
    'before_boundary_unity_z': boundary_z*0.01, 'after_boundary_unity_z': target_z*0.01,
    'dimensions_blender': [b-a for a, b in before_bounds],
    'expected_dimensions_unity': [(b-a)*0.01 for a, b in before_bounds],
    'boundary_vertices': len(boundary_vertices),
    'moved_vertices': sum(a != b for a, b in zip(before['vertices'], after['vertices'])),
    'total_vertices': len(mesh.vertices),
    'boundary_side_depth_uv_loops': len(boundary_loops),
    'updated_side_depth_uv_loops': sum(a != b for a, b in zip(before['uv_layers']['TileEdge'], after['uv_layers']['TileEdge'])),
    'unchanged': unchanged,
    'max_polygon_normal_error_degrees': max_face_normal_error,
    'max_lighting_normal_error_degrees': normal_error,
    'normal_policy': 'Kept stored custom normals without re-encoding; corner-space float error below 0.01 degree, face planes unchanged.',
    'outline_policy': 'UV4 oct normals unchanged, including across duplicate spatial vertices.',
    'uv_policy': 'Face/back UV unchanged; edge U unchanged; boundary edge V follows normalized depth.',
    'geometry': geometry,
}
args.output_blend.parent.mkdir(parents=True, exist_ok=True)
bpy.context.preferences.filepaths.save_version = 0
bpy.ops.wm.save_as_mainfile(filepath=str(args.output_blend), check_existing=False, relative_remap=False)
report['candidate_sha256'] = hashlib.sha256(args.output_blend.read_bytes()).hexdigest()
args.report.parent.mkdir(parents=True, exist_ok=True)
args.report.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print('TILE_BACK_BAND_VALIDATED', json.dumps(report, ensure_ascii=False))
