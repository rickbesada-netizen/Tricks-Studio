"""Build the x64 Windows GUI launcher using GCC and GNU binutils on Linux."""
from pathlib import Path
import struct
import subprocess
import sys
import tempfile

HERE = Path(__file__).resolve().parent
DLLS = {
    b"KERNEL32.dll": (
        "GetModuleFileNameW", "GetTempPathW", "GetTempFileNameW", "CreateFileW",
        "WriteFile", "CloseHandle", "DeleteFileW", "GetSystemDirectoryW",
        "SetEnvironmentVariableW", "GetCommandLineW", "CreateProcessW",
        "WaitForSingleObject", "GetExitCodeProcess", "ExitProcess",
    ),
    b"USER32.dll": ("MessageBoxW",),
}


def build(output):
    with tempfile.TemporaryDirectory() as temporary:
        work = Path(temporary)
        data = b"\xef\xbb\xbf" + (HERE / "editor.ps1").read_bytes()
        (work / "editor_bytes.h").write_text(
            "static const unsigned char editor[] = {" + ",".join(map(str, data)) + "};\n"
        )
        subprocess.run([
            "gcc", "-c", "-Os", "-fshort-wchar", "-ffreestanding",
            "-fno-stack-protector", "-fno-asynchronous-unwind-tables",
            "-fpie", "-fno-ident", "-I", str(work),
            str(HERE / "launcher.c"), "-o", str(work / "launcher.o"),
        ], check=True)
        layout = work / "layout.ld"
        layout.write_text("""
ENTRY(entry)
SECTIONS {
  .text 0x401000 : { *(.text*) }
  .iat 0x402000 : { *(.iat) }
  .rdata 0x403000 : { *(.rodata*) }
  .impdata 0x408000 : { *(.impdata) }
  .bss 0x409000 : { *(.bss*) *(COMMON) }
  /DISCARD/ : { *(.note*) *(.eh_frame*) }
}
""")
        linked = work / "linked"
        subprocess.run(["ld", "-T", str(layout), str(work / "launcher.o"),
                        "-o", str(linked)], check=True)
        listing = subprocess.check_output(["objdump", "-h", str(linked)], text=True)
        symbols = subprocess.check_output(["nm", str(linked)], text=True)
        entry = next(int(line.split()[0], 16) for line in symbols.splitlines()
                     if line.endswith(" T entry"))
        sections = []
        flags = {".text": 0x60000020, ".iat": 0xC0000040,
                 ".rdata": 0x40000040, ".impdata": 0x40000040, ".bss": 0xC0000080}
        for line in listing.splitlines():
            parts = line.split()
            if len(parts) < 6 or parts[1] not in flags:
                continue
            name = parts[1]
            length, address = int(parts[2], 16), int(parts[3], 16)
            content = b""
            if name != ".bss":
                raw = work / "section"
                subprocess.run(["objcopy", "-O", "binary", "-j", name, str(linked),
                                str(raw)], check=True)
                content = raw.read_bytes()
            sections.append((name, address - 0x400000, length, content, flags[name]))
        write_pe(output, entry - 0x400000, sections)
    install_import_table(output)


def align(value, boundary):
    return (value + boundary - 1) & -boundary


def write_pe(output, entry, sections):
    pe = 0x80
    opt = pe + 24
    header_size = align(opt + 240 + 40 * len(sections), 512)
    main = bytearray(header_size)
    main[:2] = b"MZ"
    struct.pack_into("<I", main, 60, pe)
    main[pe:pe + 4] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", main, pe + 4, 0x8664, len(sections), 0, 0, 0, 240, 0x23)
    struct.pack_into("<HBBIIIIIQII", main, opt, 0x20b, 1, 0,
                     align(len(sections[0][3]), 512),
                     sum(align(len(item[3]), 512) for item in sections[1:]),
                     next(item[2] for item in sections if item[0] == ".bss"),
                     entry, 0x1000, 0x400000, 4096, 512)
    struct.pack_into("<HHHHHHIIIIHHQQQQII", main, opt + 40,
                     6, 0, 0, 0, 6, 0, 0,
                     align(max(rva + length for _, rva, length, _, _ in sections), 4096),
                     header_size, 0, 2, 0x0100,
                     1048576, 4096, 1048576, 4096, 0, 16)
    for index, (name, rva, size, content, characteristics) in enumerate(sections):
        raw_size = align(len(content), 512)
        start = len(main) if raw_size else 0
        struct.pack_into("<8sIIIIIIHHI", main, opt + 240 + index * 40,
                         name.encode(), size, rva, raw_size, start, 0, 0, 0, 0, characteristics)
        main.extend(content)
        main.extend(bytes(raw_size - len(content)))
    output.write_bytes(main)


def install_import_table(output):
    binary = bytearray(output.read_bytes())
    pe = struct.unpack_from("<I", binary, 60)[0]
    count, = struct.unpack_from("<H", binary, pe + 6)
    optional_size, = struct.unpack_from("<H", binary, pe + 20)
    optional = pe + 24
    sections = {}
    for index in range(count):
        header = optional + optional_size + index * 40
        name, _, rva, size, offset = struct.unpack_from("<8sIIII", binary, header)
        sections[name.rstrip(b"\0")] = (rva, size, offset)
    data_rva, capacity, data_offset = sections[b".impdata"]
    iat_rva, _, iat_offset = sections[b".iat"]
    descriptors = (len(DLLS) + 1) * 20
    data = bytearray(descriptors)

    def add(value, alignment=1):
        data.extend(bytes((-len(data)) % alignment))
        rva = data_rva + len(data)
        data.extend(value)
        return rva

    index = 0
    for descriptor, (dll, functions) in enumerate(DLLS.items()):
        name = add(dll + b"\0")
        entries = [add(b"\0\0" + symbol.encode() + b"\0", 2) for symbol in functions]
        first_thunk = iat_rva + index * 8
        # The launcher indexes a contiguous function-pointer array. Each DLL
        # uses an independent lookup table to stop at that DLL's terminator.
        lookup = add(struct.pack("<" + "Q" * (len(entries) + 1), *entries, 0), 8)
        for entry in entries:
            struct.pack_into("<Q", binary, iat_offset + index * 8, entry)
            index += 1
        index += 1
        struct.pack_into("<IIIII", data, descriptor * 20, lookup, 0, 0, name, first_thunk)
    if len(data) > capacity:
        raise ValueError("Import section overflow")
    binary[data_offset:data_offset + len(data)] = data
    struct.pack_into("<II", binary, optional + 112 + 8, data_rva, descriptors)
    struct.pack_into("<II", binary, optional + 112 + 12 * 8, iat_rva, index * 8)
    output.write_bytes(binary)


if __name__ == "__main__":
    build(Path(sys.argv[1]).resolve())
