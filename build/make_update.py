#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""生成自动更新用的静态目录：dist/update/{exe, version.json, _headers}

用法：
    python build/make_update.py --base https://rx-tools.pages.dev --notes "1.8.0 更新说明"
"""
from __future__ import print_function

import argparse
import hashlib
import io
import json
import os
import shutil
import sys

try:
    from urllib.parse import quote
except ImportError:
    from urllib import quote

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")


def sha256_of(path):
    h = hashlib.sha256()
    with io.open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1024 * 256), b""):
            h.update(chunk)
    return h.hexdigest()


def read_version():
    p = os.path.join(ROOT, "VERSION")
    if os.path.isfile(p):
        v = io.open(p, encoding="utf-8").read().strip()
        if v:
            return v
    p = os.path.join(ROOT, "build", "program.cs")
    if os.path.isfile(p):
        for line in io.open(p, encoding="utf-8"):
            if "AssemblyVersion" in line and '"' in line:
                return line.split('"')[1]
    return "0.0.0"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="https://rx-tools.pages.dev", help="站点根地址")
    ap.add_argument("--version", default="", help="版本号，默认读 VERSION 文件")
    ap.add_argument("--notes", default="", help="更新说明，换行用 \\n")
    ap.add_argument("--exe", default="", help="指定 exe 路径，默认取 dist/Rx工具箱.exe")
    ap.add_argument("--fallback", default="https://github.com/Dauskwn/rx-tools/releases", help="备用下载地址")
    ap.add_argument("--out", default=os.path.join(DIST, "update"), help="输出目录")
    args = ap.parse_args()

    exe = args.exe or os.path.join(DIST, "Rx工具箱.exe")
    if not os.path.isfile(exe):
        print("exe 不存在：" + exe, file=sys.stderr)
        return 1

    version = args.version or read_version()
    name = os.path.basename(exe)
    url = args.base.rstrip("/") + "/" + quote(name) if args.base else name
    notes = args.notes.replace("\\n", "\n")

    out = args.out
    if not os.path.isdir(out):
        os.makedirs(out)
    shutil.copy2(exe, os.path.join(out, name))

    meta = {
        "version": version,
        "url": url,
        "fallback": args.fallback,
        "sha256": sha256_of(exe),
        "notes": notes,
        "minVersion": "1.8.0",
    }
    with io.open(os.path.join(out, "version.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f, ensure_ascii=False, indent=2)
        f.write("\n")

    headers = (
        "/version.json\n"
        "  Cache-Control: no-store\n"
        "\n"
        "/*.exe\n"
        "  Cache-Control: public, max-age=3600\n"
        "  Content-Disposition: attachment\n"
    )
    with io.open(os.path.join(out, "_headers"), "w", encoding="utf-8", newline="\n") as f:
        f.write(headers)

    print("输出目录：" + out)
    print("  " + name + "  " + str(os.path.getsize(exe)) + " 字节")
    print("  version.json  version=" + version + " sha256=" + meta["sha256"][:16] + "...")
    return 0


if __name__ == "__main__":
    sys.exit(main())
