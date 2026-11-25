"""Build synthetic JARs and fixtures for a disposable Suwayomi audit instance."""

import argparse
import struct
import subprocess
import zlib
from hashlib import sha256
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile


def png_chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))


def fixture_png():
    return (
        b"\x89PNG\r\n\x1a\n"
        + png_chunk(b"IHDR", struct.pack(">IIBBBBB", 2, 2, 8, 2, 0, 0, 0))
        + png_chunk(b"IDAT", zlib.compress(b"\0\x20\x80\xf0\x20\x80\xf0" * 2))
        + png_chunk(b"IEND", b"")
    )


def make_fixture(server_jar, gradle_lib, output):
    source = Path(__file__).with_name("SyntheticSource.kt")
    with TemporaryDirectory() as temporary:
        classes = Path(temporary)
        subprocess.run(
            ["java", "-cp", str(gradle_lib / "*"), "org.jetbrains.kotlin.cli.jvm.K2JVMCompiler",
             "-no-stdlib", "-no-reflect", "-classpath", str(server_jar),
             "-d", str(classes), str(source)],
            check=True,
        )
        output.mkdir(parents=True)
        for code, version in enumerate(("1.4.1", "1.4.2", "1.7.1"), start=1):
            manifest = f'''<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="org.mangaorganizer.audit.fixture" android:versionName="{version}" android:versionCode="{code}">
  <uses-feature android:name="tachiyomi.extension"/>
  <application android:label="Tachiyomi: Audit Fixture">
    <meta-data android:name="tachiyomi.extension.class" android:value=".SyntheticSource"/>
  </application>
</manifest>'''
            with ZipFile(output / f"audit-fixture-{code}.jar", "w") as archive:
                archive.writestr("AndroidManifest.xml", manifest)
                for file in sorted(classes.rglob("*.class")):
                    archive.write(file, file.relative_to(classes))

    site = output / "site"
    site.mkdir()
    for name in ("fixture.html", "chapter.html"):
        (site / name).write_text("Synthetic local audit fixture", encoding="utf-8")
    page = fixture_png()
    (site / "001.png").write_bytes(page)
    print(f"Synthetic page SHA-256 {sha256(page).hexdigest()}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("server_jar", type=Path)
    parser.add_argument("gradle_lib", type=Path, help="lib directory of the Gradle 9.9.2 distribution")
    parser.add_argument("output", type=Path, help="new fixture directory")
    args = parser.parse_args()
    make_fixture(args.server_jar, args.gradle_lib, args.output)
