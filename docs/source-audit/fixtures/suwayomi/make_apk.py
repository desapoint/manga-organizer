"""Package the synthetic Suwayomi source as a disposable Android-style APK."""

import argparse
import subprocess
from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile


def make_apk(fixture_jar, android_jar, build_tools, output):
    output = output.resolve()
    with TemporaryDirectory(prefix="manga-audit-apk-") as directory:
        work = Path(directory)
        manifest = work / "AndroidManifest.xml"
        with ZipFile(fixture_jar) as archive:
            manifest_xml = archive.read("AndroidManifest.xml").decode()
        manifest.write_text(manifest_xml.replace('android:label="Tachiyomi: Audit Fixture"',
                                                  'android:label="@string/audit_name"'), encoding="utf-8")
        resources = work / "res" / "values"
        resources.mkdir(parents=True)
        (resources / "strings.xml").write_text(
            '<resources><string name="audit_name">Tachiyomi: Audit Fixture</string></resources>', encoding="utf-8",
        )
        compiled = work / "resources.zip"
        subprocess.run([build_tools / "aapt2", "compile", "--dir", resources.parent, "-o", compiled], check=True)

        unsigned = work / "unsigned.apk"
        subprocess.run([build_tools / "aapt2", "link", "--manifest", manifest,
                        "--min-sdk-version", "26", "--target-sdk-version", "36",
                        "-I", android_jar, "-o", unsigned, compiled], check=True)
        subprocess.run([build_tools / "d8", "--lib", android_jar, "--min-api", "26",
                        "--output", work, fixture_jar], check=True)
        with ZipFile(unsigned, "a") as archive:
            archive.write(work / "classes.dex", "classes.dex")

        aligned = work / "aligned.apk"
        subprocess.run([build_tools / "zipalign", "-f", "4", unsigned, aligned], check=True)
        key = work / "fixture.p12"
        subprocess.run(["keytool", "-genkeypair", "-keyalg", "RSA", "-keysize", "2048", "-validity", "30",
                        "-dname", "CN=Disposable Manga Organizer Audit", "-storetype", "PKCS12",
                        "-keystore", key, "-alias", "fixture", "-storepass", "synthetic"], check=True)
        subprocess.run([build_tools / "apksigner", "sign", "--ks", key, "--ks-pass", "pass:synthetic",
                        "--out", output, aligned], check=True)
        subprocess.run([build_tools / "apksigner", "verify", output], check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("fixture_jar", type=Path)
    parser.add_argument("android_jar", type=Path)
    parser.add_argument("build_tools", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    make_apk(args.fixture_jar.resolve(), args.android_jar.resolve(), args.build_tools.resolve(), args.output)
