"""Daily export of the currently opened, edited source; never regenerates it.

Accepts -- --output <path.fbx> for an isolated import test. Default output is
next to the opened .blend, with the same basename.
"""
import argparse
import bpy
import sys
from pathlib import Path

parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
source=Path(bpy.data.filepath);assert source.suffix=='.blend'
obj=bpy.data.objects['Cube'];mesh=obj.data
assert obj.type=='MESH' and mesh.name=='Cube' and obj.parent is None and not obj.modifiers
assert tuple(obj.location)==tuple(obj.rotation_euler)==(0,0,0) and tuple(obj.scale)==(1,1,1)
assert [uv.name for uv in mesh.uv_layers]==['TileFace','TileBack','TileEdge','OutlineSmoothNormal']
assert len(mesh.materials)==1 and mesh.materials[0].name=='Material'
assert len(mesh.color_attributes)==1 and mesh.color_attributes[0].data_type=='FLOAT_COLOR'
assert all(abs(c.color[3]-1)<1e-6 for c in mesh.color_attributes[0].data)
assert mesh.has_custom_normals
for item in bpy.context.selected_objects:item.select_set(False)
obj.select_set(True);bpy.context.view_layer.objects.active=obj
destination=args.output or source.with_suffix('.fbx');destination.parent.mkdir(parents=True,exist_ok=True)
bpy.ops.export_scene.fbx(filepath=str(destination),use_selection=True,object_types={'MESH'},
    global_scale=1,apply_unit_scale=True,apply_scale_options='FBX_SCALE_NONE',use_space_transform=True,
    bake_space_transform=False,axis_forward='-Z',axis_up='Y',use_mesh_modifiers=False,mesh_smooth_type='OFF',
    use_tspace=False,use_triangles=False,colors_type='LINEAR',prioritize_active_color=True,bake_anim=False,
    add_leaf_bones=False,use_custom_props=False,path_mode='AUTO',embed_textures=False)
print('EDITED_BLEND_EXPORTED',destination)
