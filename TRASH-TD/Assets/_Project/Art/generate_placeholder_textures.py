import os
import struct
import zlib
import uuid

def create_png(width, height, get_pixel):
    """Generates an uncompressed RGBA PNG in pure Python with standard zlib."""
    raw_data = bytearray()
    for y in range(height):
        raw_data.append(0)  # Filter type 0 (None)
        for x in range(width):
            r, g, b, a = get_pixel(x, y)
            raw_data.extend((r, g, b, a))
    
    compressed = zlib.compress(bytes(raw_data))
    
    png = bytearray(b'\x89PNG\r\n\x1a\n')
    
    # IHDR chunk
    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)
    png.extend(struct.pack('>I', len(ihdr)))
    png.extend(b'IHDR')
    png.extend(ihdr)
    png.extend(struct.pack('>I', zlib.crc32(b'IHDR' + ihdr)))
    
    # IDAT chunk
    png.extend(struct.pack('>I', len(compressed)))
    png.extend(b'IDAT')
    png.extend(compressed)
    png.extend(struct.pack('>I', zlib.crc32(b'IDAT' + compressed)))
    
    # IEND chunk
    png.extend(struct.pack('>I', 0))
    png.extend(b'IEND')
    png.extend(struct.pack('>I', zlib.crc32(b'IEND')))
    
    return bytes(png)

def create_meta(guid):
    return f"""fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 64
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationMethod: 0
  spriteTessellationDetail: -1
  spriteGeometrySubdivision: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  platformSettings: []
"""

# Pixel generators for 64x64 sprites
def guard_pixel(x, y):
    # Orange shield with dark outline
    cx, cy = 32, 32
    dx = abs(x - cx)
    dy = y - cy
    if dx <= 22 and dy >= -22 and (dy <= 10 or (dy > 10 and dy <= 24 - dx * 0.8)):
        if dx >= 20 or dy <= -20 or (dy > 8 and dy >= 22 - dx * 0.8):
            return 40, 20, 10, 255 # Border
        # Inner sword
        if dx <= 3 and dy >= -18 and dy <= 16:
            return 240, 240, 255, 255
        return 230, 120, 30, 255 # Orange fill
    return 0, 0, 0, 0

def defender_pixel(x, y):
    # Blue heavy tower shield
    dx = abs(x - 32)
    dy = abs(y - 32)
    if dx <= 24 and dy <= 24:
        if dx >= 22 or dy >= 22:
            return 20, 30, 60, 255
        if dx <= 4 or dy <= 4:
            return 100, 180, 255, 255 # Highlight cross
        return 40, 100, 200, 255 # Blue fill
    return 0, 0, 0, 0

def sniper_pixel(x, y):
    # Green crosshair / scope
    dx = x - 32
    dy = y - 32
    dist_sq = dx * dx + dy * dy
    if 18*18 <= dist_sq <= 23*23:
        return 50, 220, 100, 255 # Ring
    if dist_sq <= 4*4:
        return 255, 255, 255, 255 # Center pip
    if (abs(dx) <= 2 and abs(dy) <= 26) or (abs(dy) <= 2 and abs(dx) <= 26):
        return 50, 220, 100, 255 # Crosshair hairs
    return 0, 0, 0, 0

def caster_pixel(x, y):
    # Purple diamond magic rune
    dx = abs(x - 32)
    dy = abs(y - 32)
    if dx + dy <= 26:
        if dx + dy >= 23:
            return 80, 20, 100, 255
        if dx + dy <= 10:
            return 240, 200, 255, 255 # Bright core
        return 160, 60, 220, 255 # Purple fill
    return 0, 0, 0, 0

def medic_pixel(x, y):
    # Medical cross on soft rounded green
    dx = abs(x - 32)
    dy = abs(y - 32)
    dist_sq = dx*dx + dy*dy
    if dist_sq <= 24*24:
        # Cross check
        if (dx <= 5 and dy <= 16) or (dy <= 5 and dx <= 16):
            return 255, 255, 255, 255 # White cross
        return 35, 170, 120, 255 # Teal/green circle
    return 0, 0, 0, 0

def grunt_pixel(x, y):
    # Sludge blob
    dx = x - 32
    dy = y - 32
    dist_sq = dx*dx + (dy*1.2)*(dy*1.2)
    if dist_sq <= 22*22:
        if dist_sq >= 19*19:
            return 30, 45, 15, 255
        # Red eyes
        if dy in (-4, -3, -2) and (x in (25, 26, 38, 39)):
            return 255, 50, 50, 255
        return 90, 140, 40, 255 # Olive sludge
    return 0, 0, 0, 0

def rusher_pixel(x, y):
    # Fast yellow arrow/chevron
    dx = abs(x - 32)
    dy = y - 32
    if dx <= 22 and dy - dx >= -18 and dy - dx <= 12:
        if dy - dx <= -15 or dy - dx >= 9 or dx >= 20:
            return 60, 40, 0, 255
        return 250, 200, 20, 255 # Yellow speed chevron
    return 0, 0, 0, 0

def tank_pixel(x, y):
    # Armored grey block
    dx = abs(x - 32)
    dy = abs(y - 32)
    if dx <= 26 and dy <= 22:
        if dx >= 23 or dy >= 19:
            return 30, 30, 35, 255
        if dy <= -10:
            return 180, 50, 50, 255 # Red visor slit
        return 90, 95, 105, 255 # Steel grey
    return 0, 0, 0, 0

def caster_enemy_pixel(x, y):
    # Violet toxic cloud
    dx = x - 32
    dy = y - 32
    dist_sq = dx*dx + dy*dy
    if dist_sq <= 23*23:
        if (x + y) % 6 == 0:
            return 190, 80, 240, 255
        if dy in (-2, -1) and abs(dx) in (6, 7, 8):
            return 120, 255, 150, 255 # Acid green eyes
        return 110, 40, 150, 255
    return 0, 0, 0, 0

def flyer_pixel(x, y):
    # Cyan winged creature
    dx = abs(x - 32)
    dy = abs(y - 32)
    if (dy <= 8 and dx <= 26) or (dx <= 8 and dy <= 22):
        if dx >= 23 or dy >= 19:
            return 20, 50, 70, 255
        return 60, 200, 230, 255 # Cyan wings
    return 0, 0, 0, 0

def tile_lowground_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 50, 100, 40, 255
    if (x + y) % 8 == 0: return 90, 160, 70, 255
    return 80, 145, 65, 255

def tile_highground_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 60, 70, 90, 255
    if (x + y) % 8 == 0: return 110, 125, 155, 255
    return 95, 110, 140, 255

def tile_blocked_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 40, 40, 40, 255
    if (x - y) % 12 < 4: return 200, 80, 40, 255 # Warning stripes
    return 55, 55, 60, 255

def tile_enemypath_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 110, 90, 60, 255
    if (x * y) % 11 == 0: return 160, 135, 95, 255
    return 145, 120, 85, 255

def tile_spawn_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 180, 30, 30, 255
    dx, dy = x - 32, y - 32
    if dx*dx + dy*dy <= 18*18: return 230, 50, 50, 255
    return 140, 35, 35, 255

def tile_exit_pixel(x, y):
    if x in (0, 63) or y in (0, 63): return 30, 100, 180, 255
    dx, dy = x - 32, y - 32
    if dx*dx + dy*dy <= 18*18: return 60, 170, 255, 255
    return 35, 90, 140, 255

TEXTURES = [
    ("tex_op_guard.png", "a1111111111111111111111111111101", guard_pixel),
    ("tex_op_defender.png", "a1111111111111111111111111111102", defender_pixel),
    ("tex_op_sniper.png", "a1111111111111111111111111111103", sniper_pixel),
    ("tex_op_caster.png", "a1111111111111111111111111111104", caster_pixel),
    ("tex_op_medic.png", "a1111111111111111111111111111105", medic_pixel),
    ("tex_enemy_grunt.png", "b2222222222222222222222222222201", grunt_pixel),
    ("tex_enemy_rusher.png", "b2222222222222222222222222222202", rusher_pixel),
    ("tex_enemy_tank.png", "b2222222222222222222222222222203", tank_pixel),
    ("tex_enemy_caster.png", "b2222222222222222222222222222204", caster_enemy_pixel),
    ("tex_enemy_flyer.png", "b2222222222222222222222222222205", flyer_pixel),
    ("tex_tile_lowground.png", "c3333333333333333333333333333301", tile_lowground_pixel),
    ("tex_tile_highground.png", "c3333333333333333333333333333302", tile_highground_pixel),
    ("tex_tile_blocked.png", "c3333333333333333333333333333303", tile_blocked_pixel),
    ("tex_tile_enemypath.png", "c3333333333333333333333333333304", tile_enemypath_pixel),
    ("tex_tile_spawn.png", "c3333333333333333333333333333305", tile_spawn_pixel),
    ("tex_tile_exit.png", "c3333333333333333333333333333306", tile_exit_pixel),
]

art_dir = r"c:\Users\raigo\Desktop\GAME DEV\TRASH TD\TRASH-TD\TRASH-TD\Assets\_Project\Art"
os.makedirs(art_dir, exist_ok=True)

for filename, guid, generator in TEXTURES:
    png_path = os.path.join(art_dir, filename)
    meta_path = png_path + ".meta"
    
    png_data = create_png(64, 64, generator)
    with open(png_path, "wb") as f:
        f.write(png_data)
        
    meta_data = create_meta(guid)
    with open(meta_path, "w", encoding="utf-8") as f:
        f.write(meta_data)
        
    print(f"Generated {filename} ({len(png_data)} bytes)")

print("All placeholder textures and meta files generated successfully!")
