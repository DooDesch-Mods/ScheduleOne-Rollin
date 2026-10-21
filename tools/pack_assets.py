"""Copies the CC0 models and sounds Rollin embeds out of the downloaded kits (see CREDITS.md).

Models: each GLB is written as one self-contained file. A texture the source GLB only names by path (the Kenney
colormap kits) is read from beside it and stored inside the GLB, so the mod reads every model from one resource.
Sounds: converted with ffmpeg to raw 16-bit mono PCM at 22050 Hz, which the mod turns into an AudioClip.

Usage: python -I tools/pack_assets.py <downloads folder> <repo folder>
"""
import json
import os
import struct
import subprocess
import sys

MODELS = {
    # Rollin name: path under the downloads folder. The tuning parts copy the game's own meshes, so none are packed.
}

SOUNDS = {
    "bicycle": "oga-bicycle-horn/bicycle-horn-1.wav",
}


def read_glb(path):
    b = open(path, "rb").read()
    if b[:4] != b"glTF":
        raise ValueError(path + " is not a GLB file")
    off, js, bin_ = 12, None, b""
    while off + 8 <= len(b):
        ln, ty = struct.unpack_from("<II", b, off)
        chunk = b[off + 8: off + 8 + ln]
        if ty == 0x4E4F534A:
            js = json.loads(chunk)
        elif ty == 0x004E4942:
            bin_ = chunk
        off += 8 + ln
    return js, bytearray(bin_)


def pad(data, fill):
    while len(data) % 4:
        data += fill
    return data


def embed_images(js, bin_, folder):
    for image in js.get("images", []):
        uri = image.get("uri")
        if uri is None:
            continue
        data = open(os.path.join(folder, uri.replace("/", os.sep)), "rb").read()
        pad(bin_, b"\0")
        js.setdefault("bufferViews", []).append({"buffer": 0, "byteOffset": len(bin_), "byteLength": len(data)})
        bin_ += data
        image["bufferView"] = len(js["bufferViews"]) - 1
        image["mimeType"] = "image/png"
        del image["uri"]
    pad(bin_, b"\0")
    js["buffers"] = [{"byteLength": len(bin_)}]


def write_glb(path, js, bin_):
    j = pad(bytearray(json.dumps(js, separators=(",", ":")).encode("utf-8")), b" ")
    total = 12 + 8 + len(j) + 8 + len(bin_)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(j), 0x4E4F534A) + bytes(j))
        f.write(struct.pack("<II", len(bin_), 0x004E4942) + bytes(bin_))


def main(downloads, repo):
    parts = os.path.join(repo, "Assets", "parts")
    sounds = os.path.join(repo, "Assets", "sounds")
    os.makedirs(parts, exist_ok=True)
    os.makedirs(sounds, exist_ok=True)
    for name, rel in MODELS.items():
        src = os.path.join(downloads, rel)
        js, bin_ = read_glb(src)
        embed_images(js, bin_, os.path.dirname(src))
        out = os.path.join(parts, name + ".glb")
        write_glb(out, js, bin_)
        print("model", name, os.path.getsize(out), "bytes")
    for name, rel in SOUNDS.items():
        out = os.path.join(sounds, name + ".pcm")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", os.path.join(downloads, rel),
                        "-ac", "1", "-ar", "22050", "-f", "s16le", out], check=True)
        print("sound", name, os.path.getsize(out), "bytes")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
