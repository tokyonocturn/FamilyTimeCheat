import struct, sys

path = sys.argv[1]
d = open(path, "rb").read()
e = struct.unpack_from("<I", d, 0x3C)[0]
if d[e:e+4] != b"PE\0\0":
    sys.exit("not PE")
machine = struct.unpack_from("<H", d, e+4)[0]
nsec = struct.unpack_from("<H", d, e+6)[0]
opt = e + 24
magic = struct.unpack_from("<H", d, opt)[0]
pe32plus = magic == 0x20B
ddoff = opt + (112 if pe32plus else 96)
exp_rva = struct.unpack_from("<I", d, ddoff)[0]

secs = []
so = opt + struct.unpack_from("<H", d, e+20)[0]
for i in range(nsec):
    b = so + 40*i
    vs, va, rs, pr = struct.unpack_from("<IIII", d, b+8)
    secs.append((va, vs, pr, rs))

def r2o(rva):
    for va, vs, pr, rs in secs:
        if va <= rva < va + max(vs, rs):
            return pr + (rva - va)
    return None

print("machine=0x%04x %s" % (machine, "x64" if machine == 0x8664 else "x86"))
print("export_dir_rva=%d" % exp_rva)
if exp_rva == 0:
    sys.exit("no export directory")

eo = r2o(exp_rva)
nfun, nnam = struct.unpack_from("<II", d, eo+20)
an = r2o(struct.unpack_from("<I", d, eo+32)[0])
names = []
for i in range(nnam):
    o = r2o(struct.unpack_from("<I", d, an + 4*i)[0])
    end = d.index(b"\0", o)
    names.append(d[o:end].decode("latin-1"))
print("total_exports=%d" % len(names))
print("---ALL---")
for n in sorted(names):
    print(n)
