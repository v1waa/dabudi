#!/usr/bin/env python3
"""Extract a .NET single-file bundle (bundle manifest versions 1-6).

The format is defined by Microsoft.NET.HostModel.Bundle.Manifest/FileEntry.
This utility performs no execution of the input binary.
"""

from __future__ import annotations

import argparse
import json
import struct
import zlib
from pathlib import Path, PurePosixPath


BUNDLE_SIGNATURE = bytes.fromhex(
    "8b1202b96a612038727b930214d7a032"
    "13f5b9e6efae3318ee3b2dce24b36aae"
)

FILE_TYPES = {
    0: "Unknown",
    1: "Assembly",
    2: "NativeBinary",
    3: "DepsJson",
    4: "RuntimeConfigJson",
    5: "Symbols",
}


class Reader:
    def __init__(self, data: bytes, position: int = 0) -> None:
        self.data = data
        self.position = position

    def read(self, count: int) -> bytes:
        end = self.position + count
        if end > len(self.data):
            raise ValueError("Unexpected end of bundle manifest")
        value = self.data[self.position:end]
        self.position = end
        return value

    def unpack(self, fmt: str):
        size = struct.calcsize(fmt)
        return struct.unpack(fmt, self.read(size))[0]

    def read_7bit_int(self) -> int:
        value = 0
        shift = 0
        while shift < 35:
            byte = self.read(1)[0]
            value |= (byte & 0x7F) << shift
            if not byte & 0x80:
                return value
            shift += 7
        raise ValueError("Invalid 7-bit encoded integer")

    def read_string(self) -> str:
        length = self.read_7bit_int()
        return self.read(length).decode("utf-8")


def safe_output_path(root: Path, relative_path: str) -> Path:
    posix = PurePosixPath(relative_path.replace("\\", "/"))
    if posix.is_absolute() or ".." in posix.parts:
        raise ValueError(f"Unsafe path in bundle: {relative_path!r}")
    return root.joinpath(*posix.parts)


def parse_bundle(binary: bytes) -> dict:
    signature_offset = binary.find(BUNDLE_SIGNATURE)
    if signature_offset < 8:
        raise ValueError(".NET single-file bundle signature not found")

    header_offset = struct.unpack_from("<Q", binary, signature_offset - 8)[0]
    if header_offset >= len(binary):
        raise ValueError("Invalid bundle header offset")

    reader = Reader(binary, header_offset)
    major = reader.unpack("<I")
    minor = reader.unpack("<I")
    file_count = reader.unpack("<i")
    bundle_id = reader.read_string()

    header = {
        "major_version": major,
        "minor_version": minor,
        "file_count": file_count,
        "bundle_id": bundle_id,
        "header_offset": header_offset,
        "signature_offset": signature_offset,
    }

    if major >= 2:
        header.update(
            {
                "deps_json_offset": reader.unpack("<q"),
                "deps_json_size": reader.unpack("<q"),
                "runtimeconfig_json_offset": reader.unpack("<q"),
                "runtimeconfig_json_size": reader.unpack("<q"),
                "flags": reader.unpack("<Q"),
            }
        )

    entries = []
    for index in range(file_count):
        offset = reader.unpack("<q")
        size = reader.unpack("<q")
        compressed_size = reader.unpack("<q") if major >= 6 else 0
        file_type = reader.unpack("<B")
        relative_path = reader.read_string()
        entries.append(
            {
                "index": index,
                "offset": offset,
                "size": size,
                "compressed_size": compressed_size,
                "type": file_type,
                "type_name": FILE_TYPES.get(file_type, f"Type{file_type}"),
                "relative_path": relative_path,
            }
        )

    header["manifest_end_offset"] = reader.position
    header["entries"] = entries
    return header


def extract_entry(binary: bytes, entry: dict) -> bytes:
    stored_size = entry["compressed_size"] or entry["size"]
    start = entry["offset"]
    end = start + stored_size
    if start < 0 or end > len(binary):
        raise ValueError(f"Invalid payload range for {entry['relative_path']!r}")
    payload = binary[start:end]
    if entry["compressed_size"]:
        try:
            payload = zlib.decompress(payload, -zlib.MAX_WBITS)
        except zlib.error:
            payload = zlib.decompress(payload)
    if len(payload) != entry["size"]:
        raise ValueError(
            f"Size mismatch for {entry['relative_path']!r}: "
            f"expected {entry['size']}, got {len(payload)}"
        )
    return payload


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("bundle", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--list-only", action="store_true")
    args = parser.parse_args()

    binary = args.bundle.read_bytes()
    manifest = parse_bundle(binary)

    summary = {
        key: value for key, value in manifest.items() if key != "entries"
    }
    summary["entries"] = manifest["entries"]

    args.output.mkdir(parents=True, exist_ok=True)
    manifest_path = args.output / "bundle_manifest.json"
    manifest_path.write_text(
        json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8"
    )

    if not args.list_only:
        for entry in manifest["entries"]:
            target = safe_output_path(args.output, entry["relative_path"])
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(extract_entry(binary, entry))

    print(
        json.dumps(
            {
                "version": f"{manifest['major_version']}.{manifest['minor_version']}",
                "bundle_id": manifest["bundle_id"],
                "file_count": manifest["file_count"],
                "manifest": str(manifest_path),
                "extracted": not args.list_only,
            },
            ensure_ascii=False,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
