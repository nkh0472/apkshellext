# make-fixtures.ps1 - regenerate the fixtures under tests\ApkShellext2.Tests\fixtures.
#
# The fixtures are real bytes cut out of a real apk, not hand-written tables, so the
# parser tests see the byte shapes they meet in the wild. This script takes the source
# apk apart, asserts that the offsets below still describe its bytes, and then patches a
# handful of documented fields so the resource-table caps and the icon lookup rules can
# be driven deterministically. Every output is a byte-for-byte copy of the source except
# at the offsets listed here.
#
# Source apk: the public F-Droid build of org.intoorbit.spectrum 1.1.2
#             (Spectroid_org.intoorbit.spectrum_1.1.2(31).apk, 107,824 B,
#              resources.arsc 21,336 B stored, AndroidManifest.xml 2,584 B deflated)
#
# Layout this script depends on (offsets are absolute in the extracted entry):
#   resources.arsc   table hdr 12 | global string pool 12..4416 | package 0x7F @ 4416 (16,920 B)
#                    type 0x03 color    chunk @ 13068  (config_size @ +20 = 13088)
#                    type 0x04 drawable chunk @ 13428  (config_size @ +20 = 13448)
#                    type 0x07 menu     chunk @ 14836  (config_size @ +20 = 14856)
#                    type 0x09 string   chunk @ 16096  (entryCount  @ +12 = 16108)
#                    type 0x08 mipmap   entry 0x0000 key=ic_launcher (6 config chunks @ 14980..15500)
#                    type 0x09 string   119 entries in the default config, 85 in the fr config @ 18560
#   AndroidManifest.xml  pool @ 8 (36 strings, UTF-16, stringStart 172), resmap @ 1428 (68 B),
#                    application START_ELEMENT @ 1876 (attributeCount @ +28 = 1904),
#                    string 0 ("theme") u16len @ 180
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceApk,
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Minimal read-only walk of the resource table: just enough structure for the layout
# assertions and for the entry positions the patches are written at.
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Text;

public sealed class FixtureEntry
{
    public int Id;
    public int Pos;
    public int KeyIndex;
    public string Key;
}

public sealed class FixtureType
{
    public int Pos, Size, HeaderSize, TypeId, Flags, EntryCount, EntryStart, ConfigSize;
    public List<FixtureEntry> Entries = new List<FixtureEntry>();
    public int End { get { return Pos + Size; } }
}

public sealed class FixturePackage
{
    public int Pos, Size, HeaderSize, TypePoolPos, KeyPoolPos;
    public uint Id;
    public List<FixtureType> Types = new List<FixtureType>();
    public int End { get { return Pos + Size; } }
}

public static class FixtureArsc
{
    static ushort U16(byte[] b, int o) { return BitConverter.ToUInt16(b, o); }
    static int I32(byte[] b, int o) { return BitConverter.ToInt32(b, o); }
    static uint U32(byte[] b, int o) { return BitConverter.ToUInt32(b, o); }

    static string ReadPool(byte[] b, int poolPos, int index)
    {
        int hdr = U16(b, poolPos + 2);
        int count = I32(b, poolPos + 8);
        int flags = I32(b, poolPos + 16);
        int start = I32(b, poolPos + 20);
        if (index < 0 || index >= count) return "<oob>";
        int off = index * 4 + hdr;
        int spos = I32(b, poolPos + off);
        int p = poolPos + start + spos;
        if ((flags & 0x100) != 0)
        {
            int u16 = b[p]; p++;
            if ((u16 & 0x80) != 0) { u16 = ((u16 & 0x7F) << 8) + b[p]; p++; }
            int u8 = b[p]; p++;
            if ((u8 & 0x80) != 0) { u8 = ((u8 & 0x7F) << 8) + b[p]; p++; }
            return Encoding.UTF8.GetString(b, p, u8);
        }
        int len = U16(b, p); p += 2;
        if ((len & 0x8000) != 0) { len = ((len & 0x7FFF) << 16) + U16(b, p); p += 2; }
        return Encoding.Unicode.GetString(b, p, len * 2);
    }

    public static List<FixturePackage> Parse(byte[] t)
    {
        var list = new List<FixturePackage>();
        int pkgCount = I32(t, 8);
        int pos = 12 + I32(t, 12 + 4);          // skip the global string pool
        for (int p = 0; p < pkgCount && pos + 288 <= t.Length; p++)
        {
            var pkg = new FixturePackage();
            pkg.Pos = pos;
            pkg.HeaderSize = U16(t, pos + 2);
            pkg.Size = I32(t, pos + 4);
            pkg.Id = U32(t, pos + 8);
            pkg.TypePoolPos = pos + I32(t, pos + 268);
            pkg.KeyPoolPos = pos + I32(t, pos + 276);
            if (pkg.Size < pkg.HeaderSize) break;
            int c = pos + pkg.HeaderSize;
            while (c + 8 <= pkg.End && c + 8 <= t.Length)
            {
                int ct = U16(t, c), chs = U16(t, c + 2), cs = I32(t, c + 4);
                if (chs < 8 || cs < 8 || c + cs > t.Length) break;
                if (ct == 0x0201 && chs >= 24)
                {
                    var ty = new FixtureType();
                    ty.Pos = c; ty.Size = cs; ty.HeaderSize = chs;
                    ty.TypeId = t[c + 8]; ty.Flags = t[c + 9];
                    ty.EntryCount = I32(t, c + 12); ty.EntryStart = I32(t, c + 16); ty.ConfigSize = I32(t, c + 20);
                    int idx = c + chs;
                    int limit = Math.Min(ty.EntryCount, 60000);
                    for (int i = 0; i < limit; i++)
                    {
                        int eid; long eoff;
                        if ((ty.Flags & 0x02) != 0 && (ty.Flags & 0x01) == 0)
                        {
                            if (idx + i * 2 + 2 > c + cs) break;
                            int raw = U16(t, idx + i * 2);
                            if (raw == 0xFFFF) continue;
                            eid = i; eoff = (long)raw * 4;
                        }
                        else if ((ty.Flags & 0x01) != 0)
                        {
                            if (idx + i * 4 + 4 > c + cs) break;
                            eid = U16(t, idx + i * 4);
                            eoff = (long)U16(t, idx + i * 4 + 2) * 4;
                        }
                        else
                        {
                            if (idx + i * 4 + 4 > c + cs) break;
                            uint raw = U32(t, idx + i * 4);
                            if (raw == 0xFFFFFFFF) continue;
                            eid = i; eoff = raw;
                        }
                        long ep = c + ty.EntryStart + eoff;
                        if (ep + 16 > c + cs) break;
                        var en = new FixtureEntry();
                        en.Id = eid; en.Pos = (int)ep;
                        en.KeyIndex = (int)U32(t, (int)ep + 4);
                        en.Key = ReadPool(t, pkg.KeyPoolPos, en.KeyIndex);
                        ty.Entries.Add(en);
                    }
                    pkg.Types.Add(ty);
                }
                c += cs;
            }
            list.Add(pkg);
            pos = pkg.End;
        }
        return list;
    }
}
'@

function Get-ApkEntry([string]$apk, [string]$name) {
    $z = [IO.Compression.ZipFile]::OpenRead($apk)
    try {
        $e = $z.GetEntry($name)
        if ($null -eq $e) { throw "entry $name not in $apk" }
        $ms = New-Object IO.MemoryStream
        $s = $e.Open(); $s.CopyTo($ms); $s.Dispose()
        return $ms.ToArray()
    } finally { $z.Dispose() }
}

function Set-U16([byte[]]$b, [int]$o, [int]$v) { [BitConverter]::GetBytes([uint16]$v).CopyTo($b, $o) }
function Set-U32([byte[]]$b, [int]$o, [uint32]$v) { [BitConverter]::GetBytes([uint32]$v).CopyTo($b, $o) }

function Save([string]$name, [byte[]]$bytes) {
    $p = Join-Path $OutDir $name
    [IO.File]::WriteAllBytes($p, $bytes)
    $sha = (Get-FileHash $p -Algorithm SHA256).Hash
    "{0,-32} {1,7} B  {2}" -f $name, $bytes.Length, $sha
}

if (-not (Test-Path -LiteralPath $SourceApk)) { throw "source apk not found: $SourceApk" }
if (-not (Test-Path -LiteralPath $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$arsc = Get-ApkEntry $SourceApk 'resources.arsc'
$xml = Get-ApkEntry $SourceApk 'AndroidManifest.xml'

# The offsets below are only valid for the source build named in the header. Refuse to
# patch anything unless the extracted table still has the expected shape.
$pkgs = [FixtureArsc]::Parse($arsc)
$pkg = $pkgs[0]
if ($pkgs.Count -ne 1 -or $pkg.Id -ne 0x7F -or $pkg.Pos -ne 4416 -or $pkg.Size -ne 16920) {
    throw "unexpected package layout: count=$($pkgs.Count) id=0x$('{0:X}' -f $pkg.Id) pos=$($pkg.Pos) size=$($pkg.Size)"
}
$stringType = $pkg.Types | Where-Object { $_.TypeId -eq 9 -and $_.Pos -eq 16096 }
if ($null -eq $stringType -or $stringType.Entries.Count -ne 119) { throw 'unexpected string type chunk' }
$mipmap = $pkg.Types | Where-Object { $_.TypeId -eq 8 -and $_.Pos -eq 14980 }
if ($null -eq $mipmap -or $mipmap.Entries[0].Key -ne 'ic_launcher') { throw 'unexpected mipmap entry' }
$frType = $pkg.Types | Where-Object { $_.TypeId -eq 9 -and $_.Pos -eq 18560 }
if ($null -eq $frType) { throw 'unexpected fr string type chunk' }

# ---- 1/2: the untouched real bytes ------------------------------------------------
Save 'spectroid-resources.arsc' $arsc
Save 'spectroid-manifest.bin'   $xml

# ---- 3: ARSC config_size gates ---------------------------------------------------
# cap: color chunk config_size 8192 > MaxResourceConfigSize (1024)
# underflow: menu chunk config_size 2 -> configBytes = -2
# chunk remainder: drawable chunk config_size 1024 but only 96 bytes are left in the chunk
$b = [byte[]]$arsc.Clone()
Set-U32 $b 13088 8192
Set-U32 $b 14856 2
Set-U32 $b 13448 1024
Save 'spectroid-arsc-configsize.bin' $b

# ---- 4: packageCount gate (256 cap) ----------------------------------------------
$b = [byte[]]$arsc.Clone()
Set-U32 $b 8 0x200
Save 'spectroid-arsc-packages.bin' $b

# ---- 5: entryCount gate (0x10000 cap) --------------------------------------------
$b = [byte[]]$arsc.Clone()
Set-U32 $b 16108 0x20000
Save 'spectroid-arsc-entries.bin' $b

# ---- 6: 40-hop reference chain (MaxResourceRefDepth = 32) -------------------------
# Entries 0x0000..0x0027 of the default config chunk are rewritten to TYPE_REFERENCE
# pointing at the next id (0x7F090000+i -> 0x7F090000+i+1); 0x0028 keeps its real
# TYPE_STRING. The same entries in the fr chunk are marked absent (0xffffffff), so every
# hop contributes exactly one value and the walk stays linear instead of branching per
# config.
function Set-Absent([byte[]]$b, $chunk, [int]$i) { Set-U32 $b ($chunk.Pos + $chunk.HeaderSize + $i * 4) ([uint32]::MaxValue) }
$b = [byte[]]$arsc.Clone()
for ($i = 0; $i -le 0x27; $i++) {
    $pos = $stringType.Entries[$i].Pos
    if ($stringType.Entries[$i].Id -ne $i) { throw "entry $i id mismatch" }
    Set-U16 $b ($pos + 8) 8          # Res_value.size
    $b[$pos + 10] = 0                # Res_value.res0
    $b[$pos + 11] = 1                # Res_value.dataType = TYPE_REFERENCE
    Set-U32 $b ($pos + 12) ([uint32](0x7F090000 + $i + 1))
    Set-Absent $b $frType $i         # same id in the fr chunk: no value -> linear chain
}
Save 'spectroid-arsc-chain.bin' $b

# ---- 6b: self reference (an entry whose Res_value points back at itself) -----------
# Entry 0x0000 of the default chunk points back at 0x7F090000 (and the fr twin is marked
# absent). The guard stops after the first visit: no recursion, empty resource.
$b = [byte[]]$arsc.Clone()
$pos = $stringType.Entries[0].Pos
Set-U16 $b ($pos + 8) 8
$b[$pos + 10] = 0
$b[$pos + 11] = 1
Set-U32 $b ($pos + 12) ([uint32]0x7F090000)
Set-Absent $b $frType 0
Save 'spectroid-arsc-selfref.bin' $b

# ---- 7: duplicate package id (the smallest id wins) -------------------------------
# The same package chunk is appended a second time with package id 0x02: every name now
# exists in two packages, and the lookup must return the smaller (0x7F..) id.
$b = New-Object byte[] ($arsc.Length + $pkg.Size)
[Array]::Copy($arsc, 0, $b, 0, $arsc.Length)
[Array]::Copy($arsc, $pkg.Pos, $b, $arsc.Length, $pkg.Size)
Set-U32 $b 8 2
Set-U32 $b ($arsc.Length + 8) 0x02
Save 'spectroid-arsc-dup.bin' $b

# ---- 8: AXML attribute walk past the chunk end ------------------------------------
# application START_ELEMENT claims 8 attributes; each is 20 B, the chunk is only 56 B,
# so the walk leaves the chunk and must be refused instead of reading a neighbour chunk.
$b = [byte[]]$xml.Clone()
Set-U16 $b 1904 8
Save 'spectroid-axml-attr-oob.bin' $b

# ---- 9: AXML string pool entry longer than the buffer ----------------------------
$b = [byte[]]$xml.Clone()
Set-U16 $b 180 0x7FFF
Save 'spectroid-axml-pool-oob.bin' $b

# ---- 10: AXML resource map chunk smaller than the id needs -----------------------
$b = [byte[]]$xml.Clone()
Set-U32 $b 1432 8
Save 'spectroid-axml-resmap-oob.bin' $b
