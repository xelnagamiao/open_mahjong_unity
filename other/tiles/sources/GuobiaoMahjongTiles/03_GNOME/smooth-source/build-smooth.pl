#! /usr/bin/perl

#  Copyright (C) 2004-2014  Jaye Evins <evins@snaught.com>
#
#  This file is part of the Smooth tile set renderer for Gnome Mahjongg
#  (smooth-tileset).
#
#  smooth-tileset is free software: you can redistribute it and/or modify
#  it under the terms of the GNU General Public License as published by
#  the Free Software Foundation, either version 2 of the License, or
#  (at your option) any later version.
#
#  smooth-tileset is distributed in the hope that it will be useful,
#  but WITHOUT ANY WARRANTY; without even the implied warranty of
#  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
#  GNU General Public License for more details.
#
#  You should have received a copy of the GNU General Public License
#  along with smooth-tileset.  If not, see <http://www.gnu.org/licenses/>.

use File::Basename;

#####################################
$povray = "povray";
$face_dir = "face-images";
$tmp_dir = "tmp";
$out = "smooth.png";
$tile_width = 96;
$tile_height = 132;
#####################################

#----------------------------------------------------------------------------
# Begin Main Program
#----------------------------------------------------------------------------

@faces = glob "$face_dir/*.png";

mkdir $tmp_dir;


#---------------------------------------
# Create bump_map mask files from faces
#---------------------------------------
foreach $face (@faces) {

    $base = basename($face, ".png");
    $mask = "$tmp_dir/$base-mask.png";

    print "Creating bump_map file: $mask...";
    system( "convert -alpha extract -negate $face -blur 5x3 $mask" ) == 0 || die "convert"; 
    print "[done]\n";
	
}

#---------------------------------------
# Now render tiles at different ambient
# light levels.
#---------------------------------------

foreach $ambient ( 0.4, 0.6 ) {

    foreach $face (@faces) {

	$base  = basename($face, ".png");

	print "Rendering $base-$ambient...";
	create_pov_src( $base, $ambient );
	render_pov_scene( $base );
	print "[done]\n";

    }
}


#---------------------------------------
# Join images together in array
#---------------------------------------
foreach $ambient ( 0.4, 0.6 ) {

    my @tiles;
    my $row = "$tmp_dir/row-$ambient.png";

    print "Creating row image: $row...";

    foreach $face (@faces) {
	$base = basename($face, ".png");
	push @tiles, "$tmp_dir/$base-$ambient.png";
    }

    push @rows, $row;

    system( "convert +append @tiles $row" ) == 0 || die "convert";

    print "[done]\n";
}

print "Creating final image: $out...";
system( "convert -append @rows $out\n" ) == 0 || die "convert";
print "[done]\n";



#----------------------------------------------------------------------------
# Render POV-Ray Scene
#----------------------------------------------------------------------------
sub render_pov_scene{
    my($base) = @_;

    my $scene = "$tmp_dir/$base-$ambient.pov";
    my $ini   = "$tmp_dir/$base-$ambient.ini";
    my $img   = "$tmp_dir/$base-$ambient.png";

    open( POV_INI, ">$ini" );

    print POV_INI ";; High quality render (can be very slow)\n",
                  "Quality=9\n",
                  "Antialias=On\n",
                  "Antialias_Threshold=0.3\n",
                  "Antialias_Depth=5\n",
                  "Jitter=On\n",
                  "Jitter_Amount=0.5\n",
                  ";;\n",
                  "Bounding=On\n",
                  "Bounding_Threshold=3\n",
                  "Display=On\n",
                  "Width=$tile_width\n",
                  "Height=$tile_height\n",
                  "Input_File_Name=$scene\n",
                  "Output_File_Type=N\n",
                  "Output_Alpha=On\n",
                  "Output_File_Name=$img\n";

    close( POV_INI );

    system( "$povray $ini" ) == 0 || die $povray;
}

#----------------------------------------------------------------------------
# Create POV-Ray Scene Source
#----------------------------------------------------------------------------
sub create_pov_src {
    my($base, $ambient) = @_;

    my $face  = "$face_dir/$base.png";
    my $mask  = "$tmp_dir/$base-mask.png";
    my $scene = "$tmp_dir/$base-$ambient.pov";

    open( POV_FILE, ">$scene" );
    print POV_FILE <<EOF;
////////////////////////////////////////////////////////////////////////////
//                         DO NOT EDIT BY HAND!
//
// Automatically created by $0 
////////////////////////////////////////////////////////////////////////////

#version 3.6;

global_settings {
    assumed_gamma 2.2
}

#include "colors.inc"
#include "textures.inc"
#include "stones.inc"
#include "woods.inc"

#declare Width = 128;
#declare Height = 175;

#declare W = Width;
#declare H = Height;
#declare D = 150;


#declare TileTexture =
  texture {
	pigment { color red 0.992 green 0.992 blue 0.898 }
	normal  { bumps 0.075 scale 35 }
	finish  { ambient ${ambient} diffuse .6 specular .75 roughness .01 }
  }

#declare TilePaint =
  texture {
	pigment {
		image_map {png "$face" once interpolate 2 }
		translate <-0.5,-0.5,0>
		scale <W,H,1>
	}
	normal {
		bump_map { png "$mask" once interpolate 2 bump_size 10 }
		translate <-0.5,-0.5,-0>
		scale <W,H,D>
	}
	finish { ambient ${ambient} diffuse .6 specular .75 roughness .01 }
  }


camera {
   orthographic
   location  <-10000, -10000, -100000>
   look_at <0,0,0>
   up <0,Height*1.085,0>
   right <Width*1.105,0,0>
}

light_source { <-600, 600, -900> colour White } // Key Light source

light_source { <900, 100, 1.5> colour Gray20 } // Back Light source

#declare Tile =
	superellipsoid {
		<0.1, 0.1>
		scale <W/2, H/2, D/2>
	}

// Actual Tile
object { Tile
   texture {TileTexture}
   texture {TilePaint}
}


////////////////////////////////////////////////////////////////////////////
EOF
    close( POV_FILE );

}

