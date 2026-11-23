# Parser fixtures

These files are **real byte ranges cut out of a real apk**, not hand-written tables. The
generator patches a handful of documented fields on top of them so the resource-table caps and
the icon lookup steps can be exercised deterministically.

- Source apk: `Spectroid_org.intoorbit.spectrum_1.1.2(31).apk`, the public F-Droid build
  (107,824 B; `resources.arsc` 21,336 B stored, `AndroidManifest.xml` 2,584 B deflated)
- Generator: `make-fixtures.ps1` (asserts the source layout before patching, then writes this
  directory and prints every SHA-256)
- Parser-side dump tool: `ArscTool.cs` (`ArscTool.Parse/ReadPool/Dump`,
  `AxmlTool.Pools/Chunks/Dump`)
- Behaviour probe: `verify-fixtures.ps1` (drives the shipped `ApkQuickReader.AxmlParser` /
  `ArscParser` through reflection; its output is what the NUnit assertions were written against)

The generator, the dump tool and the probe are development-time tools; they are not part of
this repository.

Source layout (absolute offsets inside the extracted file):

| offset | what |
| --- | --- |
| 0 | `resources.arsc` table header (12 B) |
| 12 | global string pool, 4,404 B (230 strings) |
| 4,416 | package 0x7F, 16,920 B, header 288 (`org.intoorbit.spectrum`) |
| 4,704 | package typeStrings pool |
| 4,944 | package keyStrings pool |
| 13,068 | type 0x03 color chunk (config_size at +20 = 13,088) |
| 13,428 | type 0x04 drawable chunk (config_size at +20 = 13,448) |
| 14,836 | type 0x07 menu chunk (config_size at +20 = 14,856) |
| 14,980 | type 0x08 mipmap chunk, entry 0 key = `ic_launcher` |
| 15,500 | five more mipmap density chunks |
| 16,096 | type 0x09 string chunk, 119 entries, entryCount at +12 = 16,108 |
| 18,560 | type 0x09 string chunk, fr config, 85 entries |
| 8 | `AndroidManifest.xml` string pool (36 strings, UTF-16, stringStart 172) |
| 180 | string 0 (`theme`) u16 length |
| 1,428 | resource map chunk (68 B) |
| 1,876 | `application` START_ELEMENT (4 attributes, attributeCount at +28 = 1,904) |

Fixture patches:

| file | patch | what it pins |
| --- | --- | --- |
| `spectroid-resources.arsc` | none | baseline lookups |
| `spectroid-manifest.bin` | none | baseline manifest parse |
| `spectroid-arsc-configsize.bin` | colour chunk config_size = 8,192; menu chunk = 2; drawable chunk = 1,024 (96 B left) | `MaxResourceConfigSize` cap / underflow / past-chunk-end |
| `spectroid-arsc-packages.bin` | table offset 8 = 0x200 | `MaxResourcePackages` cap |
| `spectroid-arsc-entries.bin` | string chunk entryCount = 0x20000 | `MaxResourceEntries` cap |
| `spectroid-arsc-chain.bin` | string entries 0x00..0x27 -> TYPE_REFERENCE to the next id, fr twins marked absent (0xffffffff) | `MaxResourceRefDepth` (31 hops resolve, 32 do not) |
| `spectroid-arsc-selfref.bin` | string entry 0x00 -> TYPE_REFERENCE to itself | self-reference guard |
| `spectroid-arsc-dup.bin` | package chunk appended a second time with package id 0x02, packageCount 1 -> 2 | smallest id wins (0x02080000, not 0x7F080000) |
| `spectroid-axml-attr-oob.bin` | `application` attributeCount 4 -> 8 | attribute walk past the element chunk end |
| `spectroid-axml-pool-oob.bin` | string 0 u16 length = 0x7FFF | string pool entry past the buffer |
| `spectroid-axml-resmap-oob.bin` | resource map id 0 = 8 | resource map rejection |

The tests read these through `RepoLayout.PathOf`, so they run against the checked-in bytes
rather than a copy in the output directory.
