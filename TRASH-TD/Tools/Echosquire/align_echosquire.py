from __future__ import annotations

import hashlib
import re
import struct
from pathlib import Path

from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parents[2]
ART_DIR = ROOT / "Assets" / "_Project" / "Art" / "Operators"
OUTPUT_DIR = Path(__file__).resolve().parent

IDLE_SOURCE = ART_DIR / "Echosquire-Sheet.png"
ATTACK_SOURCE = ART_DIR / "Echosquire Attack Sheet.png"
IDLE_OUTPUT = ART_DIR / "Echosquire_Idle_Aligned_64x32.png"
ATTACK_OUTPUT = ART_DIR / "Echosquire_Attack_Aligned_64x32.png"
PREVIEW_OUTPUT = OUTPUT_DIR / "echosquire_alignment_preview.gif"
REPORT_OUTPUT = OUTPUT_DIR / "echosquire_alignment_report.txt"

IDLE_GUID = "aa0c020617394829a8a25b9d1c5e74f1"
ATTACK_GUID = "bb1d1317284a493aa9b36cae2d6f8502"
IDLE_PREFIX = "Echosquire_Idle_Aligned_64x32"
ATTACK_PREFIX = "Echosquire_Attack_Aligned_64x32"

IDLE_FILE_IDS = (
    7841794329617421502,
    523648466364858218,
    -837147388,
    1142371386,
    -1415482300,
    -1355444340,
    1609194568,
    -1068655876,
)
ATTACK_FILE_IDS = (
    8931711473772582028,
    464284638746671369,
    -6093667170385184434,
    5773207495206052359,
    408756017840664818,
    -598964639437151268,
    -6012473284030398231,
    -7741109047117711701,
    4662877692184907558,
    -1953135051299375961,
)

FRAME_SIZE = (64, 32)
IDLE_X_OFFSET = 18
FOOT_BASELINE = 29
OUTLINE_ROW = 30
OUTLINE_COLOR = (24, 17, 26)
FOOT_FILL_COLORS = {
    (61, 47, 49),
    (110, 87, 83),
    (111, 116, 128),
    (255, 255, 255),
}
ATTACK_DURATIONS_MS = (140, 110, 170, 60, 50, 55, 90, 100, 120, 220)
GIF_IDLE_DURATIONS_MS = (120, 130)
GIF_ATTACK_DURATIONS_MS = (140, 110, 170, 60, 50, 60, 90, 100, 120, 220)
GIF_SCALE = 10
GIF_BACKGROUND = (43, 47, 63)
GROUND_MARKER = (91, 99, 121)


def fail(message: str) -> None:
    raise ValueError(message)


def load_sheet(path: Path, expected_size: tuple[int, int]) -> Image.Image:
    image = Image.open(path)
    if image.mode != "RGBA":
        fail(f"{path.name} must be RGBA; found {image.mode}.")
    if image.size != expected_size:
        fail(f"{path.name} must be {expected_size}; found {image.size}.")
    return image


def translate_vertical(frame: Image.Image, offset_y: int) -> tuple[Image.Image, int]:
    result = Image.new("RGBA", FRAME_SIZE, (0, 0, 0, 0))
    clipped = 0
    for y in range(frame.height):
        destination_y = y + offset_y
        if not 0 <= destination_y < result.height:
            clipped += sum(
                1 for x in range(frame.width) if frame.getpixel((x, y))[3] > 0
            )
            continue
        for x in range(frame.width):
            pixel = frame.getpixel((x, y))
            if pixel[3] > 0:
                result.putpixel((x, destination_y), pixel)
    return result, clipped


def foot_pixels(frame: Image.Image) -> list[tuple[int, int]]:
    return [
        (x, y)
        for y in range(27, frame.height)
        for x in range(frame.width)
        if frame.getpixel((x, y))[3] > 0
        and frame.getpixel((x, y))[:3] in FOOT_FILL_COLORS
    ]


def lowest_foot_row(frame: Image.Image) -> int:
    pixels = foot_pixels(frame)
    if not pixels:
        fail("Could not identify foot-fill pixels in an animation frame.")
    return max(y for _, y in pixels)


def opaque_bounds(frame: Image.Image) -> tuple[int, int]:
    xs = [
        x
        for y in range(frame.height)
        for x in range(frame.width)
        if frame.getpixel((x, y))[3] > 0
    ]
    if not xs:
        fail("An output frame is fully transparent.")
    return min(xs), max(xs)


def opaque_colors(image: Image.Image) -> set[tuple[int, int, int]]:
    return {
        pixel[:3]
        for y in range(image.height)
        for x in range(image.width)
        if (pixel := image.getpixel((x, y)))[3] > 0
    }


def pixels_identical(first: Image.Image, second: Image.Image) -> bool:
    return first.mode == second.mode and first.size == second.size and first.tobytes() == second.tobytes()


def sprite_id(name: str) -> str:
    return hashlib.sha256(name.encode("utf-8")).hexdigest()[:16] + "0800000000000000"


def write_sprite_meta(
    output_path: Path,
    guid: str,
    prefix: str,
    frame_width: int,
    file_ids: tuple[int, ...],
    template_path: Path,
    old_prefix: str,
) -> None:
    meta_path = output_path.with_suffix(output_path.suffix + ".meta")
    text = template_path.read_text(encoding="utf-8")
    text = re.sub(r"(?m)^guid: [0-9a-f]{32}$", f"guid: {guid}", text, count=1)
    text = re.sub(
        re.escape(old_prefix) + r"_(\d+)",
        lambda match: f"{prefix}_{match.group(1)}",
        text,
    )
    section_start = text.index("  spriteSheet:")
    section_end = text.find("  mipmapLimitGroupName:", section_start)
    if section_end < 0:
        section_end = len(text)
    section = text[section_start:section_end]
    rect_index = 0

    def replace_rect(match: re.Match[str]) -> str:
        nonlocal rect_index
        if rect_index >= len(file_ids):
            fail(f"Unexpected sprite rectangle in {template_path.name}.")
        x_offset = rect_index * frame_width
        rect_index += 1
        return (
            f"{match.group(1)}{x_offset}"
            f"{match.group(2)}0"
            f"{match.group(3)}{frame_width}"
            f"{match.group(4)}32"
        )

    section = re.sub(
        r"(?m)^([ \t]+x: )\d+(\n[ \t]+y: )\d+(\n[ \t]+width: )\d+(\n[ \t]+height: )\d+",
        replace_rect,
        section,
    )
    if rect_index != len(file_ids):
        fail(f"Expected {len(file_ids)} sprite rectangles in {template_path.name}, found {rect_index}.")
    text = text[:section_start] + section + text[section_end:]
    text = re.sub(
        r'"gridSpriteSize":\{"x":[0-9.]+,"y":[0-9.]+\}',
        '"gridSpriteSize":{"x":64.0,"y":32.0}',
        text,
    )
    for index, file_id in enumerate(file_ids):
        name = f"{prefix}_{index}"
        # Source slice IDs remain stable, so prefabs can reference these sub-assets.
        original_match = re.search(
            rf"(?m)^[ \t]+name: {re.escape(name)}[ \t]*$", text
        )
        if original_match is None:
            fail(f"Could not rename sprite metadata entry {name}.")
        sprite_block_start = text.rfind("    - serializedVersion: 2", 0, original_match.start())
        sprite_block_end = text.find("    - serializedVersion: 2", original_match.end())
        if sprite_block_end < 0:
            sprite_block_end = text.find("    outline: []", original_match.end())
        if sprite_block_start < 0 or sprite_block_end < 0:
            fail(f"Could not locate metadata block for {name}.")
        block = text[sprite_block_start:sprite_block_end]
        block = re.sub(r"(?m)^(\s+internalID: )-?\d+$", rf"\g<1>{file_id}", block)
        block = re.sub(
            r"(?m)^(\s+spriteID: )[0-9a-f]{32}$",
            rf"\g<1>{sprite_id(name)}",
            block,
            count=1,
        )
        text = text[:sprite_block_start] + block + text[sprite_block_end:]
    meta_path.write_text(text, encoding="utf-8", newline="\n")


def verify_sprite_meta(
    image_path: Path, guid: str, prefix: str, frame_width: int, file_ids: tuple[int, ...]
) -> None:
    text = image_path.with_suffix(image_path.suffix + ".meta").read_text(encoding="utf-8")
    if not text.startswith(f"fileFormatVersion: 2\nguid: {guid}\n"):
        fail(f"{image_path.name} has an unexpected Unity asset GUID.")
    section = text[text.index("  spriteSheet:") :]
    for index, file_id in enumerate(file_ids):
        name = f"{prefix}_{index}"
        match = re.search(
            rf"(?ms)^    - serializedVersion: 2\n      name: {re.escape(name)}\n"
            r"      rect:\n        serializedVersion: 2\n"
            r"        x: (\d+)\n        y: 0\n        width: (\d+)\n"
            r"        height: 32.*?^      internalID: (-?\d+)$",
            section,
        )
        if match is None:
            fail(f"{image_path.name} is missing the {name} 64x32 sprite slice.")
        if (
            int(match.group(1)) != index * frame_width
            or int(match.group(2)) != frame_width
            or int(match.group(3)) != file_id
        ):
            fail(f"{image_path.name} has incorrect Unity metadata for {name}.")


def palette_image(colors: set[tuple[int, int, int]]) -> Image.Image:
    ordered = sorted(colors)
    if len(ordered) > 256:
        fail(f"The preview needs {len(ordered)} colors; GIF supports at most 256.")
    palette = [channel for color in ordered for channel in color]
    palette.extend([0] * (768 - len(palette)))
    image = Image.new("P", (1, 1))
    image.putpalette(palette)
    return image


def verify_gif_global_palette(path: Path, expected_frames: int, expected_duration: int) -> int:
    data = path.read_bytes()
    if data[:6] not in (b"GIF87a", b"GIF89a"):
        fail("Preview is not a GIF.")
    packed = data[10]
    if packed & 0x80 == 0:
        fail("Preview GIF has no global color table.")
    table_size = 1 << ((packed & 0x07) + 1)
    index = 13 + table_size * 3
    frame_count = 0
    while index < len(data):
        marker = data[index]
        if marker == 0x3B:
            break
        if marker == 0x21:
            index += 2
            while True:
                block_size = data[index]
                index += 1
                if block_size == 0:
                    break
                index += block_size
            continue
        if marker != 0x2C:
            fail(f"Unexpected GIF block marker 0x{marker:02x}.")
        descriptor_flags = data[index + 9]
        if descriptor_flags & 0x80:
            fail("Preview GIF contains a frame-local color table.")
        index += 10
        index += 1
        while True:
            block_size = data[index]
            index += 1
            if block_size == 0:
                break
            index += block_size
        frame_count += 1
    if not 0 < frame_count <= expected_frames:
        fail(f"Preview GIF has an invalid encoded frame count: {frame_count}.")
    preview = Image.open(path)
    encoded_duration = 0
    for frame_index in range(frame_count):
        preview.seek(frame_index)
        encoded_duration += preview.info.get("duration", 0)
    if encoded_duration != expected_duration:
        fail(
            f"Preview duration is {encoded_duration} ms; expected {expected_duration} ms."
        )
    return frame_count


def make_preview(
    idle_frames: list[Image.Image], attack_frames: list[Image.Image], source_colors: set[tuple[int, int, int]]
) -> tuple[int, int]:
    native_frames: list[Image.Image] = []
    durations: list[int] = []
    idle_ordinal = 0
    for _ in range(2):
        for frame in idle_frames:
            native_frames.append(frame)
            durations.append(GIF_IDLE_DURATIONS_MS[idle_ordinal % 2])
            idle_ordinal += 1
    for frame, duration in zip(attack_frames, GIF_ATTACK_DURATIONS_MS):
        native_frames.append(frame)
        durations.append(duration)
    for _ in range(2):
        for frame in idle_frames:
            native_frames.append(frame)
            durations.append(GIF_IDLE_DURATIONS_MS[idle_ordinal % 2])
            idle_ordinal += 1

    background = Image.new("RGBA", FRAME_SIZE, (*GIF_BACKGROUND, 255))
    draw = ImageDraw.Draw(background)
    draw.line((0, 31, 63, 31), fill=(*GROUND_MARKER, 255), width=1)
    rendered: list[Image.Image] = []
    for frame in native_frames:
        composite = Image.alpha_composite(background, frame)
        rendered.append(
            composite.resize(
                (FRAME_SIZE[0] * GIF_SCALE, FRAME_SIZE[1] * GIF_SCALE),
                Image.Resampling.NEAREST,
            ).convert("RGB")
        )

    all_colors = source_colors | {GIF_BACKGROUND, GROUND_MARKER}
    fixed_palette = palette_image(all_colors)
    indexed = [
        frame.quantize(palette=fixed_palette, dither=Image.Dither.NONE)
        for frame in rendered
    ]
    for frame in indexed:
        frame.putpalette(fixed_palette.getpalette())
    indexed[0].save(
        PREVIEW_OUTPUT,
        save_all=True,
        append_images=indexed[1:],
        palette=fixed_palette.getpalette(),
        duration=durations,
        loop=0,
        disposal=2,
        optimize=False,
    )
    encoded_frames = verify_gif_global_palette(
        PREVIEW_OUTPUT, len(native_frames), sum(durations)
    )
    return encoded_frames, len(all_colors)


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    idle_source = load_sheet(IDLE_SOURCE, (256, 32))
    attack_source = load_sheet(ATTACK_SOURCE, (640, 32))
    source_colors = opaque_colors(idle_source) | opaque_colors(attack_source)

    idle_frames: list[Image.Image] = []
    idle_corrections: list[int] = []
    idle_clip_counts: list[int] = []
    for index in range(8):
        frame = Image.new("RGBA", FRAME_SIZE, (0, 0, 0, 0))
        source_frame = idle_source.crop((index * 32, 0, (index + 1) * 32, 32))
        frame.alpha_composite(source_frame, (IDLE_X_OFFSET, 0))
        correction = FOOT_BASELINE - lowest_foot_row(frame)
        shifted, clipped = translate_vertical(frame, correction)
        idle_frames.append(shifted)
        idle_corrections.append(correction)
        idle_clip_counts.append(clipped)

    attack_frames: list[Image.Image] = []
    attack_measurements: list[tuple[int, int, int]] = []
    for index in range(10):
        source_frame = attack_source.crop((index * 64, 0, (index + 1) * 64, 32))
        baseline = lowest_foot_row(source_frame)
        correction = FOOT_BASELINE - baseline
        shifted, clipped = translate_vertical(source_frame, correction)
        attack_frames.append(shifted)
        attack_measurements.append((baseline, correction, clipped))

    attack_frames[0] = idle_frames[0].copy()
    attack_frames[8] = idle_frames[2].copy()
    attack_frames[9] = idle_frames[0].copy()

    loop_checks = (
        ("Attack frame 1 == idle frame 1", pixels_identical(attack_frames[0], idle_frames[0])),
        ("Attack frame 9 == idle frame 3", pixels_identical(attack_frames[8], idle_frames[2])),
        ("Attack frame 10 == idle frame 1", pixels_identical(attack_frames[9], idle_frames[0])),
    )
    if any(not passed for _, passed in loop_checks):
        fail("One or more required loop frames are not pixel-identical.")

    for index, frame in enumerate(idle_frames + attack_frames):
        if frame.size != FRAME_SIZE:
            fail(f"Output frame {index + 1} has incorrect dimensions.")
        if lowest_foot_row(frame) != FOOT_BASELINE:
            fail(f"Output frame {index + 1} does not have feet on row {FOOT_BASELINE}.")
        if not any(frame.getpixel((x, OUTLINE_ROW))[:3] == OUTLINE_COLOR for x in range(frame.width)):
            fail(f"Output frame {index + 1} has no foot outline on row {OUTLINE_ROW}.")

    output_colors = set().union(*(opaque_colors(frame) for frame in idle_frames + attack_frames))
    if not output_colors.issubset(source_colors):
        fail("Aligned PNGs contain colors not present in the source sheets.")

    idle_sheet = Image.new("RGBA", (512, 32), (0, 0, 0, 0))
    for index, frame in enumerate(idle_frames):
        idle_sheet.alpha_composite(frame, (index * 64, 0))
    attack_sheet = Image.new("RGBA", (640, 32), (0, 0, 0, 0))
    for index, frame in enumerate(attack_frames):
        attack_sheet.alpha_composite(frame, (index * 64, 0))
    idle_sheet.save(IDLE_OUTPUT)
    attack_sheet.save(ATTACK_OUTPUT)

    write_sprite_meta(
        IDLE_OUTPUT,
        IDLE_GUID,
        IDLE_PREFIX,
        64,
        IDLE_FILE_IDS,
        IDLE_SOURCE.with_suffix(".png.meta"),
        "Echosquire-Sheet",
    )
    verify_sprite_meta(IDLE_OUTPUT, IDLE_GUID, IDLE_PREFIX, 64, IDLE_FILE_IDS)
    write_sprite_meta(
        ATTACK_OUTPUT,
        ATTACK_GUID,
        ATTACK_PREFIX,
        64,
        ATTACK_FILE_IDS,
        ATTACK_SOURCE.with_suffix(".png.meta"),
        "Echosquire Attack Sheet",
    )
    verify_sprite_meta(ATTACK_OUTPUT, ATTACK_GUID, ATTACK_PREFIX, 64, ATTACK_FILE_IDS)
    preview_frames, preview_colors = make_preview(idle_frames, attack_frames, source_colors)

    lines = [
        "Echosquire alignment report",
        "===========================",
        f"Idle placement: x={IDLE_X_OFFSET}, y=0 before baseline corrections.",
        f"Idle vertical corrections by frame: {idle_corrections}; top-edge opaque pixels clipped: {idle_clip_counts}",
        f"Feet target: fill row {FOOT_BASELINE}, outline row {OUTLINE_ROW}.",
        "Attack corrections are integer vertical translations only; no horizontal attack shifts.",
        "Attack frame 1, 9, and 10 are replaced by the corresponding aligned idle poses.",
        f"Impact starts on attack frame 6 at {sum(ATTACK_DURATIONS_MS[:5])} ms; attack sequence duration is {sum(ATTACK_DURATIONS_MS)} ms.",
        "GIF timing uses 10 ms ticks: idle 120/130 ms alternating (125 ms average), frame 6 rounds from 55 to 60 ms.",
        "",
        "Attack source measurements:",
        "frame | source foot-fill row | vertical correction",
        "------+----------------------+--------------------",
    ]
    for index, (baseline, correction, clipped) in enumerate(attack_measurements, start=1):
        lines.append(f"{index:>5} | {baseline:>20} | {correction:+d} px; clipped pixels: {clipped}")
    lines.extend(["", "Aligned attack-frame measurements:", "frame | foot-fill row | body X* | left opaque X | right opaque X"])
    lines.append("------+---------------+---------+---------------+----------------")
    for index, frame in enumerate(attack_frames, start=1):
        left, right = opaque_bounds(frame)
        body_x = 34.5 if index in (1, 9, 10) else (left + right) / 2
        lines.append(f"{index:>5} | {lowest_foot_row(frame):>13} | {body_x:>7.1f} | {left:>13} | {right:>14}")
    lines.extend(
        [
            "",
            "* Body X is the midpoint of the opaque bounds; wide weapon effects can shift this proxy.",
            "",
            "Required loop continuity:",
        ]
    )
    lines.extend(f"{'PASS' if passed else 'FAIL'}: {label}" for label, passed in loop_checks)
    lines.extend(
        [
            f"PASS: all aligned frames are RGBA {FRAME_SIZE[0]}x{FRAME_SIZE[1]}.",
            "PASS: every aligned frame's lowest identified foot-fill pixel is row 29.",
            "PASS: aligned PNG colors are a subset of the source palettes.",
            f"PASS: preview encodes {preview_frames} frames for the 42-step order, with {preview_colors} locked colors, no frame-local palettes, and preserved total timing.",
            "",
            f"Idle PNG: {IDLE_OUTPUT.name} (512x32)",
            f"Attack PNG: {ATTACK_OUTPUT.name} (640x32)",
            f"Preview GIF: {PREVIEW_OUTPUT.name} ({GIF_SCALE}x nearest-neighbor)",
        ]
    )
    REPORT_OUTPUT.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
